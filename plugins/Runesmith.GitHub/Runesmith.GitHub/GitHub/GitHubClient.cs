using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Runesmith.Sdk.VersionControl;

namespace Runesmith.GitHub.GitHub;

/// <summary>Gives the GitHub client the signed-in account's token, and hears when GitHub refuses it.</summary>
internal interface IGitHubTokenSource
{
    /// <summary>Gets a token that is valid now, refreshing it first when it is about to expire, or null when no one is signed in.</summary>
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken);

    /// <summary>Called when GitHub answers 401 to the token, so the account can ask the user to sign in again.</summary>
    void OnUnauthorized();
}

/// <summary>A small typed client for the parts of GitHub's REST and GraphQL APIs that Runesmith uses.</summary>
internal sealed partial class GitHubClient(HttpClient http, IGitHubTokenSource tokens, TimeProvider? time = null)
{
    /// <summary>The address of GitHub's REST API; tokens are sent to this host only.</summary>
    public static readonly Uri ApiBase = new("https://api.github.com/");

    private const int MaxPages = 50;

    private const string PullRequestsQuery = """
        query($owner: String!, $name: String!) {
          repository(owner: $owner, name: $name) {
            pullRequests(states: OPEN, first: 50, orderBy: {field: UPDATED_AT, direction: DESC}) {
              nodes {
                number title url isDraft headRefName baseRefName updatedAt
                author { login avatarUrl }
                reviewDecision
                commits(last: 1) { nodes { commit { statusCheckRollup { state } } } }
              }
            }
          }
        }
        """;

    private readonly TimeProvider time = time ?? TimeProvider.System;

    /// <summary>Gets the signed-in account.</summary>
    public Task<GitHubUser> GetUserAsync(CancellationToken cancellationToken) =>
        GetAsync("user", GitHubJson.Default.GitHubUser, token: null, cancellationToken);

    /// <summary>Gets the account a token belongs to, which also checks that GitHub accepts the token.</summary>
    public Task<GitHubUser> GetUserAsync(string token, CancellationToken cancellationToken) =>
        GetAsync("user", GitHubJson.Default.GitHubUser, token, cancellationToken);

    /// <summary>Gets the installations of the GitHub App that the signed-in user can reach: their account and organizations.</summary>
    public Task<IReadOnlyList<GitHubInstallation>> GetInstallationsAsync(CancellationToken cancellationToken) =>
        GetPagesAsync("user/installations?per_page=100", GitHubJson.Default.GitHubInstallationPage, page => page.Installations, cancellationToken);

    /// <summary>Gets the repositories an installation gives the signed-in user.</summary>
    public Task<IReadOnlyList<GitHubRepository>> GetInstallationRepositoriesAsync(long installationId, CancellationToken cancellationToken) =>
        GetPagesAsync(FormattableString.Invariant($"user/installations/{installationId}/repositories?per_page=100"), GitHubJson.Default.GitHubRepositoryPage,
            page => page.Repositories, cancellationToken);

    /// <summary>Gets the repositories the signed-in user owns, collaborates on or reaches through an organization; for tokens that are not
    /// the GitHub App's.</summary>
    public Task<IReadOnlyList<GitHubRepository>> GetUserRepositoriesAsync(CancellationToken cancellationToken) =>
        GetPagesAsync("user/repos?per_page=100&sort=pushed&affiliation=owner,collaborator,organization_member", GitHubJson.Default.ListGitHubRepository,
            page => page, cancellationToken);

    public Task<GitHubRepository> GetRepositoryAsync(string owner, string name, CancellationToken cancellationToken) =>
        GetAsync($"repos/{Escape(owner)}/{Escape(name)}", GitHubJson.Default.GitHubRepository, token: null, cancellationToken);

