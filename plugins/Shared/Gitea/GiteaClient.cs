using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;

namespace Runesmith.Plugins.Gitea;

/// <summary>Why a request to a server did not work.</summary>
internal enum GiteaFailure
{
    /// <summary>The server does not accept the token, or no one is signed in.</summary>
    Unauthorized,

    /// <summary>The token may not do this.</summary>
    Forbidden,

    /// <summary>The server has no such thing, or the account cannot see it.</summary>
    NotFound,

    /// <summary>The server refused what was sent, such as a pull request that exists already.</summary>
    Invalid,

    /// <summary>The server could not be reached.</summary>
    Network,

    /// <summary>The server answered with an error or an answer Runesmith does not understand.</summary>
    Server,
}

/// <summary>A failed request, with a message for people; it never carries a token.</summary>
internal sealed class GiteaException(GiteaFailure failure, string message, Exception? inner = null) : Exception(message, inner)
{
    public GiteaFailure Failure { get; } = failure;
}

/// <summary>A token and how the server expects it: OAuth2 access tokens as <c>Bearer</c>, access tokens as <c>token</c>.</summary>
internal sealed record GiteaToken(string Value, SignInKind Kind)
{
    public AuthenticationHeaderValue Header => new(Kind == SignInKind.OAuth ? "Bearer" : "token", Value);

    public override string ToString() => $"{Kind} token";
}

/// <summary>Gives the client the account's token, and hears when the server refuses it.</summary>
internal interface IGiteaTokenSource
{
    /// <summary>Gets a token that is valid now, refreshed first when it is about to expire, or null when the account is signed out.</summary>
    Task<GiteaToken?> GetTokenAsync(CancellationToken cancellationToken);

    /// <summary>Called when the server answers 401 to the token, so the account can ask the user to sign in again.</summary>
    void OnUnauthorized();
}

/// <summary>A small typed client for the parts of the Forgejo and Gitea REST API (<c>/api/v1</c>) that Runesmith uses, for one server.</summary>
/// <param name="http">The client to send with.</param>
/// <param name="server">The server; tokens are sent to its address only.</param>
/// <param name="serverName">The server's name in messages, such as "Codeberg".</param>
/// <param name="tokens">Where the account's token comes from.</param>
internal sealed partial class GiteaClient(HttpClient http, GiteaServer server, string serverName, IGiteaTokenSource tokens)
{
    /// <summary>The page size asked for; servers allow 50 by default.</summary>
    public const int PageSize = 50;

    private const int MaxPages = 40;

    public GiteaServer Server => server;

    /// <summary>Gets the signed-in account.</summary>
    public Task<GiteaUser> GetUserAsync(CancellationToken cancellationToken) =>
        GetAsync("user", GiteaJson.Default.GiteaUser, token: null, cancellationToken);

    /// <summary>Gets the account a token belongs to, which also checks that the server accepts it.</summary>
    public Task<GiteaUser> GetUserAsync(GiteaToken token, CancellationToken cancellationToken) =>
        GetAsync("user", GiteaJson.Default.GiteaUser, token, cancellationToken);

    /// <summary>Gets the repositories the account owns or collaborates on.</summary>
    public Task<IReadOnlyList<GiteaRepository>> GetUserRepositoriesAsync(CancellationToken cancellationToken) =>
        GetPagesAsync(Paged("user/repos"), GiteaJson.Default.ListGiteaRepository, MaxPages, cancellationToken);

    /// <summary>Gets the organizations the account belongs to.</summary>
    public Task<IReadOnlyList<GiteaOrganization>> GetOrganizationsAsync(CancellationToken cancellationToken) =>
        GetPagesAsync(Paged("user/orgs"), GiteaJson.Default.ListGiteaOrganization, MaxPages, cancellationToken);

    /// <summary>Gets the repositories of an organization that the account can see.</summary>
    public Task<IReadOnlyList<GiteaRepository>> GetOrganizationRepositoriesAsync(string organization, CancellationToken cancellationToken) =>
        GetPagesAsync(Paged($"orgs/{Escape(organization)}/repos"), GiteaJson.Default.ListGiteaRepository, MaxPages, cancellationToken);

