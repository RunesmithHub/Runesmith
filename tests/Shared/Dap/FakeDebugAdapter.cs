using System.Globalization;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Runesmith.Tests.Dap;

/// <summary>A debug adapter at the other end of two pipes: it answers each request with what the test scripted for its command, records
/// what it got, and sends events and requests of its own when the test asks.</summary>
internal sealed class FakeDebugAdapter : IAsyncDisposable
{
    private readonly AnonymousPipeServerStream toAdapter = new(PipeDirection.Out);
    private readonly AnonymousPipeServerStream toClient = new(PipeDirection.Out);
    private readonly Stream input;
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Func<JsonObject?, FakeReply>> handlers = new(StringComparer.Ordinal);
    private readonly List<(string Command, JsonObject? Arguments)> received = [];
    private readonly Dictionary<int, TaskCompletionSource<JsonObject>> answers = [];
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task loop;
    private int seq;

    public FakeDebugAdapter()
    {
        input = new AnonymousPipeClientStream(PipeDirection.In, toAdapter.ClientSafePipeHandle);
        ClientInput = new AnonymousPipeClientStream(PipeDirection.In, toClient.ClientSafePipeHandle);
        loop = Task.Run(ReadLoopAsync);
    }

    /// <summary>Gets the stream the client reads the adapter's messages from.</summary>
    public Stream ClientInput { get; }

    /// <summary>Gets the stream the client writes its messages to.</summary>
    public Stream ClientOutput => toAdapter;

    /// <summary>Raised, on the adapter's reading task before it answers, for each request.</summary>
    public event Action<string, JsonObject?>? RequestReceived;

    /// <summary>Answers a command with a reply made from its arguments; commands without one succeed without a body.</summary>
    public void On(string command, Func<JsonObject?, FakeReply> reply) => handlers[command] = reply;

    public void On(string command, string body) => On(command, _ => FakeReply.Success(body));

    /// <summary>Gets the commands received so far, in order.</summary>
    public IReadOnlyList<string> Commands
    {
        get
        {
            lock (received)
                return [.. received.Select(r => r.Command)];
        }
    }

    /// <summary>Gets the arguments of the last request with a command.</summary>
    public JsonObject? Arguments(string command)
    {
        lock (received)
            return received.LastOrDefault(r => r.Command == command).Arguments;
    }

    /// <summary>Gets the arguments of every request with a command, in order.</summary>
    public IReadOnlyList<JsonObject?> AllArguments(string command)
    {
        lock (received)
            return [.. received.Where(r => r.Command == command).Select(r => r.Arguments)];
    }

    /// <summary>Waits until a request with a command has come.</summary>
    public async Task WaitForAsync(string command, int count = 1)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            lock (received)
            {
                if (received.Count(r => r.Command == command) >= count)
                    return;
            }

