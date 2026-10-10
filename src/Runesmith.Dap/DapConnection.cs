using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;
using Runesmith.Dap.Protocol;

namespace Runesmith.Dap;

/// <summary>The Debug Adapter Protocol's base protocol over a pair of streams: numbered requests and their responses, events, and requests
/// from the adapter.</summary>
/// <remarks>Events and the adapter's requests are handled one at a time in the order they arrive, on a task of their own, so a slow handler
/// never holds up reading responses.</remarks>
internal sealed class DapConnection : IAsyncDisposable
{
    private readonly Stream _input;
    private readonly MessageWriter _writer;
    private readonly ConcurrentDictionary<int, Pending> _pending = new();
    private readonly Channel<Action> _incoming = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _lifetime = new();
    private int _nextSeq;
    private Task? _readLoop;
    private Task? _dispatchLoop;
    private int _closed;
    private int _disposed;

    public DapConnection(Stream input, Stream output)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _writer = new MessageWriter(output ?? throw new ArgumentNullException(nameof(output)));
    }

    /// <summary>Gets the options messages are serialized with: the protocol types' generated metadata, then reflection for other types.</summary>
    public static JsonSerializerOptions SerializerOptions { get; } = new(DapJsonContext.Default.Options)
    {
        TypeInfoResolver = JsonTypeInfoResolver.Combine(DapJsonContext.Default, new DefaultJsonTypeInfoResolver()),
    };

    /// <summary>Gets or sets what handles events: the event's name and body. It runs on the dispatch task, in order.</summary>
    public Action<string, JsonElement?>? EventHandler { get; set; }

    /// <summary>Gets or sets what answers the adapter's requests, such as <c>runInTerminal</c>; without one they fail.</summary>
    public Func<string, JsonElement?, CancellationToken, Task<object?>>? RequestHandler { get; set; }

    /// <summary>Gets or sets what runs when a request is canceled before its answer, with the request's sequence number.</summary>
    public Action<int>? Canceled { get; set; }

    /// <summary>Gets whether the connection has closed.</summary>
    public bool IsClosed => Volatile.Read(ref _closed) != 0;

    /// <summary>Raised for every message sent or received; the JSON is only produced while there are subscribers.</summary>
    public event Action<DapTrace>? Trace;

    /// <summary>Raised once when the connection closes, after the events received before that, with the exception that closed it, or null at
    /// the end of the input.</summary>
    public event EventHandler<Exception?>? Closed;

    public void Start()
    {
        if (_readLoop is not null)
            throw new InvalidOperationException("The connection has already started.");
        _dispatchLoop = Task.Run(DispatchAsync);
        _readLoop = Task.Run(ReadLoopAsync);
    }

    /// <summary>Sends a request and waits for the body of its successful answer.</summary>
    /// <exception cref="DebugAdapterException">The adapter answered with an error.</exception>
    /// <exception cref="IOException">The connection closed before the answer came.</exception>
    /// <exception cref="OperationCanceledException">The token was canceled; <see cref="Canceled"/> has been told.</exception>
    public async Task<JsonElement?> SendRequestAsync(string command, object? arguments, CancellationToken cancellationToken)
    {
        if (IsClosed)
            throw new IOException($"The debug adapter is not running, so {command} was not sent.");
        cancellationToken.ThrowIfCancellationRequested();

        var seq = Interlocked.Increment(ref _nextSeq);
        var pending = new Pending(command);
        _pending[seq] = pending;
        await using var registration = cancellationToken.Register(() =>
        {
            if (_pending.TryRemove(seq, out var removed) && removed.Completion.TrySetCanceled(cancellationToken))
                Canceled?.Invoke(seq);
        }).ConfigureAwait(false);

        try
        {
            await WriteAsync(writer =>
            {
                writer.WriteNumber("seq", seq);
                writer.WriteString("type", "request");
                writer.WriteString("command", command);
                WriteValue(writer, "arguments", arguments);
            }).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _pending.TryRemove(seq, out _);
            throw new IOException($"{command} could not be sent: the debug adapter's input is closed.", exception);
        }

        return await pending.Completion.Task.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _lifetime.CancelAsync().ConfigureAwait(false);
        Close(null);
        if (_readLoop is not null)
            await _readLoop.ConfigureAwait(false);
        if (_dispatchLoop is not null)
            await _dispatchLoop.ConfigureAwait(false);
        _lifetime.Dispose();
        _writer.Dispose();
    }

    internal static T? Read<T>(JsonElement? body)
    {
        if (body is not { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } element)
            return default;
        return (T?)element.Deserialize(SerializerOptions.GetTypeInfo(typeof(T)));
    }

    private static void WriteValue(Utf8JsonWriter writer, string name, object? value)
    {
        if (value is null)
            return;
        writer.WritePropertyName(name);
        if (value is JsonElement element)
            element.WriteTo(writer);
        else
            JsonSerializer.Serialize(writer, value, SerializerOptions.GetTypeInfo(value.GetType()));
    }

    private async Task WriteAsync(Action<Utf8JsonWriter> writeBody)
    {
        var body = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(body))
        {
            writer.WriteStartObject();
            writeBody(writer);
            writer.WriteEndObject();
        }

        await _writer.WriteAsync(body.WrittenMemory, _lifetime.Token).ConfigureAwait(false);
        Trace?.Invoke(new DapTrace(DapDirection.Sent, Encoding.UTF8.GetString(body.WrittenSpan)));
    }

    private async Task ReadLoopAsync()
    {
        var reader = new MessageReader(_input);
        Exception? failure = null;
        try
        {
            while (await reader.ReadAsync(_lifetime.Token).ConfigureAwait(false) is { } message)
                Receive(message);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or JsonException or FormatException or InvalidOperationException)
        {
            failure = exception;
        }

        Close(failure);
    }

    private void Receive(byte[] message)
    {
        Trace?.Invoke(new DapTrace(DapDirection.Received, Encoding.UTF8.GetString(message)));
        using var document = JsonDocument.Parse(message);
        var root = document.RootElement;
        var type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
        JsonElement? Body(string name) => root.TryGetProperty(name, out var element) ? element.Clone() : null;

        switch (type)
        {
            case "response":
                if (!root.TryGetProperty("request_seq", out var requestSeq) || !requestSeq.TryGetInt32(out var seq) || !_pending.TryRemove(seq, out var pending))
                    return;

                if (root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True)
                {
                    pending.Completion.TrySetResult(Body("body"));
                    return;
                }

                var text = root.TryGetProperty("message", out var messageElement) ? messageElement.GetString() : null;
                var error = Read<ErrorResponse>(Body("body"))?.Error;
                pending.Completion.TrySetException(new DebugAdapterException(pending.Command, ErrorText(pending.Command, text, error), error?.Id));
                return;

            case "event":
                var name = root.TryGetProperty("event", out var eventElement) ? eventElement.GetString() : null;
                if (name is not null)
                {
                    var body = Body("body");
                    _incoming.Writer.TryWrite(() => EventHandler?.Invoke(name, body));
                }

                return;

            case "request":
                var command = root.TryGetProperty("command", out var commandElement) ? commandElement.GetString() ?? "" : "";
                var requestNumber = root.TryGetProperty("seq", out var seqElement) && seqElement.TryGetInt32(out var number) ? number : 0;
                var arguments = Body("arguments");
                _incoming.Writer.TryWrite(() => _ = AnswerAsync(command, requestNumber, arguments));
                return;
        }
    }

    // Prefers the error's format, with its variables filled in, over the short message, which is often only an error code.
    private static string ErrorText(string command, string? message, Message? error)
    {
        if (error is { Format.Length: > 0 } detail)
        {
            var text = detail.Format;
            foreach (var (key, value) in detail.Variables ?? new Dictionary<string, string>())
                text = text.Replace("{" + key + "}", value, StringComparison.Ordinal);
            return text;
        }

        return string.IsNullOrEmpty(message) ? $"The debug adapter could not do {command}." : message;
    }

    private async Task AnswerAsync(string command, int requestSeq, JsonElement? arguments)
    {
        object? result = null;
        string? failure = null;
        try
        {
            if (RequestHandler is { } handler)
                result = await handler(command, arguments, _lifetime.Token).ConfigureAwait(false);
            else
                failure = $"{command} is not supported.";
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failure = exception.Message;
        }

        if (IsClosed)
            return;
        try
        {
            await WriteAsync(writer =>
            {
                writer.WriteNumber("seq", Interlocked.Increment(ref _nextSeq));
                writer.WriteString("type", "response");
                writer.WriteNumber("request_seq", requestSeq);
                writer.WriteBoolean("success", failure is null);
                writer.WriteString("command", command);
                if (failure is not null)
                    writer.WriteString("message", failure);
                WriteValue(writer, "body", result);
            }).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }

    private async Task DispatchAsync()
    {
        await foreach (var action in _incoming.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                action();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Trace?.Invoke(new DapTrace(DapDirection.Received, string.Create(CultureInfo.InvariantCulture, $"An event handler failed: {exception.Message}")));
            }
        }
    }

    private void Close(Exception? failure)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
            return;
        foreach (var seq in _pending.Keys)
        {
            if (_pending.TryRemove(seq, out var pending))
                pending.Completion.TrySetException(new IOException($"The debug adapter stopped before it answered {pending.Command}.", failure));
        }

        _incoming.Writer.TryWrite(() => Closed?.Invoke(this, failure));
        _incoming.Writer.TryComplete();
    }

    private sealed class Pending(string command)
    {
        public string Command { get; } = command;

        public TaskCompletionSource<JsonElement?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
