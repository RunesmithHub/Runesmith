namespace Runesmith.Sdk.VersionControl;

/// <summary>A repository on a hosting service.</summary>
/// <param name="Owner">The account or organization that owns it.</param>
/// <param name="Name">Its name.</param>
/// <param name="CloneUrl">The HTTPS URL to clone it from.</param>
/// <param name="WebUrl">Its page.</param>
public sealed record HostedRepository(string Owner, string Name, string CloneUrl, Uri WebUrl)
{
    public string? Description { get; init; }

    public bool IsPrivate { get; init; }

    public bool IsFork { get; init; }

    public bool IsArchived { get; init; }

    /// <summary>Gets whether the owner is an organization rather than the signed-in account or another user.</summary>
    public bool OwnerIsOrganization { get; init; }

    public Uri? OwnerAvatarUrl { get; init; }

    /// <summary>Gets its main language, such as "C#".</summary>
    public string? Language { get; init; }

    public int Stars { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>What a link on a hosting service shows.</summary>
public enum WebTargetKind
{
    Repository,
    File,
    Folder,
    Commit,
    Branch,
}

/// <summary>What to open on a hosting service's website.</summary>
/// <param name="Kind">What the link shows.</param>
/// <param name="Revision">The branch or commit the link is at, for files, folders, commits and branches.</param>
/// <param name="Path">The path inside the repository, with forward slashes, for files and folders.</param>
public sealed record WebTarget(WebTargetKind Kind, string? Revision = null, string? Path = null)
{
    /// <summary>Gets the first selected line, counted from 1, for files.</summary>
    public int? FirstLine { get; init; }

    /// <summary>Gets the last selected line, counted from 1, for files.</summary>
    public int? LastLine { get; init; }
}

/// <summary>How reviewers judged a pull request.</summary>
public enum ReviewState
{
    None,
    ReviewRequired,
    Approved,
    ChangesRequested,
}

/// <summary>How a pull request's checks are doing.</summary>
public enum ChecksState
{
    None,
    Pending,
    Passing,
    Failing,
}

/// <summary>A pull request.</summary>
/// <param name="Number">Its number in the repository.</param>
/// <param name="Title">Its title.</param>
/// <param name="Author">Its author's login.</param>
/// <param name="HeadBranch">The branch it merges.</param>
/// <param name="BaseBranch">The branch it merges into.</param>
/// <param name="WebUrl">Its page.</param>
public sealed record PullRequest(int Number, string Title, string Author, string HeadBranch, string BaseBranch, Uri WebUrl)
{
    public Uri? AuthorAvatarUrl { get; init; }

    public bool IsDraft { get; init; }

    public ReviewState Review { get; init; }

    public ChecksState Checks { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>Gets the ref that holds its head commits on the base repository, such as <c>refs/pull/12/head</c>.</summary>
    public string? HeadRef { get; init; }
}

/// <summary>What a new pull request should be.</summary>
/// <param name="HeadBranch">The branch it merges, already pushed.</param>
/// <param name="BaseBranch">The branch it merges into.</param>
/// <param name="Title">Its title.</param>
/// <param name="Body">Its description, in Markdown.</param>
/// <param name="IsDraft">Whether it starts as a draft, where the service supports drafts.</param>
public sealed record PullRequestDraft(string HeadBranch, string BaseBranch, string Title, string Body, bool IsDraft);

/// <summary>An account on a hosting service, such as a GitHub account or an account on a Forgejo server: what Runesmith may list, clone, link
/// to and open pull requests on.</summary>
public interface IRepositoryHost
{
    /// <summary>Gets a stable id, such as <c>github</c> or <c>forgejo:codeberg.org:alice</c>.</summary>
    string Id { get; }

    /// <summary>Gets the service's name, such as "GitHub", "Codeberg" or "Forgejo (git.example.com)".</summary>
    string Name { get; }

    /// <summary>Gets the name of its icon.</summary>
    string Icon { get; }

    /// <summary>Gets the server, such as <c>github.com</c> or <c>codeberg.org</c>.</summary>
    string Server { get; }

    /// <summary>Gets the signed-in account's login, or null while signed out.</summary>
    string? Account { get; }

    /// <summary>Gets the signed-in account's avatar.</summary>
    Uri? AccountAvatarUrl { get; }

    /// <summary>Gets whether pull requests are created as drafts when asked.</summary>
    bool SupportsDraftPullRequests { get; }

    /// <summary>Gets the page where the user chooses what Runesmith may access, or null when the service has none.</summary>
    Uri? ManageAccessUrl { get; }

    /// <summary>Asks the user to sign in, with the service's own dialog; returns whether they are signed in afterwards.</summary>
    Task<bool> SignInAsync(CancellationToken cancellationToken);

    /// <summary>Signs out and forgets the account's tokens.</summary>
    Task SignOutAsync();

    /// <summary>Lists the repositories the account may use.</summary>
    Task<IReadOnlyList<HostedRepository>> GetRepositoriesAsync(CancellationToken cancellationToken);

    /// <summary>Whether a remote URL points at this service's server.</summary>
    bool Owns(string remoteUrl);

    /// <summary>Gets the web page of something in the repository a remote points at, or null when the remote is not this service's.</summary>
    Uri? GetWebUrl(string remoteUrl, WebTarget target);

    /// <summary>Gets the repository's default branch.</summary>
    Task<string?> GetDefaultBranchAsync(string remoteUrl, CancellationToken cancellationToken);

    /// <summary>Lists the open pull requests of the repository a remote points at.</summary>
    Task<IReadOnlyList<PullRequest>> GetPullRequestsAsync(string remoteUrl, CancellationToken cancellationToken);

    /// <summary>Creates a pull request in the repository a remote points at.</summary>
    Task<PullRequest> CreatePullRequestAsync(string remoteUrl, PullRequestDraft draft, CancellationToken cancellationToken);

    /// <summary>Raised when the account signs in or out, or its access changes.</summary>
    event EventHandler? Changed;
}

/// <summary>Supplies repository hosts: one per signed-in account, plus a way to add accounts, such as on another server. Export it with
/// <c>[Export(typeof(IRepositoryHostProvider))]</c>.</summary>
public interface IRepositoryHostProvider
{
    /// <summary>Gets the service's name, such as "GitHub" or "Forgejo".</summary>
    string Name { get; }

    /// <summary>Gets the name of its icon.</summary>
    string Icon { get; }

    /// <summary>Gets the hosts: its accounts, signed in or waiting to be, in the order they show.</summary>
    IReadOnlyList<IRepositoryHost> Hosts { get; }

    /// <summary>Asks the user for a new account, such as a server address and a sign-in; returns it, or null when they cancelled.</summary>
    Task<IRepositoryHost?> AddAccountAsync(CancellationToken cancellationToken);

    /// <summary>Raised when hosts are added or removed.</summary>
    event EventHandler? Changed;
}
