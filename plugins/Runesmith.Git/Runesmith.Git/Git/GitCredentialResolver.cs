using System.Composition;
using Runesmith.Sdk.VersionControl;

namespace Runesmith.Git.Git;

/// <summary>Finds credentials for a remote among the hosting plugins' credential sources: the first source that answers wins, and remotes
/// that are not HTTPS keep the user's own setup, such as SSH keys.</summary>
[Export]
[Shared]
[method: ImportingConstructor]
internal sealed class GitCredentialResolver([ImportMany] IEnumerable<IGitCredentialSource> sources)
{
    private readonly IReadOnlyList<IGitCredentialSource> sources = [.. sources];

    /// <summary>Gets credentials for a remote URL, or null to leave Git to the user's setup.</summary>
    public async Task<GitCredentials?> GetAsync(string remoteUrl, CancellationToken cancellationToken)
    {
        if (!IsHttps(remoteUrl))
            return null;

        foreach (var source in sources)
        {
            if (await source.GetAsync(remoteUrl, cancellationToken).ConfigureAwait(false) is { } credentials)
                return credentials;
        }

        return null;
    }

    /// <summary>Whether a remote URL is HTTPS, the only kind Runesmith gives credentials to; Git asks a credential helper for no other.</summary>
    public static bool IsHttps(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
