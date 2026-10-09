using System.Composition;
using Runesmith.Git.Git;
using Runesmith.Git.Hosting;
using Runesmith.Sdk.VersionControl;

namespace Runesmith.Git.Views.PullRequests;

/// <summary>A pull request's title and description, prefilled from the commits of a branch.</summary>
internal sealed record PullRequestText(string Title, string Body);

/// <summary>The repository of the open folder as its host knows it: the remote the host owns, and the host.</summary>
internal sealed record PullRequestContext(RepositoryInfo Repository, GitRemote Remote, IRepositoryHost Host);

/// <summary>Pull requests of the open folder's repository on the host that owns its remote: listing, creating, checking out, and the Git work
/// around them.</summary>
[Export]
[Shared]
[method: ImportingConstructor]
internal sealed class PullRequestService(RepositoryHosts hosts, IGitService git, [Import(AllowDefault = true)] IRepositoryService? repositories)
{
    /// <summary>Gets the service that follows the open folder's repository, or null when there is none.</summary>
    public IRepositoryService? Repositories => repositories;

    /// <summary>Gets the hosts.</summary>
    public RepositoryHosts Hosts => hosts;

    /// <summary>Gets the open folder's repository with the remote a host owns, signed in or not, or null when no host owns any of its remotes.</summary>
    public PullRequestContext? Current =>
        repositories?.Current is { } repository && hosts.Find(repository) is { } found ? new PullRequestContext(repository, found.Remote, found.Host) : null;

    /// <summary>Fetches a pull request's commits into a local branch and switches to it. The branch has the pull request's branch name, or
    /// <c>pr-&lt;number&gt;</c> when a local branch of that name exists already.</summary>
    /// <returns>The local branch.</returns>
    /// <exception cref="GitException">Git failed; the message is Git's own.</exception>
    public async Task<string> CheckoutAsync(PullRequestContext context, PullRequest pullRequest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(pullRequest);
        var root = context.Repository.Root;
        var branch = (await git.RunAsync(root, ["rev-parse", "--verify", "--quiet", $"refs/heads/{pullRequest.HeadBranch}"], null, cancellationToken).ConfigureAwait(false)).Succeeded
            ? FormattableString.Invariant($"pr-{pullRequest.Number}")
            : pullRequest.HeadBranch;
        var source = pullRequest.HeadRef ?? $"refs/heads/{pullRequest.HeadBranch}";
        var fetch = new GitRunOptions { CredentialsFor = context.Remote.FetchUrl };

        if (context.Repository.Branch == branch)
        {
            await RunCheckedAsync(root, ["fetch", context.Remote.Name, source], fetch, cancellationToken).ConfigureAwait(false);
            await RunCheckedAsync(root, ["merge", "--ff-only", "FETCH_HEAD"], null, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await RunCheckedAsync(root, ["fetch", context.Remote.Name, $"+{source}:refs/heads/{branch}"], fetch, cancellationToken).ConfigureAwait(false);
            await RunCheckedAsync(root, ["switch", branch], null, cancellationToken).ConfigureAwait(false);
        }

        await RefreshQuietlyAsync().ConfigureAwait(false);
        return branch;
    }

    /// <summary>Pushes the current branch to the host's remote, setting it as the upstream when it has none.</summary>
    /// <exception cref="InvalidOperationException">HEAD is detached.</exception>
    /// <exception cref="GitException">Git failed.</exception>
    public async Task PushAsync(PullRequestContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var branch = context.Repository.Branch ?? throw new InvalidOperationException("HEAD is detached; switch to a branch first.");
        string[] arguments = context.Repository.Upstream is null ? ["push", "--set-upstream", context.Remote.Name, branch] : ["push", context.Remote.Name, branch];
        await RunCheckedAsync(context.Repository.Root, arguments, new GitRunOptions { CredentialsFor = context.Remote.PushUrl }, cancellationToken).ConfigureAwait(false);
        await RefreshQuietlyAsync().ConfigureAwait(false);
    }

    /// <summary>Gets the repository's default branch from its host and the remote's branches as the last fetch saw them, default first.</summary>
    public async Task<(string DefaultBranch, IReadOnlyList<string> Branches)> GetBranchesAsync(PullRequestContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var defaultBranch = await context.Host.GetDefaultBranchAsync(context.Remote.FetchUrl, cancellationToken).ConfigureAwait(false) ?? "main";
        var prefix = $"refs/remotes/{context.Remote.Name}/";
        var listed = await git.RunAsync(context.Repository.Root, ["for-each-ref", "--format=%(refname)", prefix], null, cancellationToken).ConfigureAwait(false);
        var names = listed.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => name[prefix.Length..])
            .Where(name => name != "HEAD" && name != defaultBranch);
        return (defaultBranch, [defaultBranch, .. names.Order(StringComparer.OrdinalIgnoreCase)]);
    }

    /// <summary>Gets a title and description from the commits on the current branch that the base branch does not have.</summary>
    public async Task<PullRequestText> DraftAsync(PullRequestContext context, string baseBranch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var result = await git.RunAsync(context.Repository.Root,
            ["log", "--no-merges", "--max-count=50", "--format=%s%x1f%b%x1e", $"{context.Remote.Name}/{baseBranch}..HEAD"], null, cancellationToken).ConfigureAwait(false);
        return Draft(result.Succeeded ? result.Output : "", context.Repository.Branch ?? "");
    }

    /// <summary>Gets the text for <c>git log</c> output of subject and body pairs, newest first: one commit gives its subject and body,
    /// several give a title from the branch name and a list of their subjects.</summary>
    internal static PullRequestText Draft(string log, string branch)
    {
        var commits = log.Split('\x1e', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry => entry.Split('\x1f'))
            .Select(parts => (Subject: parts[0].Trim(), Body: parts.Length > 1 ? parts[1].Trim() : ""))
            .Where(commit => commit.Subject.Length > 0)
            .ToList();

        return commits.Count switch
        {
            0 => new PullRequestText(TitleFromBranch(branch), ""),
            1 => new PullRequestText(commits[0].Subject, commits[0].Body),
            _ => new PullRequestText(TitleFromBranch(branch), string.Join('\n', Enumerable.Reverse(commits).Select(commit => "- " + commit.Subject))),
        };
    }

    /// <summary>Turns a branch name such as <c>feature/streaming-export</c> into a title such as "Streaming export".</summary>
    internal static string TitleFromBranch(string branch)
    {
        var name = branch.Contains('/', StringComparison.Ordinal) ? branch[(branch.LastIndexOf('/') + 1)..] : branch;
        var words = name.Replace('-', ' ').Replace('_', ' ').Trim();
        return words.Length == 0 ? branch : char.ToUpperInvariant(words[0]) + words[1..];
    }

    private async Task RunCheckedAsync(string root, IReadOnlyList<string> arguments, GitRunOptions? options, CancellationToken cancellationToken)
    {
        var result = await git.RunAsync(root, arguments, options, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
            throw new GitException(result, arguments);
    }

    private async Task RefreshQuietlyAsync()
    {
        if (repositories is not null)
            await repositories.RefreshAsync().ConfigureAwait(false);
    }
}
