using System.Diagnostics;
using System.Text;
using Runesmith.Sdk.VersionControl;

namespace Runesmith.Git.Git;

/// <summary>Thrown when a Git command fails; its message is Git's own, without its <c>fatal:</c> and <c>error:</c> prefixes.</summary>
internal sealed class GitException(GitResult result, IReadOnlyList<string> arguments)
    : Exception(MessageOf(result) is { Length: > 0 } message ? message : $"git {(arguments.Count > 0 ? arguments[0] : "")} failed with exit code {result.ExitCode}.")
{
    public GitResult Result { get; } = result;

    public IReadOnlyList<string> Arguments { get; } = arguments;

    /// <summary>Gets what went wrong, for the failures Runesmith offers a way out of.</summary>
    public GitErrorKind Kind { get; } = GitErrors.Classify(result.Error + "\n" + result.Output);

    private static string MessageOf(GitResult result) =>
        string.Join('\n', result.Error.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.StartsWith("fatal: ", StringComparison.Ordinal) ? line[7..] : line.StartsWith("error: ", StringComparison.Ordinal) ? line[7..] : line));
}

/// <summary>Runs the <c>git</c> command line: the one place Runesmith starts Git, so every command gets the same environment.</summary>
internal static class GitProcess
{
    private const string TokenVariable = "RUNESMITH_GIT_TOKEN";
    private const string UserVariable = "RUNESMITH_GIT_USER";

    /// <summary>Gets the name of the Git executable; <c>git</c> on the <c>PATH</c> unless set, such as by tests.</summary>
    public static string Executable { get; set; } = "git";

    /// <summary>Runs Git and returns what it printed, whether it succeeded or not.</summary>
    /// <param name="workingDirectory">The folder Git runs in.</param>
    /// <param name="arguments">Git's arguments, unquoted.</param>
    /// <param name="credentials">Credentials for one host, given to Git through a credential helper for this command only.</param>
    /// <param name="progress">Receives each line of Git's progress output, such as the percentages of a clone, when given.</param>
    /// <param name="input">Text written to Git's standard input, such as a commit message.</param>
    public static async Task<GitResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken,
        GitCredentials? credentials = null, IProgress<string>? progress = null, string? input = null)
    {
        var start = new ProcessStartInfo(Executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        // Git must never wait for a password or an editor Runesmith does not have, and its messages are parsed in English.
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GIT_EDITOR"] = "true";
        start.Environment["GCM_INTERACTIVE"] = "never";
        start.Environment["LC_ALL"] = "C";
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";

        if (credentials is not null)
        {
            // The token reaches Git through the environment, never the command line; the empty helper first drops the user's own helpers
            // for this host, for this command only.
            var scope = $"credential.https://{credentials.Host}.helper";
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add(scope + "=");
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add($"{scope}=!f() {{ echo \"username=${UserVariable}\"; echo \"password=${TokenVariable}\"; }}; f");
            start.Environment[UserVariable] = credentials.UserName;
            start.Environment[TokenVariable] = credentials.Token;
        }

        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new GitException(new GitResult(-1, "", $"Git could not be started: {exception.Message}. Install Git and make sure it is on the PATH."), arguments);
        }

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
        });

        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = ReadErrorAsync(process.StandardError, progress);
        if (input is not null)
            await process.StandardInput.WriteAsync(input).ConfigureAwait(false);
        process.StandardInput.Close();

        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        var result = new GitResult(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    /// <summary>Runs Git and returns its output, or throws a <see cref="GitException"/> with Git's message when it fails.</summary>
    public static async Task<string> RunCheckedAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken,
        GitCredentials? credentials = null, IProgress<string>? progress = null, string? input = null)
    {
        var result = await RunAsync(workingDirectory, arguments, cancellationToken, credentials, progress, input).ConfigureAwait(false);
        return result.Succeeded ? result.Output : throw new GitException(result, arguments);
    }

    // Progress lines end in a carriage return as Git redraws them, so both line endings end a line.
    private static async Task<string> ReadErrorAsync(StreamReader reader, IProgress<string>? progress)
    {
        var all = new StringBuilder();
        var line = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                var character = buffer[i];
                if (character is '\r' or '\n')
                {
                    if (line.Length > 0)
                        progress?.Report(line.ToString());
                    if (character == '\n')
                        all.Append(line).Append('\n');
                    line.Clear();
                }
                else
                {
                    line.Append(character);
                }
            }
        }

        if (line.Length > 0)
        {
            progress?.Report(line.ToString());
            all.Append(line);
        }

        return all.ToString();
    }
}
