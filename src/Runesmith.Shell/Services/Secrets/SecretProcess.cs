using System.ComponentModel;
using System.Diagnostics;

namespace Runesmith.Shell.Services.Secrets;

/// <summary>Runs a secret store's command line tool, with what it reads given on its standard input.</summary>
internal static class SecretProcess
{
    public sealed record Result(int ExitCode, string Output, string Error);

    /// <exception cref="SecretStoreException">The program could not start.</exception>
    public static async Task<Result> RunAsync(string program, IEnumerable<string> arguments, string? input, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(program)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            throw new SecretStoreException($"{program} could not start: {exception.Message}", exception);
        }

        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            if (input is not null)
                await process.StandardInput.WriteAsync(input.AsMemory(), cancellationToken).ConfigureAwait(false);
            process.StandardInput.Close();
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        catch (IOException exception)
        {
            throw new SecretStoreException($"{program} stopped early: {exception.Message}", exception);
        }

        return new Result(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
    }
}