    public Task<GiteaRepository> GetRepositoryAsync(string owner, string name, CancellationToken cancellationToken) =>
        GetAsync($"repos/{Escape(owner)}/{Escape(name)}", GiteaJson.Default.GiteaRepository, token: null, cancellationToken);

    /// <summary>Gets a repository's open pull requests, most recently updated first, up to two pages.</summary>
    public Task<IReadOnlyList<GiteaPullRequest>> GetOpenPullRequestsAsync(string owner, string name, CancellationToken cancellationToken) =>
        GetPagesAsync(Paged($"repos/{Escape(owner)}/{Escape(name)}/pulls?state=open&sort=recentupdate"), GiteaJson.Default.ListGiteaPullRequest, 2,
            cancellationToken);

    /// <summary>Gets the reviews of a pull request, oldest first.</summary>
    public Task<IReadOnlyList<GiteaReview>> GetReviewsAsync(string owner, string name, int number, CancellationToken cancellationToken) =>
        GetPagesAsync(Paged(FormattableString.Invariant($"repos/{Escape(owner)}/{Escape(name)}/pulls/{number}/reviews")), GiteaJson.Default.ListGiteaReview, 4,
            cancellationToken);

    /// <summary>Gets the combined status of a commit, the worst of its statuses.</summary>
    public Task<GiteaCombinedStatus> GetCombinedStatusAsync(string owner, string name, string commit, CancellationToken cancellationToken) =>
        GetAsync($"repos/{Escape(owner)}/{Escape(name)}/commits/{Escape(commit)}/status", GiteaJson.Default.GiteaCombinedStatus, token: null, cancellationToken);

