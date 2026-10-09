using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Runesmith.Hub.Links;
using Runesmith.Sdk;

namespace Runesmith.App;

/// <summary>Keeps one Runesmith per user: a second start hands its paths or link to the running one over a pipe only the same user can open,
/// and exits.</summary>
internal sealed class SingleInstance : IDisposable
{
    /// <summary>The largest handover the running instance reads; anything longer is dropped.</summary>
    public const int MaxMessageBytes = 64 * 1024;

    private readonly CancellationTokenSource stop = new();
    private readonly string pipeName = PipeName();

    /// <summary>Raised, on a background thread, when another start hands over paths and its working folder.</summary>
    public event Action<IReadOnlyList<string>, string>? Received;

    /// <summary>Raised, on a background thread, when another start hands over a link; the link is not checked yet beyond its length.</summary>
    public event Action<string>? LinkReceived;

    /// <summary>Hands paths and a link to the running Runesmith; returns false when none is running.</summary>
    public static bool TryHandOver(IReadOnlyList<string> paths, string workingDirectory, string? link = null)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName(), PipeDirection.Out);
            client.Connect(250);
            var message = JsonSerializer.Serialize(new Message([.. paths], workingDirectory) { Link = link }, MessageJson.Default.Message);
            using var writer = new StreamWriter(client, new UTF8Encoding(false));
            writer.Write(message);
            return true;
        }
        catch (Exception exception) when (exception is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Listens for later starts until disposed.</summary>
    public void Listen() => _ = Task.Run(ListenAsync);

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

    private async Task ListenAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(stop.Token);
                if (Read(await ReadLimitedAsync(server, stop.Token)) is not { } message)
                    continue;

                if (message.Paths.Length > 0 || message.Link is null)
                    Received?.Invoke(message.Paths, message.WorkingDirectory);
                if (message.Link is { } link)
                    LinkReceived?.Invoke(link);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A broken handover is dropped; the next start connects again.
            }
        }
    }

    private static async Task<byte[]> ReadLimitedAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxMessageBytes)
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
