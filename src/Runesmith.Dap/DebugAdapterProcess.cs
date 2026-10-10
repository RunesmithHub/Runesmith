using System.ComponentModel;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace Runesmith.Dap;

/// <summary>A running debug adapter and the client that talks to it: a child process over its standard streams, or a server over a socket.
/// Disposing it ends the process and every process it started.</summary>
public sealed class DebugAdapterProcess : IAsyncDisposable
{
    private readonly Process? _process;
    private readonly TcpClient? _socket;
    private int _disposed;

    private DebugAdapterProcess(DebugAdapterClient client, Process? process, TcpClient? socket)
    {
        Client = client;
        _process = process;
        _socket = socket;
        Exited = process is null ? Task.FromResult<int?>(null) : WaitAsync(process);
    }

    public DebugAdapterClient Client { get; }

    /// <summary>Gets a task that ends with the adapter process's exit code, or with null at once for an adapter reached over a socket.</summary>
    public Task<int?> Exited { get; }

    /// <summary>Starts an adapter process and a client over its standard streams; the client has not started reading yet.</summary>
    /// <param name="start">The program, its arguments, working folder and environment; the streams are redirected here.</param>
    /// <param name="log">Receives the adapter's standard error, line by line, on a background thread.</param>
    /// <exception cref="DebugAdapterException">The program could not be started; the message says why.</exception>
    public static DebugAdapterProcess Start(ProcessStartInfo start, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(start);
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.StandardErrorEncoding = Encoding.UTF8;
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            process.Dispose();
            throw new DebugAdapterException($"{start.FileName} could not be started: {exception.Message}", exception);
        }

        _ = Task.Run(() => ReadErrorsAsync(process.StandardError, log));
        var client = new DebugAdapterClient(process.StandardOutput.BaseStream, process.StandardInput.BaseStream);
        return new DebugAdapterProcess(client, process, null);
    }

    /// <summary>Connects to an adapter that listens on a socket and creates a client over it; the client has not started reading yet.</summary>
    /// <exception cref="DebugAdapterException">The adapter could not be reached.</exception>
    public static async Task<DebugAdapterProcess> ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);
        var socket = new TcpClient { NoDelay = true };
        try
        {
            await socket.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException exception)
        {
            socket.Dispose();
            throw new DebugAdapterException($"The debug adapter at {host}:{port} could not be reached: {exception.Message}", exception);
        }

        var stream = socket.GetStream();
        return new DebugAdapterProcess(new DebugAdapterClient(stream, stream), null, socket);
    }

    /// <summary>Closes the client and ends the adapter process and its children.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        Kill();
        _socket?.Dispose();
        await Client.DisposeAsync().ConfigureAwait(false);
        _process?.Dispose();
    }

    /// <summary>Ends the adapter process and every process it started, such as the program being debugged.</summary>
    public void Kill()
    {
        try
        {
            if (_process is { HasExited: false })
                _process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
        }
    }

    private static async Task<int?> WaitAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static async Task ReadErrorsAsync(StreamReader reader, Action<string>? log)
    {
        try
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
                log?.Invoke(line);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }
    }
}
