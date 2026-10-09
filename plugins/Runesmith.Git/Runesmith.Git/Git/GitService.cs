using System.Composition;
using Runesmith.Sdk.VersionControl;

namespace Runesmith.Git.Git;

/// <summary>Runs Git for other plugins, and for the Git plugin's own features that work outside the open repository's queue.</summary>
[Export(typeof(IGitService))]
[Shared]
[method: ImportingConstructor]
internal sealed class GitService(GitCredentialResolver credentials) : IGitService
{
    public async Task<GitResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, GitRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var forCommand = options?.CredentialsFor is { } url ? await credentials.GetAsync(url, cancellationToken).ConfigureAwait(false) : null;
        try
        {
            return await GitProcess.RunAsync(workingDirectory, arguments, cancellationToken, forCommand, options?.Progress, options?.Input).ConfigureAwait(false);
        }
        catch (GitException exception)
        {
            return exception.Result;
        }
    }
}
