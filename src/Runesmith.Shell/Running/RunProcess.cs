using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Runesmith.Sdk.Running;

namespace Runesmith.Shell.Running;

/// <summary>A program started from a launch plan, with its output, error and input redirected; stopping it ends its whole process tree.</summary>
public sealed class RunProcess : IDisposable
{
    private readonly Process process;
    private readonly Task<int> exited;
    private int stopped;

    private RunProcess(Process process, Action<string, ConsoleSource> output)
    {
        this.process = process;
        var reading = Task.WhenAll(
            Task.Run(() => PumpAsync(process.StandardOutput, ConsoleSource.Output, output)),
            Task.Run(() => PumpAsync(process.StandardError, ConsoleSource.Error, output)));
        exited = WaitAsync(process, reading);
    }

    /// <summary>Gets the process id.</summary>
    public int Id => process.Id;

    /// <summary>Gets a task that ends with the exit code once the process has exited and its output has been read.</summary>
    public Task<int> Exited => exited;

    /// <summary>Gets whether <see cref="Stop"/> ended the process.</summary>
    public bool WasStopped => Volatile.Read(ref stopped) != 0;

    /// <summary>Starts a plan's program; its environment is Runesmith's with the plan's variables over it.</summary>
    /// <param name="output">Receives the program's text as it arrives, on a background thread, in pieces that may split lines.</param>
    /// <exception cref="InvalidOperationException">The program could not be started; the message says why.</exception>
    public static RunProcess Start(LaunchPlan plan, Action<string, ConsoleSource> output)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(output);
        var start = CreateStartInfo(plan);
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            process.Dispose();
            throw new InvalidOperationException($"{plan.Program} could not be started: {exception.Message}", exception);
        }

        return new RunProcess(process, output);
    }

    /// <summary>Gets how a plan's program is started.</summary>
    internal static ProcessStartInfo CreateStartInfo(LaunchPlan plan)
    {
        var start = new ProcessStartInfo(plan.Program)
        {
            WorkingDirectory = plan.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        foreach (var argument in plan.Arguments)
            start.ArgumentList.Add(argument);

        // .NET programs write their console colors as escape sequences only when told to and when TERM names a terminal with colors.
        start.Environment["DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION"] = "1";
        if (!start.Environment.TryGetValue("TERM", out var term) || string.IsNullOrEmpty(term) || term == "dumb")
            start.Environment["TERM"] = "xterm-256color";
        // .NET reads TERMINFO but not TERMINFO_DIRS, which systems without /usr/share/terminfo, such as NixOS, set instead.
        if (!start.Environment.ContainsKey("TERMINFO") && start.Environment.TryGetValue("TERMINFO_DIRS", out var folders) && folders is { Length: > 0 }
            && FindTerminfo(folders, start.Environment["TERM"]!) is { } folder)
            start.Environment["TERMINFO"] = folder;
        foreach (var (name, value) in plan.Environment)
            start.Environment[name] = value;
        return start;
    }

    private static string? FindTerminfo(string folders, string term) =>
        folders.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(folder =>
            File.Exists(Path.Combine(folder, term[..1], term)) || File.Exists(Path.Combine(folder, ((int)term[0]).ToString("x2", CultureInfo.InvariantCulture), term)));

    /// <summary>Writes a line to the program's standard input.</summary>
    public async Task WriteLineAsync(string line)
    {
        try
        {
            await process.StandardInput.WriteLineAsync(line).ConfigureAwait(false);
            await process.StandardInput.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }
    }

    /// <summary>Ends the process and every process it started.</summary>
    public void Stop()
    {
        Interlocked.Exchange(ref stopped, 1);
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
        }
    }

    public void Dispose() => process.Dispose();

    private static async Task PumpAsync(StreamReader reader, ConsoleSource stream, Action<string, ConsoleSource> output)
    {
        var buffer = new char[8192];
        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
                output(new string(buffer, 0, read), stream);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
        }
    }

    private static async Task<int> WaitAsync(Process process, Task reading)
    {
        await process.WaitForExitAsync().ConfigureAwait(false);

        // A child that outlives the process can hold its output open, so reading gets a moment to finish rather than forever.
        await Task.WhenAny(reading, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
        return process.ExitCode;
    }
}