    /// <summary>Opens a pull request.</summary>
    public async Task<GiteaPullRequest> CreatePullRequestAsync(string owner, string name, GiteaNewPullRequest pullRequest, CancellationToken cancellationToken)
    {
        var body = JsonContent.Create(pullRequest, GiteaJson.Default.GiteaNewPullRequest);
        using var response = await SendAsync(HttpMethod.Post, Api($"repos/{Escape(owner)}/{Escape(name)}/pulls"), body, token: null, cancellationToken)
            .ConfigureAwait(false);
        return await ReadAsync(response, GiteaJson.Default.GiteaPullRequest, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Adds the headers every request carries.</summary>
    public static void AddCommonHeaders(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Runesmith", null));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>Gets the next page's address from a response's <c>Link</c> header when it is on the server's API, or else from
    /// <c>X-Total-Count</c> when more items remain.</summary>
    /// <param name="response">The page's response.</param>
    /// <param name="current">The page's address.</param>
    /// <param name="received">How many items all pages so far held.</param>
    /// <param name="lastPageCount">How many items the page held.</param>
    internal Uri? NextPage(HttpResponseMessage response, Uri current, int received, int lastPageCount)
    {
        if (response.Headers.TryGetValues("Link", out var values))
        {
            foreach (var value in values)
            {
                var match = NextLinkPattern().Match(value);
                if (match.Success && Uri.TryCreate(match.Groups[1].Value, UriKind.Absolute, out var uri) && IsOnApi(uri))
                    return uri;
            }

            return null;
        }

        if (lastPageCount == 0 || !response.Headers.TryGetValues("X-Total-Count", out var totals)
            || !int.TryParse(totals.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var total) || received >= total)
            return null;

        var query = System.Web.HttpUtility.ParseQueryString(current.Query);
        var page = int.TryParse(query["page"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 1;
        query["page"] = (page + 1).ToString(CultureInfo.InvariantCulture);
        return new UriBuilder(current) { Query = query.ToString() }.Uri;
    }

    private bool IsOnApi(Uri uri) =>
        uri.Scheme == server.ApiUri.Scheme && uri.IdnHost.Equals(server.Host, StringComparison.OrdinalIgnoreCase) && uri.Port == server.ApiUri.Port
        && uri.AbsolutePath.StartsWith(server.ApiUri.AbsolutePath, StringComparison.Ordinal);

    private static string Paged(string path) =>
        path + (path.Contains('?', StringComparison.Ordinal) ? "&" : "?") + FormattableString.Invariant($"limit={PageSize}&page=1");

    private Uri Api(string path) => new(server.ApiUri, path);

    private static string Escape(string segment) => Uri.EscapeDataString(segment);

    private async Task<T> GetAsync<T>(string path, JsonTypeInfo<T> type, GiteaToken? token, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, Api(path), null, token, cancellationToken).ConfigureAwait(false);
        return await ReadAsync(response, type, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<T>> GetPagesAsync<T>(string path, JsonTypeInfo<List<T>> type, int maxPages, CancellationToken cancellationToken)
    {
        var all = new List<T>();
        Uri? next = Api(path);
        for (var page = 0; next is not null && page < maxPages; page++)
        {
            using var response = await SendAsync(HttpMethod.Get, next, null, token: null, cancellationToken).ConfigureAwait(false);
            var items = await ReadAsync(response, type, cancellationToken).ConfigureAwait(false);
            all.AddRange(items);
            next = NextPage(response, next, all.Count, items.Count);
        }

        return all;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, Uri uri, HttpContent? content, GiteaToken? token, CancellationToken cancellationToken)
    {
        var explicitToken = token is not null;
        token ??= await tokens.GetTokenAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new GiteaException(GiteaFailure.Unauthorized, $"Sign in to {serverName} to continue.");

        using var request = new HttpRequestMessage(method, uri) { Content = content };
        AddCommonHeaders(request);
        request.Headers.Authorization = token.Header;

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new GiteaException(GiteaFailure.Network, $"{serverName} could not be reached. Check the connection and try again.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GiteaException(GiteaFailure.Network, $"{serverName} took too long to answer. Try again in a moment.", exception);
        }

        if (response.IsSuccessStatusCode)
            return response;

        using (response)
        {
            var failure = await ToExceptionAsync(response, cancellationToken).ConfigureAwait(false);
            if (failure.Failure == GiteaFailure.Unauthorized && !explicitToken)
                tokens.OnUnauthorized();
            throw failure;
        }
    }

    private async Task<GiteaException> ToExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var message = (await ReadErrorAsync(response, cancellationToken).ConfigureAwait(false)) is { } body ? Describe(body) : null;
        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new GiteaException(GiteaFailure.Unauthorized, $"{serverName} no longer accepts this sign-in. Sign in to {serverName} again."),
            HttpStatusCode.Forbidden => new GiteaException(GiteaFailure.Forbidden, message is { Length: > 0 }
                ? $"{serverName} refused: {message}. Check that the token has the scopes Runesmith needs."
                : $"{serverName} refused. Check that the token has the scopes Runesmith needs."),
            HttpStatusCode.NotFound => new GiteaException(GiteaFailure.NotFound, $"{serverName} could not find it, or the account has no access to it."),
            HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity => new GiteaException(GiteaFailure.Invalid,
                message is { Length: > 0 } ? $"{serverName} did not accept it: {message}." : $"{serverName} did not accept the request."),
            var status => new GiteaException(GiteaFailure.Server,
                FormattableString.Invariant($"{serverName} answered with an error ({(int)status}). Try again in a moment.")),
        };
    }

    private static string? Describe(GiteaErrorBody body)
    {
        var details = body.Errors?.Where(error => error.Length > 0).ToList() ?? [];
        var text = details.Count > 0 ? string.Join(" ", details) : body.Message;
        return text?.Trim().TrimEnd('.');
    }

    private static async Task<GiteaErrorBody?> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync(GiteaJson.Default.GiteaErrorBody, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or HttpRequestException)
        {
            return null;
        }
    }

    private async Task<T> ReadAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> type, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync(type, cancellationToken).ConfigureAwait(false)
                ?? throw new GiteaException(GiteaFailure.Server, $"{serverName} sent an empty answer.");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new GiteaException(GiteaFailure.Server, $"{serverName} sent an answer Runesmith does not understand.", exception);
        }
    }

    [GeneratedRegex(@"<([^>]+)>\s*;\s*rel=""next""")]
    private static partial Regex NextLinkPattern();
}
