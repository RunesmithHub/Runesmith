using Runesmith.Sdk.VersionControl;

namespace Runesmith.Plugins.Gitea;

/// <summary>Gives Git a signed-in account's login and token for the HTTPS remotes of its server. When several accounts are signed in to the
/// server, the one named in the remote's URL, such as <c>https://alice@codeberg.org/...</c>, wins, and otherwise the first.</summary>
internal class GiteaCredentialSource(GiteaHostProvider provider) : IGitCredentialSource
{
    public async Task<GitCredentials?> GetAsync(string remoteUrl, CancellationToken cancellationToken)
    {
        var user = Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri) && uri.UserInfo.Length > 0
            ? Uri.UnescapeDataString(uri.UserInfo.Split(':')[0])
            : null;
        var candidates = provider.Accounts.Where(host => GiteaRemote.IsWebRemote(host.Server, remoteUrl)).ToList();
        foreach (var host in candidates.OrderBy(host => string.Equals(host.Login, user, StringComparison.OrdinalIgnoreCase) ? 0 : 1))
        {
            if (await host.GetTokenAsync(cancellationToken).ConfigureAwait(false) is { } token && host.Login is { } login)
                return new GitCredentials(host.Server.BaseUri.Authority, login, token.Value);
        }

        return null;
    }
}
