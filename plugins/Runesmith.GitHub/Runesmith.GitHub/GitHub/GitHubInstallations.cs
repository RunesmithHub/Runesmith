using System.Composition;
using System.Globalization;

namespace Runesmith.GitHub.GitHub;

/// <summary>An account or organization the GitHub App is installed on, as the Repository access card shows it.</summary>
/// <param name="Login">The account's login.</param>
/// <param name="AvatarUrl">The account's avatar.</param>
/// <param name="IsOrganization">Whether the account is an organization.</param>
/// <param name="AllRepositories">Whether the installation covers every repository, including future ones.</param>
/// <param name="RepositoryCount">How many repositories the installation gives the signed-in user.</param>
/// <param name="ConfigureUri">The installation's settings page.</param>
internal sealed record InstallationRow(string Login, string AvatarUrl, bool IsOrganization, bool AllRepositories, int RepositoryCount, Uri ConfigureUri)
{
    /// <summary>Gets what the installation covers: "All repositories" or "3 selected".</summary>
    public string Scope => AllRepositories ? "All repositories" : string.Create(CultureInfo.InvariantCulture, $"{RepositoryCount} selected");
}

/// <summary>The installations of the GitHub App the signed-in user can reach, with the repositories each one gives.</summary>
internal sealed record InstallationsSnapshot(IReadOnlyList<GitHubInstallation> Installations, IReadOnlyDictionary<long, IReadOnlyList<GitHubRepository>> Repositories)
{
    public static InstallationsSnapshot Empty { get; } = new([], new Dictionary<long, IReadOnlyList<GitHubRepository>>());

    /// <summary>Gets the rows of the Repository access card: the signed-in user's account first, then organizations and other accounts by name.</summary>
    public IReadOnlyList<InstallationRow> Rows(string? userLogin) =>
    [
        .. Installations
            .OrderBy(i => string.Equals(i.Account.Login, userLogin, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(i => i.Account.Login, StringComparer.OrdinalIgnoreCase)
            .Select(i => new InstallationRow(i.Account.Login, i.Account.AvatarUrl, i.Account.IsOrganization,
                string.Equals(i.RepositorySelection, "all", StringComparison.Ordinal), Repositories.GetValueOrDefault(i.Id)?.Count ?? 0, GitHubAppUrls.Configure(i))),
    ];

    /// <summary>Gets the installations that are in this snapshot but were not among <paramref name="before"/>.</summary>
    public IReadOnlyList<GitHubInstallation> AddedSince(IReadOnlyCollection<long> before) => [.. Installations.Where(i => !before.Contains(i.Id))];

    /// <summary>Gets whether two snapshots give the same accounts with the same repositories.</summary>
    public bool SameAccessAs(InstallationsSnapshot? other) =>
        other is not null
        && Installations.Select(Key).Order(StringComparer.Ordinal).SequenceEqual(other.Installations.Select(other.Key).Order(StringComparer.Ordinal));

    private string Key(GitHubInstallation installation) =>
        string.Create(CultureInfo.InvariantCulture, $"{installation.Id}:{installation.RepositorySelection}:{Repositories.GetValueOrDefault(installation.Id)?.Count ?? 0}");
}

/// <summary>Loads the GitHub App's installations and their repositories for an app sign-in, keeps the last result, and tells when access
/// changed.</summary>
[Export]
[Shared]
internal sealed class GitHubInstallations
{
    private const int Parallel = 4;

    private readonly GitHubAccount account;
    private readonly Lock gate = new();
    private Task<InstallationsSnapshot>? loading;

    [ImportingConstructor]
    public GitHubInstallations(GitHubAccount account)
    {
        this.account = account;
        account.Changed += (_, _) =>
        {
            if (Current is null || account.IsSignedIn)
                return;

            Current = null;
            Changed?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>Raised, on any thread, when a load finds other installations or repositories than the load before, or the account signs out.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the last loaded installations, or null before the first load.</summary>
    public InstallationsSnapshot? Current { get; private set; }

    /// <summary>Loads the installations and their repositories; loads asked for while one runs share it.</summary>
    public Task<InstallationsSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        Task<InstallationsSnapshot> task;
        lock (gate)
        {
            task = loading ??= Task.Run(LoadAndKeepAsync);
        }

        return task.WaitAsync(cancellationToken);
    }

    private async Task<InstallationsSnapshot> LoadAndKeepAsync()
    {
        try
        {
            var snapshot = account.Kind == GitHubSignInKind.App ? await FetchAsync(CancellationToken.None).ConfigureAwait(false) : InstallationsSnapshot.Empty;
            var previous = Current;
            Current = snapshot;
            if (previous is not null && !snapshot.SameAccessAs(previous))
                Changed?.Invoke(this, EventArgs.Empty);
            return snapshot;
        }
        finally
        {
            lock (gate)
                loading = null;
        }
    }

    private async Task<InstallationsSnapshot> FetchAsync(CancellationToken cancellationToken)
    {
        var installations = await account.Client.GetInstallationsAsync(cancellationToken).ConfigureAwait(false);
        account.RememberAppSlug(installations.Select(i => i.AppSlug).FirstOrDefault(slug => !string.IsNullOrEmpty(slug)));
        var lists = new IReadOnlyList<GitHubRepository>[installations.Count];
        await System.Threading.Tasks.Parallel.ForEachAsync(Enumerable.Range(0, installations.Count),
            new ParallelOptions { MaxDegreeOfParallelism = Parallel, CancellationToken = cancellationToken },
            async (index, token) => lists[index] = await account.Client.GetInstallationRepositoriesAsync(installations[index].Id, token).ConfigureAwait(false))
            .ConfigureAwait(false);
        return new InstallationsSnapshot(installations, Enumerable.Range(0, installations.Count).ToDictionary(i => installations[i].Id, i => lists[i]));
    }
}
