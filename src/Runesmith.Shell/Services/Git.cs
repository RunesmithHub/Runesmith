using System.ComponentModel;
using System.Diagnostics;
using Runesmith.Sdk.Shell;

namespace Runesmith.Shell.Services;

/// <summary>Runs the few Git commands Runesmith needs itself, with the <c>git</c> on the PATH.</summary>
internal static class Git
{
    /// <summary>Gets whether a <c>git</c> program is on the PATH.</summary>
    public static bool IsAvailable() => FindOnPath(OperatingSystem.IsWindows() ? "git.exe" : "git") is not null;

    /// <summary>Makes a folder a Git repository with <c>git init</c>, writing what Git says to a channel; throws when Git fails.</summary>
    public static async Task InitAsync(string folder, IOutputChannel log, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(log);
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("init");
        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException($"Git could not start: {exception.Message}", exception);
        }

        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        log.Append(await output);
        log.Append(await error);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git init failed with exit code {process.ExitCode}: {(await error).Trim()}");
    }

    private static string? FindOnPath(string program) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => Path.Combine(folder, program))
            .FirstOrDefault(File.Exists);
}
