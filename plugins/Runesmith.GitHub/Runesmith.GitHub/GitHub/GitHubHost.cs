using System.Composition;
using Runesmith.GitHub.Views.Account;
using Runesmith.Sdk.VersionControl;

namespace Runesmith.GitHub.GitHub;

/// <summary>The github.com account as a repository host: its repositories, links to github.com, and pull requests with their reviews and
/// checks.</summary>
[Export]
[Shared]
[method: ImportingConstructor]
internal sealed class GitHubHost(GitHubAccount account, GitHubRepositories repositories, GitHubInstallations installations, AccountActions actions) : IRepositoryHost
{
    public string Id => "github";

    public string Name => "GitHub";

    public string Icon
    {
        get
        {
            GitHubIcons.EnsureRegistered();
            return GitHubIcons.GitHub;
        }
    }

    public string Server => "github.com";

    public string? Account => account.User?.Login;

    public Uri? AccountAvatarUrl => Uri.TryCreate(account.User?.AvatarUrl, UriKind.Absolute, out var avatar) ? avatar : null;

    public bool SupportsDraftPullRequests => true;

    public Uri? ManageAccessUrl => account.Kind switch
    {
        null => null,
        GitHubSignInKind.PersonalToken => new Uri("https://github.com/settings/personal-access-tokens"),
        GitHubSignInKind.Cli => null,
        _ => account.ManageAccessUri,
    };

    public event EventHandler? Changed
    {
        add
        {
            account.Changed += value;
            installations.Changed += value;
        }

        remove
        {
            account.Changed -= value;
            installations.Changed -= value;
        }
    }

    public Task<bool> SignInAsync(CancellationToken cancellationToken) => actions.SignInAsync();

    public Task SignOutAsync() => account.SignOutAsync();

    public async Task<IReadOnlyList<HostedRepository>> GetRepositoriesAsync(CancellationToken cancellationToken) =>
        [.. (await repositories.LoadAsync(cancellationToken).ConfigureAwait(false)).Select(ToHosted)];

    public bool Owns(string remoteUrl) => GitHubRemote.Parse(remoteUrl) is not null;

    public Uri? GetWebUrl(string remoteUrl, WebTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (GitHubRemote.Parse(remoteUrl) is not { } remote)
            return null;

        return target switch
        {
            { Kind: WebTargetKind.File, Revision: { } revision, Path: { } path } => remote.FileUri(revision, path, target.FirstLine, target.LastLine),
            { Kind: WebTargetKind.Folder, Revision: { } revision, Path: { } path } => remote.FolderUri(revision, path),
            { Kind: WebTargetKind.Commit, Revision: { } revision } => remote.CommitUri(revision),
            { Kind: WebTargetKind.Branch, Revision: { } revision } => remote.BranchUri(revision),
            _ => remote.WebUri,
        };
    }

    public async Task<string?> GetDefaultBranchAsync(string remoteUrl, CancellationToken cancellationToken)
    {
        var remote = Parse(remoteUrl);
        return (await account.Client.GetRepositoryAsync(remote.Owner, remote.Name, cancellationToken).ConfigureAwait(false)).DefaultBranch;
    }

    public Task<IReadOnlyList<PullRequest>> GetPullRequestsAsync(string remoteUrl, CancellationToken cancellationToken)
    {
        var remote = Parse(remoteUrl);
        return account.Client.GetOpenPullRequestsAsync(remote.Owner, remote.Name, cancellationToken);
    }

    public async Task<PullRequest> CreatePullRequestAsync(string remoteUrl, PullRequestDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var remote = Parse(remoteUrl);
        var created = await account.Client.CreatePullRequestAsync(remote.Owner, remote.Name,
            new GitHubNewPullRequest(draft.Title, draft.HeadBranch, draft.BaseBranch, string.IsNullOrWhiteSpace(draft.Body) ? null : draft.Body, draft.IsDraft),
            cancellationToken).ConfigureAwait(false);
        return new PullRequest(created.Number, draft.Title, Account ?? "", draft.HeadBranch, draft.BaseBranch, new Uri(created.HtmlUrl))
        {
            AuthorAvatarUrl = AccountAvatarUrl,
            IsDraft = draft.IsDraft,
            UpdatedAt = DateTimeOffset.UtcNow,
            HeadRef = FormattableString.Invariant($"refs/pull/{created.Number}/head"),
        };
    }

    /// <summary>Gets a repository as the clone dialog lists it.</summary>
    internal static HostedRepository ToHosted(GitHubRepository repository) =>
        new(repository.Owner.Login, repository.Name, repository.CloneUrl, new Uri(repository.HtmlUrl))
        {
            Description = repository.Description,
            IsPrivate = repository.Private,
            IsFork = repository.Fork,
            IsArchived = repository.Archived,
            OwnerIsOrganization = repository.Owner.IsOrganization,
            OwnerAvatarUrl = Uri.TryCreate(repository.Owner.AvatarUrl, UriKind.Absolute, out var avatar) ? avatar : null,
            Language = repository.Language,
            Stars = repository.StargazersCount,
            UpdatedAt = repository.LastChanged,
        };

    private static GitHubRemote Parse(string remoteUrl) =>
        GitHubRemote.Parse(remoteUrl) ?? throw new ArgumentException($"{remoteUrl} is not a repository on github.com.", nameof(remoteUrl));
}

/// <summary>Gives Runesmith the one GitHub host, the github.com account; adding an account signs in to it, or shows it when signed in.</summary>
[Export(typeof(IRepositoryHostProvider))]
[Shared]
[method: ImportingConstructor]
internal sealed class GitHubHostProvider(GitHubHost host) : IRepositoryHostProvider
{
    public string Name => host.Name;

    public string Icon => host.Icon;

    public IReadOnlyList<IRepositoryHost> Hosts { get; } = [host];

    // The provider always has its one host, so the list never changes.
    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public async Task<IRepositoryHost?> AddAccountAsync(CancellationToken cancellationToken) =>
        host.Account is not null || await host.SignInAsync(cancellationToken).ConfigureAwait(true) ? host : null;
}