    /// <summary>Gets the names of a repository's branches.</summary>
    public async Task<IReadOnlyList<string>> GetBranchNamesAsync(string owner, string name, CancellationToken cancellationToken)
    {
        var branches = await GetPagesAsync($"repos/{Escape(owner)}/{Escape(name)}/branches?per_page=100", GitHubJson.Default.ListGitHubBranch, page => page,
            cancellationToken).ConfigureAwait(false);
        return [.. branches.Select(branch => branch.Name)];
    }

    /// <summary>Gets a repository's open pull requests, most recently updated first, with their review decision and checks.</summary>
    public async Task<IReadOnlyList<PullRequest>> GetOpenPullRequestsAsync(string owner, string name, CancellationToken cancellationToken)
    {
        var body = JsonContent.Create(new GraphQLRequest(PullRequestsQuery, new PullRequestQueryVariables(owner, name)), GitHubGraphJson.Default.GraphQLRequest);
        using var response = await SendAsync(HttpMethod.Post, new Uri(ApiBase, "graphql"), body, token: null, cancellationToken).ConfigureAwait(false);
        var result = await ReadAsync(response, GitHubGraphJson.Default.GraphQLResponse, cancellationToken).ConfigureAwait(false);

        // Partial errors, such as no access to checks, still come with the rest of the data.
        if (result.Data?.Repository is { } repository)
            return [.. repository.PullRequests.Nodes.Select(node => node.ToPullRequest())];

        var error = result.Errors?.FirstOrDefault();
        throw error?.Type switch
        {
            "NOT_FOUND" => new GitHubException(GitHubFailure.NotFound, $"GitHub could not find {owner}/{name}, or the sign-in has no access to it."),
            "FORBIDDEN" => new GitHubException(GitHubFailure.Forbidden, $"The sign-in may not read the pull requests of {owner}/{name}."),
            _ => new GitHubException(GitHubFailure.Server, error?.Message ?? "GitHub did not return the pull requests."),
        };
    }

    /// <summary>Opens a pull request.</summary>
    public async Task<GitHubCreatedPullRequest> CreatePullRequestAsync(string owner, string name, GitHubNewPullRequest pullRequest, CancellationToken cancellationToken)
    {
        var body = JsonContent.Create(pullRequest, GitHubJson.Default.GitHubNewPullRequest);
        using var response = await SendAsync(HttpMethod.Post, new Uri(ApiBase, $"repos/{Escape(owner)}/{Escape(name)}/pulls"), body, token: null, cancellationToken)
            .ConfigureAwait(false);
        return await ReadAsync(response, GitHubJson.Default.GitHubCreatedPullRequest, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Adds the headers every request to GitHub carries.</summary>
    public static void AddCommonHeaders(HttpRequestMessage request, string accept)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Runesmith", null));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
    }

    private static string Escape(string segment) => Uri.EscapeDataString(segment);

    private async Task<T> GetAsync<T>(string path, JsonTypeInfo<T> type, string? token, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, new Uri(ApiBase, path), null, token, cancellationToken).ConfigureAwait(false);
        return await ReadAsync(response, type, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<TItem>> GetPagesAsync<TPage, TItem>(string path, JsonTypeInfo<TPage> type, Func<TPage, List<TItem>> items,
        CancellationToken cancellationToken)
    {
        var all = new List<TItem>();
        Uri? next = new(ApiBase, path);
        for (var page = 0; next is not null && page < MaxPages; page++)
        {
            using var response = await SendAsync(HttpMethod.Get, next, null, token: null, cancellationToken).ConfigureAwait(false);
            all.AddRange(items(await ReadAsync(response, type, cancellationToken).ConfigureAwait(false)));
            next = NextPage(response);
        }

        return all;
    }

    /// <summary>Gets the next page's address from a response's <c>Link</c> header, when it is on the API's host.</summary>
    internal static Uri? NextPage(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out var values))
            return null;

        foreach (var value in values)
        {
            var match = NextLinkPattern().Match(value);
            if (match.Success && Uri.TryCreate(match.Groups[1].Value, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps && string.Equals(uri.Host, ApiBase.Host, StringComparison.OrdinalIgnoreCase))
                return uri;
        }

        return null;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, Uri uri, HttpContent? content, string? token, CancellationToken cancellationToken)
    {
        var explicitToken = token is not null;
        token ??= await tokens.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new GitHubException(GitHubFailure.Unauthorized, "Sign in to GitHub to continue.");

        using var request = new HttpRequestMessage(method, uri) { Content = content };
        AddCommonHeaders(request, "application/vnd.github+json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new GitHubException(GitHubFailure.Network, "GitHub could not be reached. Check the connection and try again.", inner: exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GitHubException(GitHubFailure.Network, "GitHub took too long to answer. Try again in a moment.", inner: exception);
        }

        if (response.IsSuccessStatusCode)
            return response;

        using (response)
        {
            var failure = await ToExceptionAsync(response, cancellationToken).ConfigureAwait(false);
            if (failure.Failure == GitHubFailure.Unauthorized && !explicitToken)
                tokens.OnUnauthorized();
            throw failure;
        }
    }

    private async Task<GitHubException> ToExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await ReadErrorAsync(response, cancellationToken).ConfigureAwait(false);
        var message = body?.Message;
        var status = response.StatusCode;

        if (status == HttpStatusCode.Unauthorized)
            return new GitHubException(GitHubFailure.Unauthorized, "GitHub no longer accepts this sign-in. Sign in to GitHub again.");

        if (status is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests && RateLimitReset(response) is { } reset)
        {
            var local = reset.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);
            return new GitHubException(GitHubFailure.RateLimited, $"GitHub's rate limit is used up. It resets at {local}.", reset);
        }

