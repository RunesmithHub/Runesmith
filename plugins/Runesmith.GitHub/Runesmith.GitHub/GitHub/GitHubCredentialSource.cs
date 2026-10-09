using System.Composition;
using Runesmith.Sdk.VersionControl;

namespace Runesmith.GitHub.GitHub;

/// <summary>Gives Git the signed-in account's token for <c>https://github.com</c> remotes; other remotes keep the user's own setup.</summary>
[Export(typeof(IGitCredentialSource))]
[Export]
[Shared]
[method: ImportingConstructor]
internal sealed class GitHubCredentialSource(GitHubAccount account) : IGitCredentialSource
{
    /// <summary>The user name GitHub expects next to an access token.</summary>
    public const string UserName = "x-access-token";

    public async Task<GitCredentials?> GetAsync(string remoteUrl, CancellationToken cancellationToken)
    {
        if (!GitHubRemote.IsHttps(remoteUrl))
            return null;

        try
        {
            return await account.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false) is { } token
                ? new GitCredentials("github.com", UserName, token)
                : null;
        }
        catch (GitHubException)
        {
            return null;
        }
    }
}
