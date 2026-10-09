using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Runesmith.CSharp.Templates;

/// <summary>Runs the <c>dotnet</c> command line.</summary>
internal static class DotnetCli
{
    private const int ReportedLines = 20;

    /// <summary>Runs <c>dotnet</c> and returns its output; throws <see cref="InvalidOperationException"/> with the end of the output when it
    /// fails.</summary>
    /// <param name="root">The .NET root to set as <c>DOTNET_ROOT</c>, or null to keep the environment's.</param>
    public static async Task<string> RunAsync(string dotnet, string? root, string workingDirectory, IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(dotnet)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        if (root is not null)
            start.Environment["DOTNET_ROOT"] = root;
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";

        using var process = new Process { StartInfo = start };
        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => Append(output, e.Data);
        process.ErrorDataReceived += (_, e) => Append(output, e.Data);
        try
        {
            process.Start();
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException($"Could not start {dotnet} ({exception.Message}). Install the .NET SDK from https://dot.net.", exception);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        process.WaitForExit();
        string text;
        lock (output)
            text = output.ToString();
        if (process.ExitCode != 0)
        {
            var tail = string.Join('\n', text.Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(ReportedLines));
            throw new InvalidOperationException($"dotnet {string.Join(' ', arguments.Take(2))} failed with exit code {process.ExitCode}:\n{tail}");
        }

        return text;
    }

    private static void Append(StringBuilder output, string? line)
    {
        if (line is null)
            return;
        lock (output)
            output.AppendLine(line);
    }
}
