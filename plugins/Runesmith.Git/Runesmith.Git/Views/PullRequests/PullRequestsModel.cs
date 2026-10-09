using Runesmith.Sdk.VersionControl;

namespace Runesmith.Git.Views.PullRequests;

/// <summary>What the Pull Requests window shows.</summary>
internal abstract record PullRequestsState;

/// <summary>No host owns a remote of the open folder's repository, or no repository is open.</summary>
internal sealed record NotHostedState(bool HasRepository) : PullRequestsState;

/// <summary>The host that owns the repository's remote is signed out.</summary>
internal sealed record SignedOutState(IRepositoryHost Host) : PullRequestsState;

/// <summary>The open pull requests.</summary>
internal sealed record LoadedState(PullRequestContext Context, IReadOnlyList<PullRequest> Items) : PullRequestsState;

/// <summary>The host could not list the pull requests.</summary>
internal sealed record FailedState(PullRequestContext Context, string Message) : PullRequestsState;

/// <summary>Decides what the Pull Requests window shows for the open repository and its host, and keeps the last list until it is refreshed or the
/// repository or account changes.</summary>
internal sealed class PullRequestsModel(PullRequestService service)
{
    private LoadedState? shown;
    private string? shownAccount;

    /// <summary>Gets the open repository with the host that owns its remote, signed in or not.</summary>
    public PullRequestContext? Context => service.Current;

    /// <summary>Gets what to show without asking the host, or null when the list must be loaded.</summary>
    /// <param name="force">Whether a list shown already must be loaded again.</param>
    public PullRequestsState? Ready(bool force)
    {
        if (Context is not { } context)
            return new NotHostedState(service.Repositories?.Current is not null);
        if (context.Host.Account is null)
            return new SignedOutState(context.Host);
        return !force && shown is { } last && SameList(last.Context, context) ? last : null;
    }

    /// <summary>Gets whether the list shown is the one of the current repository and account, so a reload can keep it on screen.</summary>
    public bool IsShowing(PullRequestContext context) => shown is { } last && SameList(last.Context, context);

    /// <summary>Loads the open pull requests from the host.</summary>
    public async Task<PullRequestsState> LoadAsync(CancellationToken cancellationToken)
    {
        if (Ready(force: true) is { } state)
            return state;

        var context = Context!;
        try
        {
            var items = await context.Host.GetPullRequestsAsync(context.Remote.FetchUrl, cancellationToken).ConfigureAwait(true);
            shown = new LoadedState(context, items);
            shownAccount = context.Host.Account;
            return shown;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new FailedState(context, exception.Message);
        }
    }

    private bool SameList(PullRequestContext left, PullRequestContext right) =>
        ReferenceEquals(left.Host, right.Host) && shownAccount == right.Host.Account && left.Remote.FetchUrl == right.Remote.FetchUrl;
}
