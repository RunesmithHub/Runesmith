using System.Composition;

namespace Runesmith.GitHub.GitHub;

/// <summary>Lists the repositories the signed-in account may clone: through the GitHub App's installations when it signed in as the app,
/// and the account's own list for other tokens.</summary>
[Export]
[Shared]
[method: ImportingConstructor]
internal sealed class GitHubRepositories(GitHubAccount account, GitHubInstallations installations)
{
    /// <summary>Gets the repositories, each once.</summary>
    public async Task<IReadOnlyList<GitHubRepository>> LoadAsync(CancellationToken cancellationToken)
    {
        if (account.Kind != GitHubSignInKind.App)
            return await account.Client.GetUserRepositoriesAsync(cancellationToken).ConfigureAwait(false);

        var snapshot = await installations.LoadAsync(cancellationToken).ConfigureAwait(false);
        return [.. snapshot.Installations.SelectMany(i => snapshot.Repositories.GetValueOrDefault(i.Id) ?? []).DistinctBy(r => r.Id)];
    }
}
