namespace Runesmith.Sdk.VersionControl;

/// <summary>Credentials for one Git command: a user name and a token for one host.</summary>
/// <param name="Host">The host they are for, such as <c>github.com</c> or <c>codeberg.org</c>.</param>
/// <param name="UserName">The user name Git sends.</param>
/// <param name="Token">The token Git sends as the password.</param>
public sealed record GitCredentials(string Host, string UserName, string Token);

/// <summary>Supplies credentials for a remote, such as a signed-in account's token for its server. Export it with
/// <c>[Export(typeof(IGitCredentialSource))]</c>; the first source that answers for a remote wins.</summary>
public interface IGitCredentialSource
{
    /// <summary>Gets credentials for a remote's URL, or null to let other sources or the user's own Git setup answer.</summary>
    Task<GitCredentials?> GetAsync(string remoteUrl, CancellationToken cancellationToken);
}

/// <summary>What a Git command printed and how it ended.</summary>
/// <param name="ExitCode">Git's exit code; zero means success.</param>
/// <param name="Output">Standard output, as Git wrote it.</param>
/// <param name="Error">Standard error, as Git wrote it.</param>
public sealed record GitResult(int ExitCode, string Output, string Error)
{
    /// <summary>Gets whether the command succeeded.</summary>
    public bool Succeeded => ExitCode == 0;
}

/// <summary>How a Git command runs.</summary>
public sealed record GitRunOptions
{
    /// <summary>Gets the remote URL whose credentials the command gets from the credential sources, such as the URL being cloned.</summary>
    public string? CredentialsFor { get; init; }

    /// <summary>Gets what receives each line of Git's progress output, such as a clone's percentages.</summary>
    public IProgress<string>? Progress { get; init; }

    /// <summary>Gets text written to Git's standard input.</summary>
    public string? Input { get; init; }
}

/// <summary>Runs Git the way the Git plugin does: the user's own <c>git</c>, never waiting for a terminal prompt, with credentials from the
/// credential sources passed for one command only. The Git plugin provides it.</summary>
public interface IGitService
{
    /// <summary>Runs Git in a folder and returns what it printed, whether it succeeded or not.</summary>
    Task<GitResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, GitRunOptions? options = null,
        CancellationToken cancellationToken = default);
}