            await Task.Delay(5, timeout.Token);
        }
    }

    public Task SendEventAsync(string name, string? body = null) =>
        WriteAsync(new JsonObject { ["seq"] = Next(), ["type"] = "event", ["event"] = name, ["body"] = body is null ? null : JsonNode.Parse(body) });

    /// <summary>Sends a request to the client and waits for its response.</summary>
    public async Task<JsonObject> SendRequestAsync(string command, string arguments)
    {
        var number = Next();
        var answer = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (answers)
            answers[number] = answer;
        await WriteAsync(new JsonObject { ["seq"] = number, ["type"] = "request", ["command"] = command, ["arguments"] = JsonNode.Parse(arguments) });
        return await answer.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>Writes raw bytes to the client, such as a message split in parts.</summary>
    public async Task WriteRawAsync(byte[] bytes)
    {
        await writeLock.WaitAsync();
        try
        {
            await toClient.WriteAsync(bytes);
            await toClient.FlushAsync();
        }
        finally
        {
            writeLock.Release();
        }
    }

    /// <summary>Ends the adapter's output as a crash would.</summary>
    public void Crash() => toClient.Dispose();

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        toClient.Dispose();
        toAdapter.Dispose();
        input.Dispose();
        try
        {
            await loop;
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
        }

        ClientInput.Dispose();
        lifetime.Dispose();
    }

    /// <summary>Frames a message as the protocol does.</summary>
    public static byte[] Frame(string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        return [.. Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"Content-Length: {body.Length}\r\n\r\n")), .. body];
    }

    private int Next() => Interlocked.Increment(ref seq);

    private Task WriteAsync(JsonObject message) => WriteRawAsync(Frame(message.ToJsonString()));

    private async Task ReadLoopAsync()
    {
        while (!lifetime.IsCancellationRequested && await ReadMessageAsync() is { } message)
        {
            if (message["type"]?.GetValue<string>() == "response")
            {
                TaskCompletionSource<JsonObject>? answer;
                lock (answers)
                    answers.Remove(message["request_seq"]!.GetValue<int>(), out answer);
                answer?.TrySetResult(message);
                continue;
            }

            var command = message["command"]!.GetValue<string>();
            var arguments = message["arguments"] as JsonObject;
            lock (received)
                received.Add((command, arguments));
            RequestReceived?.Invoke(command, arguments);
            var reply = handlers.TryGetValue(command, out var handler) ? handler(arguments) : FakeReply.Success();
            if (reply.Silent)
                continue;

            var response = new JsonObject
            {
                ["seq"] = Next(),
                ["type"] = "response",
                ["request_seq"] = message["seq"]!.GetValue<int>(),
                ["command"] = command,
                ["success"] = reply.Error is null,
            };
            if (reply.Error is not null)
                response["message"] = reply.Error;
            if (reply.Body is not null)
                response["body"] = JsonNode.Parse(reply.Body);
            try
            {
                if (reply.Delay > TimeSpan.Zero)
                    _ = Task.Delay(reply.Delay).ContinueWith(_ => WriteAsync(response), TaskScheduler.Default);
                else
                    await WriteAsync(response);
                foreach (var (name, body) in reply.Then)
                    await SendEventAsync(name, body);
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                return;
            }
        }
    }

    private async Task<JsonObject?> ReadMessageAsync()
    {
        var length = -1;
        while (true)
        {
            var line = await ReadLineAsync();
            if (line is null)
                return null;
            if (line.Length == 0)
                break;
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                length = int.Parse(line["Content-Length:".Length..].Trim(), CultureInfo.InvariantCulture);
        }

        var body = new byte[length];
        var read = 0;
        while (read < length)
        {
            var count = await input.ReadAsync(body.AsMemory(read), lifetime.Token);
            if (count == 0)
                return null;
            read += count;
        }

        return JsonNode.Parse(body)!.AsObject();
    }

    private async Task<string?> ReadLineAsync()
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        while (true)
        {
            if (await input.ReadAsync(one, lifetime.Token) == 0)
                return null;
            if (one[0] == '\n' && bytes.Count > 0 && bytes[^1] == '\r')
                return Encoding.ASCII.GetString([.. bytes.Take(bytes.Count - 1)]);
            bytes.Add(one[0]);
        }
    }
}

/// <summary>How the fake adapter answers a request.</summary>
internal sealed record FakeReply(string? Body, string? Error)
{
    /// <summary>Gets events to send right after the answer, in order: their names and bodies.</summary>
    public IReadOnlyList<(string Name, string? Body)> Then { get; init; } = [];

    /// <summary>Gets whether the request gets no answer at all.</summary>
    public bool Silent { get; init; }

    /// <summary>Gets how long to wait before answering, without holding up other requests.</summary>
    public TimeSpan Delay { get; init; }

    public static FakeReply Success(string? body = null) => new(body, null);

    public static FakeReply Failure(string message, string? body = null) => new(body, message);

    public static FakeReply None { get; } = new(null, null) { Silent = true };
}