        return status switch
        {
            HttpStatusCode.Forbidden => new GitHubException(GitHubFailure.Forbidden, message is { Length: > 0 }
                ? $"GitHub refused: {message}. Check which repositories Runesmith may use with Manage Access."
                : "GitHub refused. Check which repositories Runesmith may use with Manage Access."),
            HttpStatusCode.NotFound => new GitHubException(GitHubFailure.NotFound, "GitHub could not find it, or the sign-in has no access to it."),
            HttpStatusCode.UnprocessableEntity => new GitHubException(GitHubFailure.Invalid, ValidationMessage(body)),
            _ => new GitHubException(GitHubFailure.Server,
                FormattableString.Invariant($"GitHub answered with an error ({(int)status}). Try again in a moment.")),
        };
    }

    private DateTimeOffset? RateLimitReset(HttpResponseMessage response)
    {
        if (Header(response, "x-ratelimit-remaining") == "0"
            && long.TryParse(Header(response, "x-ratelimit-reset"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            return DateTimeOffset.FromUnixTimeSeconds(seconds);

        return response.Headers.RetryAfter?.Delta is { } delay ? time.GetUtcNow() + delay
            : response.Headers.RetryAfter?.Date is { } date ? date
            : null;
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static string ValidationMessage(GitHubErrorBody? body)
    {
        var details = body?.Errors?.Select(error => error.Message).OfType<string>().Where(text => text.Length > 0).ToList() ?? [];
        return details.Count > 0 ? string.Join(" ", details) : body?.Message ?? "GitHub did not accept the request.";
    }

    private static async Task<GitHubErrorBody?> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync(GitHubJson.Default.GitHubErrorBody, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or HttpRequestException)
        {
            return null;
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> type, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync(type, cancellationToken).ConfigureAwait(false)
                ?? throw new GitHubException(GitHubFailure.Server, "GitHub sent an empty answer.");
        }
        catch (JsonException exception)
        {
            throw new GitHubException(GitHubFailure.Server, "GitHub sent an answer Runesmith does not understand.", inner: exception);
        }
    }

    [GeneratedRegex(@"<([^>]+)>\s*;\s*rel=""next""")]
    private static partial Regex NextLinkPattern();
}
