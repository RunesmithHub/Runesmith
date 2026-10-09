using System.Text.Json;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.VersionControl;

namespace Runesmith.Plugins.Gitea;

/// <summary>Shows the sign-in UI for an account; the views implement it, so accounts can be tested without them.</summary>
internal interface ISignInPrompt
{
    /// <summary>Shows the sign-in dialog for an account; returns whether it is signed in afterwards.</summary>
    Task<bool> SignInAsync(GiteaHost host, CancellationToken cancellationToken);
}

/// <summary>One account on a Forgejo or Gitea server: its tokens, kept in the secret store and refreshed before they expire, and what Runesmith
/// does with them through the server's API.</summary>
internal sealed class GiteaHost : IRepositoryHost, IGiteaTokenSource, IDisposable
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(2);
    private const int ParallelRequests = 4;
    private const string DraftPrefix = "WIP: ";
    private static readonly string[] WorkInProgressPrefixes = ["WIP:", "[WIP]"];

    private readonly GiteaContext context;
    private readonly Func<ISignInPrompt?> prompt;
    private readonly SemaphoreSlim gate = new(1, 1);
    private StoredTokens? tokens;
    private bool isLoaded;

    /// <summary>Creates an account; <paramref name="account"/> names it, or has no login for the server's first account before it signs in.</summary>
    public GiteaHost(GiteaContext context, GiteaServer server, StoredAccount account, Func<ISignInPrompt?> prompt)
    {
        this.context = context;
        this.prompt = prompt;
        Server = server;
        Profile = account;
        Client = new GiteaClient(context.Http, server, Name, this);
        OAuth = new OAuthClient(context.Http, server, Name, context.Time);
    }

    /// <summary>Raised on the UI thread when the account signs in or out.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when the account's login or profile changed, so the provider saves the list of accounts.</summary>
    internal event EventHandler? ProfileChanged;

    /// <summary>Gets <c>forgejo:codeberg.org:alice</c>, or <c>forgejo:codeberg.org</c> for a server's first account before it signs in.</summary>
    public string Id => Login is { } login ? $"{context.Flavor.IdPrefix}:{Server.Key}:{login}" : $"{context.Flavor.IdPrefix}:{Server.Key}";

    public string Name => context.Flavor.ServerName(Server);

    public string Icon => context.Flavor.Icon;

    /// <summary>Gets the server's address, such as <c>codeberg.org</c> or <c>git.example.com:3000</c>.</summary>
    string IRepositoryHost.Server => Server.Key;

    public GiteaServer Server { get; }

    /// <summary>Gets the account's login, profile and avatar as last seen, signed in or not.</summary>
    public StoredAccount Profile { get; private set; }

    /// <summary>Gets the account's login, signed in or not, or null before its first sign-in.</summary>
    public string? Login => Profile.Login;

    public string? Account => tokens is not null ? Login : null;

    public Uri? AccountAvatarUrl => Uri.TryCreate(Profile.AvatarUrl, UriKind.Absolute, out var uri) ? uri : null;

    /// <summary>Gets true: a title that starts with <c>WIP:</c> makes a pull request a draft on Forgejo and Gitea.</summary>
    public bool SupportsDraftPullRequests => true;

    public Uri? ManageAccessUrl => Server.ApplicationsUri;

    /// <summary>Gets how the account signed in, or null while it is signed out.</summary>
    public SignInKind? Kind => tokens?.Kind;

    public bool IsSignedIn => tokens is not null;

    public GiteaContext Context => context;

    public GiteaClient Client { get; }

    public OAuthClient OAuth { get; }

    /// <summary>Gets the OAuth2 client id for the server, or null when browser sign-in is not set up for it.</summary>
    public string? ClientId => context.ClientIdFor(Server);

    /// <summary>Reads the tokens from the secret store, once.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (isLoaded)
            return;

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (isLoaded)
                return;

            if (tokens is null && Login is { } login && context.Secrets is { } secrets)
                tokens = Deserialize(await secrets.GetAsync(context.SecretKey(Server, login), cancellationToken).ConfigureAwait(false));
            isLoaded = true;
        }
        finally
        {
            gate.Release();
        }

        RaiseChanged();
    }

    /// <summary>Takes the tokens and profile of another host object that signed in to the same account.</summary>
    internal void TakeTokensFrom(GiteaHost other)
    {
        tokens = other.tokens;
        isLoaded = true;
        Profile = other.Profile;
        RaiseChanged();
    }

    /// <summary>Uses tokens directly, without the secret store, such as for the made-up accounts of screenshots.</summary>
    internal void UseTokens(StoredTokens stored)
    {
        tokens = stored;
        isLoaded = true;
    }

    public async Task<bool> SignInAsync(CancellationToken cancellationToken)
    {
        if (prompt() is not { } signIn)
        {
            context.Notifications?.Notify(NotificationKind.Error, "Signing in needs a newer Runesmith", "This version of Runesmith does not let plugins show dialogs.");
            return false;
        }

        return await signIn.SignInAsync(this, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Finishes an OAuth2 sign-in: exchanges the code, reads the account the tokens belong to and keeps them.</summary>
    public async Task<GiteaUser> SignInWithCodeAsync(LoopbackAuthorization authorization, string code, CancellationToken cancellationToken)
    {
        var received = await OAuth.ExchangeAsync(authorization, code, cancellationToken).ConfigureAwait(false);
        var stored = new StoredTokens(SignInKind.OAuth, received.AccessToken, received.ExpiresAt, received.RefreshToken);
        return await SignInAsync(stored, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Signs in with an access token the user created, after checking that the server accepts it.</summary>
    public Task<GiteaUser> SignInWithTokenAsync(string token, CancellationToken cancellationToken) =>
        SignInAsync(new StoredTokens(SignInKind.Token, token.Trim(), null, null), cancellationToken);

    public async Task SignOutAsync()
    {
        await ForgetTokensAsync().ConfigureAwait(false);
        RaiseChanged();
    }

    public async Task<GiteaToken?> GetTokenAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken).ConfigureAwait(false);
        return tokens switch
        {
            null => null,
            { ExpiresAt: { } expires } when expires - RefreshMargin <= context.Time.GetUtcNow() => await RefreshAsync(cancellationToken).ConfigureAwait(false),
            var current => new GiteaToken(current.AccessToken, current.Kind),
        };
    }

    public void Dispose() => gate.Dispose();

    public void OnUnauthorized()
    {
        if (tokens is not null)
            _ = ExpireAsync($"{Name} no longer accepts the sign-in of {Login}. Sign in again.");
    }

    public async Task<IReadOnlyList<HostedRepository>> GetRepositoriesAsync(CancellationToken cancellationToken)
    {
        var own = Client.GetUserRepositoriesAsync(cancellationToken);
        var organizations = await Client.GetOrganizationsAsync(cancellationToken).ConfigureAwait(false);
        var lists = new List<IReadOnlyList<GiteaRepository>> { await own.ConfigureAwait(false) };
        lists.AddRange(await ForEachAsync(organizations, organization => Client.GetOrganizationRepositoriesAsync(organization.Name, cancellationToken), cancellationToken)
            .ConfigureAwait(false));

        var organizationNames = organizations.Select(organization => organization.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return
        [
            .. lists.SelectMany(list => list)
                .DistinctBy(repository => repository.FullName, StringComparer.OrdinalIgnoreCase)
                .OrderBy(repository => string.Equals(repository.Owner.Login, Login, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(repository => repository.Owner.Login, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(repository => repository.UpdatedAt)
                .Select(repository => ToHosted(repository, organizationNames.Contains(repository.Owner.Login))),
        ];
    }

    public bool Owns(string remoteUrl) => GiteaRemote.Parse(Server, remoteUrl) is not null;

    public Uri? GetWebUrl(string remoteUrl, WebTarget target) => GiteaRemote.Parse(Server, remoteUrl)?.WebUrl(target);

    public async Task<string?> GetDefaultBranchAsync(string remoteUrl, CancellationToken cancellationToken)
    {
        var remote = Remote(remoteUrl);
        var repository = await Client.GetRepositoryAsync(remote.Owner, remote.Name, cancellationToken).ConfigureAwait(false);
        return repository.DefaultBranch is { Length: > 0 } branch ? branch : null;
    }

    public async Task<IReadOnlyList<PullRequest>> GetPullRequestsAsync(string remoteUrl, CancellationToken cancellationToken)
    {
        var remote = Remote(remoteUrl);
        var pulls = await Client.GetOpenPullRequestsAsync(remote.Owner, remote.Name, cancellationToken).ConfigureAwait(false);
        return await ForEachAsync(pulls, pull => DescribeAsync(remote, pull, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public async Task<PullRequest> CreatePullRequestAsync(string remoteUrl, PullRequestDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var remote = Remote(remoteUrl);
        var title = draft.IsDraft && !IsWorkInProgress(draft.Title) ? DraftPrefix + draft.Title : draft.Title;
        var created = await Client.CreatePullRequestAsync(remote.Owner, remote.Name,
            new GiteaNewPullRequest(draft.HeadBranch, draft.BaseBranch, title, draft.Body is { Length: > 0 } body ? body : null), cancellationToken).ConfigureAwait(false);
        return ToPullRequest(created, ReviewState.None, ChecksState.None);
    }

    /// <summary>Whether a title marks a pull request as work in progress, as the servers' default prefixes do.</summary>
    internal static bool IsWorkInProgress(string title) =>
        WorkInProgressPrefixes.Any(prefix => title.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>Sums up reviews the way the server's page does: each reviewer's latest verdict that is not dismissed counts.</summary>
    internal static ReviewState Summarize(IReadOnlyList<GiteaReview> reviews, bool hasRequestedReviewers)
    {
        var latest = reviews
            .Where(review => review.User is not null && !review.Dismissed && review.State is "APPROVED" or "REQUEST_CHANGES")
            .GroupBy(review => review.User!.Login, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(review => review.SubmittedAt).ThenBy(review => review.Id).Last())
            .ToList();
        if (latest.Any(review => review.State == "REQUEST_CHANGES"))
            return ReviewState.ChangesRequested;
        if (latest.Any(review => review.State == "APPROVED" && !review.Stale))
            return ReviewState.Approved;
        return hasRequestedReviewers || reviews.Any(review => review.State == "REQUEST_REVIEW" && !review.Dismissed)
            ? ReviewState.ReviewRequired
            : ReviewState.None;
    }

    /// <summary>Reads a combined commit status: no statuses is no checks; warnings and skipped checks do not fail a pull request.</summary>
    internal static ChecksState Summarize(GiteaCombinedStatus status) => status.TotalCount == 0 ? ChecksState.None : status.State switch
    {
        "pending" => ChecksState.Pending,
        "error" or "failure" => ChecksState.Failing,
        "success" or "warning" or "skipped" => ChecksState.Passing,
        _ => ChecksState.None,
    };

    private async Task<PullRequest> DescribeAsync(GiteaRemote remote, GiteaPullRequest pull, CancellationToken cancellationToken)
    {
        var review = ReviewState.None;
        var checks = ChecksState.None;
        try
        {
            var reviews = await Client.GetReviewsAsync(remote.Owner, remote.Name, pull.Number, cancellationToken).ConfigureAwait(false);
            review = Summarize(reviews, pull.RequestedReviewers is { Count: > 0 });
            if (pull.Head.Sha is { Length: > 0 } sha)
                checks = Summarize(await Client.GetCombinedStatusAsync(remote.Owner, remote.Name, sha, cancellationToken).ConfigureAwait(false));
        }
        catch (GiteaException exception) when (exception.Failure is GiteaFailure.Forbidden or GiteaFailure.NotFound or GiteaFailure.Server)
        {
        }

        return ToPullRequest(pull, review, checks);
    }

    private static PullRequest ToPullRequest(GiteaPullRequest pull, ReviewState review, ChecksState checks) =>
        new(pull.Number, pull.Title, pull.User?.Login ?? "", pull.Head.Ref, pull.Base.Ref, new Uri(pull.HtmlUrl))
        {
            AuthorAvatarUrl = Uri.TryCreate(pull.User?.AvatarUrl, UriKind.Absolute, out var avatar) ? avatar : null,
            IsDraft = pull.Draft ?? IsWorkInProgress(pull.Title),
            Review = review,
            Checks = checks,
            UpdatedAt = pull.UpdatedAt,
            HeadRef = FormattableString.Invariant($"refs/pull/{pull.Number}/head"),
        };

    private static HostedRepository ToHosted(GiteaRepository repository, bool ownerIsOrganization) =>
        new(repository.Owner.Login, repository.Name, repository.CloneUrl, new Uri(repository.HtmlUrl))
        {
            Description = repository.Description is { Length: > 0 } description ? description : null,
            IsPrivate = repository.Private,
            IsFork = repository.Fork,
            IsArchived = repository.Archived,
            OwnerIsOrganization = ownerIsOrganization,
            OwnerAvatarUrl = Uri.TryCreate(repository.Owner.AvatarUrl, UriKind.Absolute, out var avatar) ? avatar : null,
            Language = repository.Language is { Length: > 0 } language ? language : null,
            Stars = repository.StarsCount,
            UpdatedAt = repository.UpdatedAt,
        };

    private static async Task<IReadOnlyList<TResult>> ForEachAsync<TItem, TResult>(IReadOnlyList<TItem> items, Func<TItem, Task<TResult>> run,
        CancellationToken cancellationToken)
    {
        var results = new TResult[items.Count];
        await Parallel.ForAsync(0, items.Count, new ParallelOptions { MaxDegreeOfParallelism = ParallelRequests, CancellationToken = cancellationToken },
            async (index, _) => results[index] = await run(items[index]).ConfigureAwait(false)).ConfigureAwait(false);
        return results;
    }

    private GiteaRemote Remote(string remoteUrl) =>
        GiteaRemote.Parse(Server, remoteUrl) ?? throw new ArgumentException($"The remote is not a repository on {Server.Key}.", nameof(remoteUrl));

    private async Task<GiteaUser> SignInAsync(StoredTokens stored, CancellationToken cancellationToken)
    {
        var user = await Client.GetUserAsync(new GiteaToken(stored.AccessToken, stored.Kind), cancellationToken).ConfigureAwait(false);
        if (Login is { } previous && !string.Equals(previous, user.Login, StringComparison.OrdinalIgnoreCase))
            await ForgetTokensAsync().ConfigureAwait(false);

        if (context.Secrets is { } secrets)
            await secrets.SetAsync(context.SecretKey(Server, user.Login), Serialize(stored), cancellationToken).ConfigureAwait(false);
        tokens = stored;
        isLoaded = true;
        Profile = new StoredAccount(Server.Key, user.Login, user.FullName is { Length: > 0 } name ? name : null, user.AvatarUrl);
        ProfileChanged?.Invoke(this, EventArgs.Empty);
        RaiseChanged();
        return user;
    }

    private async Task<GiteaToken?> RefreshAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (tokens is not { } current)
                return null;
            if (current.ExpiresAt is not { } expires || expires - RefreshMargin > context.Time.GetUtcNow())
                return new GiteaToken(current.AccessToken, current.Kind);

            if (current.RefreshToken is { } refreshToken && ClientId is { } clientId)
            {
                try
                {
                    var received = await OAuth.RefreshAsync(clientId, refreshToken, cancellationToken).ConfigureAwait(false);
                    // A server may accept each refresh token only once, so the new one is kept before anything else can fail.
                    var refreshed = current with { AccessToken = received.AccessToken, ExpiresAt = received.ExpiresAt, RefreshToken = received.RefreshToken ?? refreshToken };
                    tokens = refreshed;
                    if (context.Secrets is { } secrets && Login is { } login)
                        await secrets.SetAsync(context.SecretKey(Server, login), Serialize(refreshed), CancellationToken.None).ConfigureAwait(false);
                    return new GiteaToken(refreshed.AccessToken, refreshed.Kind);
                }
                catch (OAuthException exception) when (exception.Failure == OAuthFailure.Network)
                {
                    throw new GiteaException(GiteaFailure.Network, exception.Message, exception);
                }
                catch (OAuthException)
                {
                }
            }
        }
        finally
        {
            gate.Release();
        }

        await ExpireAsync($"The {Name} sign-in of {Login} expired. Sign in again.").ConfigureAwait(false);
        return null;
    }

    private async Task ExpireAsync(string message)
    {
        await ForgetTokensAsync().ConfigureAwait(false);
        RaiseChanged();
        context.Notifications?.Notify(NotificationKind.Warning, $"Signed out of {Name}", message, "Sign In", () => _ = SignInAsync(CancellationToken.None));
    }

    private async Task ForgetTokensAsync()
    {
        tokens = null;
        if (context.Secrets is { } secrets && Login is { } login)
        {
            try
            {
                await secrets.DeleteAsync(context.SecretKey(Server, login)).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
            }
        }
    }

    private void RaiseChanged() => GiteaContext.OnUiThread(() => Changed?.Invoke(this, EventArgs.Empty));

    internal static string Serialize(StoredTokens stored) => JsonSerializer.Serialize(stored, GiteaJson.Default.StoredTokens);

    private static StoredTokens? Deserialize(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize(json, GiteaJson.Default.StoredTokens);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
