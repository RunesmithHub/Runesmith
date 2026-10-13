using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Runesmith.Hub.Links;
using Runesmith.Sdk;

namespace Runesmith.App;

/// <summary>What became of handing a start over to the running Runesmith.</summary>
internal enum HandOverResult
{
    /// <summary>No Runesmith is running; this start continues.</summary>
    NoneRunning,

    /// <summary>The running Runesmith took the paths or link.</summary>
    HandedOver,

    /// <summary>A Runesmith is running but did not take them in time, such as one whose window is frozen.</summary>
    NotResponding,
}

/// <summary>Keeps one Runesmith per user: a second start hands its paths or link to the running one over a pipe only the same user can open,
/// and exits.</summary>
/// <remarks>A handover is one line of JSON. The running Runesmith answers with one byte once its window has taken the handover, so a start
/// can tell a running Runesmith from a frozen one. A start that sends the line and closes the pipe without waiting is still understood.</remarks>
internal sealed class SingleInstance : IDisposable
{
    /// <summary>The largest handover the running instance reads; anything longer is dropped.</summary>
    public const int MaxMessageBytes = 64 * 1024;

    /// <summary>How long a start waits for the running Runesmith to take its handover.</summary>
    public static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(2);

    private const byte Taken = 1;

    private readonly CancellationTokenSource stop = new();
    private readonly string pipeName;

    public SingleInstance()
        : this(PipeName())
    {
    }

    internal SingleInstance(string pipeName) => this.pipeName = pipeName;

    /// <summary>Hands paths and a link to the running Runesmith.</summary>
    public static HandOverResult TryHandOver(IReadOnlyList<string> paths, string workingDirectory, string? link = null) =>
        TryHandOver(PipeName(), new Message([.. paths], workingDirectory) { Link = link }, AnswerTimeout);

    internal static HandOverResult TryHandOver(string pipeName, Message message, TimeSpan answerTimeout)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            client.Connect(250);
            var line = JsonSerializer.SerializeToUtf8Bytes(message, MessageJson.Default.Message);
            client.Write(line);
            client.WriteByte((byte)'\n');
            client.Flush();

            using var timeout = new CancellationTokenSource(answerTimeout);
            var answer = new byte[1];
            return client.ReadAsync(answer, timeout.Token).AsTask().GetAwaiter().GetResult() == 1 && answer[0] == Taken
                ? HandOverResult.HandedOver
                : HandOverResult.NotResponding;
        }
        catch (TimeoutException)
        {
            return HandOverResult.NoneRunning;
        }
        catch (OperationCanceledException)
        {
            return HandOverResult.NotResponding;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return HandOverResult.NoneRunning;
        }
    }

    /// <summary>Listens for later starts until disposed.</summary>
    /// <param name="take">Takes a handover; the start that sent it learns it was taken when the returned task completes.</param>
    public void Listen(Func<Message, Task> take) => _ = Task.Run(() => ListenAsync(take));

    public void Dispose()
    {
        stop.Cancel();
        stop.Dispose();
    }

    /// <summary>Reads a handover, or null when it is too long or not a handover.</summary>
    internal static Message? Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaxMessageBytes)
            return null;

        try
        {
            if (JsonSerializer.Deserialize(bytes, MessageJson.Default.Message) is not { Paths: not null, WorkingDirectory: not null } message)
                return null;
            return message.Link is { Length: > HubLink.MaxLength } ? message with { Link = null } : message;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task ListenAsync(Func<Message, Task> take)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(stop.Token);
                if (Read(await ReadLineAsync(server, stop.Token)) is not { } message)
                    continue;

                await take(message);
                server.WriteByte(Taken);
                await server.FlushAsync(stop.Token);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception) when (!stop.IsCancellationRequested)
            {
                // A broken handover, or one the window failed to take, is dropped; the next start connects again.
            }
        }
    }

    // Reads up to the end of the line, or of the stream for a start that closes the pipe after writing.
    private static async Task<byte[]> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            var end = Array.IndexOf(chunk, (byte)'\n', 0, read);
            buffer.Write(chunk, 0, end < 0 ? read : end);
            if (end >= 0 || buffer.Length > MaxMessageBytes)
                break;
        }

        return buffer.ToArray();
    }

    // One pipe per user and settings folder, so a portable copy with its own RUNESMITH_HOME runs on its own.
    private static string PipeName()
    {
        var key = $"{Environment.UserName}|{RunesmithPaths.Config}";
        return "runesmith-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
    }

    internal sealed record Message(string[] Paths, string WorkingDirectory)
    {
        public string? Link { get; init; }
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(SingleInstance.Message))]
internal sealed partial class MessageJson : System.Text.Json.Serialization.JsonSerializerContext;
