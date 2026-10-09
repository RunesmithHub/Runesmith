using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Runesmith.GitHub.GitHub;

/// <summary>Reads the token of the GitHub CLI's own sign-in, for people who signed in with <c>gh auth login</c> already.</summary>
internal static class GitHubCli
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>Gets the name of the executable; <c>gh</c> on the <c>PATH</c> unless set, such as by tests.</summary>
    public static string Executable { get; set; } = "gh";

    /// <summary>Gets whether the GitHub CLI is on the <c>PATH</c>.</summary>
    public static bool IsInstalled()
    {
        if (Path.IsPathRooted(Executable))
            return File.Exists(Executable);

        var names = OperatingSystem.IsWindows() ? new[] { Executable + ".exe", Executable } : [Executable];
        return (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(folder => names.Any(name => File.Exists(Path.Combine(folder, name))));
    }

    /// <summary>Gets the CLI's token for github.com, or null when it is not installed or not signed in.</summary>
    public static async Task<string?> GetTokenAsync(CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(Executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            ArgumentList = { "auth", "token", "--hostname", "github.com" },
        };
        start.Environment["GH_PROMPT_DISABLED"] = "1";

        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (Win32Exception)
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            _ = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var token = (await output.ConfigureAwait(false)).Trim();
            return process.ExitCode == 0 && token.Length > 0 ? token : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            return null;
        }
    }
}
