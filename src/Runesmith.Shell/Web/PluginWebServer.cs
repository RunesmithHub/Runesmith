using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Runesmith.Shell.Web;

/// <summary>Serves one plugin's web folder on a loopback port of its own, so each plugin's pages have their own origin. It answers GET and
/// HEAD for the folder's files, with the plugin's content security policy, and nothing else.</summary>
internal sealed class PluginWebServer : IDisposable
{
    private const int MaxHeaderLength = 16 * 1024;

    private readonly string root;
    private readonly Func<string> policy;
    private readonly TcpListener listener;
    private readonly CancellationTokenSource stopping = new();

    /// <param name="root">The plugin's web folder.</param>
    /// <param name="policy">Gets the content security policy, once the origin is known.</param>
    public PluginWebServer(string root, Func<string> policy)
    {
        this.root = root;
        this.policy = policy;
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Origin = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/");
        _ = AcceptAsync();
    }

    public Uri Origin { get; }

    public void Dispose()
    {
        stopping.Cancel();
        listener.Stop();
        stopping.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!stopping.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(stopping.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            _ = ServeAsync(client);
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var stream = client.GetStream();
                var request = await ReadHeaderAsync(stream, timeout.Token);
                await RespondAsync(stream, request, timeout.Token);
            }
            catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
            }
        }
    }

    private static async Task<string?> ReadHeaderAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxHeaderLength];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken);
            if (read == 0)
                return null;

            length += read;
            if (buffer.AsSpan(0, length).IndexOf("\r\n\r\n"u8) >= 0)
                return Encoding.Latin1.GetString(buffer, 0, length);
        }

        return null;
    }

    private async Task RespondAsync(NetworkStream stream, string? request, CancellationToken cancellationToken)
    {
        var lines = request?.Split("\r\n") ?? [];
        var parts = lines.Length > 0 ? lines[0].Split(' ') : [];
        if (parts.Length != 3 || !parts[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
        {
            await WriteAsync(stream, 400, "Bad Request", null, null, cancellationToken);
            return;
        }

        var host = lines.Skip(1).Select(l => l.Split(':', 2)).FirstOrDefault(p => p.Length == 2 && p[0].Trim().Equals("Host", StringComparison.OrdinalIgnoreCase))?[1].Trim();
        if (host != Origin.Authority)
        {
            await WriteAsync(stream, 421, "Misdirected Request", null, null, cancellationToken);
            return;
        }

        var head = parts[0] == "HEAD";
        if (parts[0] != "GET" && !head)
        {
            await WriteAsync(stream, 405, "Method Not Allowed", null, null, cancellationToken);
            return;
        }

        var path = parts[1].Split(['?', '#'], 2)[0];
        if (path == BundledFiles.BridgePath)
        {
            await WriteAsync(stream, 200, "OK", "text/javascript; charset=utf-8", Encoding.UTF8.GetBytes(WebMessages.BridgeScript), cancellationToken, head);
            return;
        }

        if (BundledFiles.Resolve(root, parts[1]) is not { } file)
        {
            await WriteAsync(stream, 404, "Not Found", null, null, cancellationToken);
            return;
        }

        var body = await File.ReadAllBytesAsync(file, cancellationToken);
        await WriteAsync(stream, 200, "OK", BundledFiles.ContentTypeOf(file), body, cancellationToken, head);
    }

    private async Task WriteAsync(NetworkStream stream, int status, string reason, string? type, byte[]? body, CancellationToken cancellationToken, bool head = false)
    {
        body ??= status == 200 ? [] : Encoding.UTF8.GetBytes(reason);
        var header = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"HTTP/1.1 {status} {reason}\r\n")
            .Append(CultureInfo.InvariantCulture, $"Content-Type: {type ?? "text/plain; charset=utf-8"}\r\n")
            .Append(CultureInfo.InvariantCulture, $"Content-Length: {body.Length}\r\n")
            .Append(CultureInfo.InvariantCulture, $"Content-Security-Policy: {policy()}\r\n")
            .Append("X-Content-Type-Options: nosniff\r\n")
            .Append("Referrer-Policy: no-referrer\r\n")
            .Append("Cross-Origin-Resource-Policy: same-origin\r\n")
            .Append("Cross-Origin-Opener-Policy: same-origin\r\n")
            .Append("Cache-Control: no-store\r\n")
            .Append("Connection: close\r\n\r\n")
            .ToString();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), cancellationToken);
        if (!head)
            await stream.WriteAsync(body, cancellationToken);
    }
}
