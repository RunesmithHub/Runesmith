using System.Net;
using Runesmith.GitHub.GitHub;
using Runesmith.Sdk.VersionControl;

namespace Runesmith.GitHub.Tests.GitHub;

public sealed class GitHubClientTests : IDisposable
{
    private readonly FakeServer server = new();
    private readonly FixedTokens tokens = new("ghu_token");
    private readonly HttpClient http;
    private readonly GitHubClient client;

    public GitHubClientTests()
    {
        http = new HttpClient(server);
        client = new GitHubClient(http, tokens, new ManualClock(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero)));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReadsTheAccountWithTheTokenAndGitHubsHeaders()
    {
        server.On("/user", Recorded.User);

        var user = await client.GetUserAsync(Token);

        Assert.Equal("octocat", user.Login);
        Assert.Equal("The Octocat", user.Name);
        Assert.Equal("https://avatars.githubusercontent.com/u/583231?v=4", user.AvatarUrl);
        var request = Assert.Single(server.Requests);
        Assert.Equal("https://api.github.com/user", request.Uri.AbsoluteUri);
        Assert.Equal("Bearer ghu_token", request.Authorization);
        Assert.Equal("Runesmith", request.UserAgent);
        Assert.Equal("2022-11-28", request.ApiVersion);
        Assert.Equal("application/vnd.github+json", request.Accept);
    }

    [Fact]
    public async Task ChecksAGivenTokenWithoutTheAccountsOwn()
    {
        server.On("/user", Recorded.User);

        await client.GetUserAsync("ghp_pasted", Token);

        Assert.Equal("Bearer ghp_pasted", Assert.Single(server.Requests).Authorization);
    }

    [Fact]
    public async Task FollowsTheLinkHeaderThroughEveryPage()
    {
        server.On("/user/installations/1001/repositories?per_page=100", Recorded.RepositoryPage(Recorded.Repository(1, "octocat", "one"), Recorded.Repository(2, "octocat", "two")),
            headers: response => response.Headers.Add("Link",
                """<https://api.github.com/user/installations/1001/repositories?per_page=100&page=2>; rel="next", <https://api.github.com/user/installations/1001/repositories?per_page=100&page=2>; rel="last" """));
        server.On("/user/installations/1001/repositories?per_page=100&page=2", Recorded.RepositoryPage(Recorded.Repository(3, "octocat", "three")),
            headers: response => response.Headers.Add("Link", """<https://api.github.com/user/installations/1001/repositories?per_page=100&page=1>; rel="prev" """));

        var repositories = await client.GetInstallationRepositoriesAsync(1001, Token);

        Assert.Equal(["one", "two", "three"], repositories.Select(r => r.Name));
        Assert.Equal(2, server.Requests.Count);
        Assert.All(server.Requests, request => Assert.Equal("Bearer ghu_token", request.Authorization));
    }

    [Fact]
    public async Task NeverFollowsAPageLinkToAnotherHost()
    {
        server.On("/user/repos", "[" + Recorded.Repository(1, "octocat", "one") + "]",
            headers: response => response.Headers.Add("Link", """<https://example.com/steal?page=2>; rel="next" """));

        var repositories = await client.GetUserRepositoriesAsync(Token);

        Assert.Single(repositories);
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task AsksForTheAccountsOwnCollaboratorAndOrganizationRepositories()
    {
        server.On("/user/repos", "[]");

        await client.GetUserRepositoriesAsync(Token);

        Assert.Contains("affiliation=owner,collaborator,organization_member", Assert.Single(server.Requests).Uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadsInstallationsWithTheirAccounts()
    {
        server.On("/user/installations", Recorded.Installations);

        var installations = await client.GetInstallationsAsync(Token);

        Assert.Equal([1001L, 1002L], installations.Select(i => i.Id));
        Assert.Equal("runesmith", installations[0].AppSlug);
        Assert.False(installations[0].Account.IsOrganization);
        Assert.True(installations[1].Account.IsOrganization);
    }

    [Fact]
    public async Task ARefusedTokenAsksToSignInAgain()
    {
        server.On("/user", Recorded.BadCredentials, HttpStatusCode.Unauthorized);

        var exception = await Assert.ThrowsAsync<GitHubException>(() => client.GetUserAsync(Token));

        Assert.Equal(GitHubFailure.Unauthorized, exception.Failure);
        Assert.Contains("Sign in to GitHub again", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, tokens.Refusals);
    }

    [Fact]
    public async Task ARefusedPastedTokenDoesNotSignTheAccountOut()
    {
        server.On("/user", Recorded.BadCredentials, HttpStatusCode.Unauthorized);

        await Assert.ThrowsAsync<GitHubException>(() => client.GetUserAsync("ghp_wrong", Token));

        Assert.Equal(0, tokens.Refusals);
    }

    [Fact]
    public async Task AUsedUpRateLimitSaysWhenItResets()
    {
        var reset = new DateTimeOffset(2026, 10, 8, 13, 0, 0, TimeSpan.Zero);
        server.On("/user/repos", Recorded.RateLimited, HttpStatusCode.Forbidden, response =>
        {
            response.Headers.Add("x-ratelimit-remaining", "0");
            response.Headers.Add("x-ratelimit-reset", reset.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        });

        var exception = await Assert.ThrowsAsync<GitHubException>(() => client.GetUserRepositoriesAsync(Token));

        Assert.Equal(GitHubFailure.RateLimited, exception.Failure);
        Assert.Equal(reset, exception.ResetsAt);
        Assert.Contains("rate limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASecondaryRateLimitWaitsAsLongAsGitHubAsks()
    {
        server.On("/user/repos", Recorded.RateLimited, HttpStatusCode.TooManyRequests, response => response.Headers.Add("Retry-After", "60"));

        var exception = await Assert.ThrowsAsync<GitHubException>(() => client.GetUserRepositoriesAsync(Token));

        Assert.Equal(new DateTimeOffset(2026, 10, 8, 12, 1, 0, TimeSpan.Zero), exception.ResetsAt);
    }

    [Fact]
    public async Task AForbiddenRequestPointsToManageAccess()
    {
        server.On("/repos/octocat/hello", """{"message":"Resource not accessible by integration"}""", HttpStatusCode.Forbidden);

        var exception = await Assert.ThrowsAsync<GitHubException>(() => client.GetRepositoryAsync("octocat", "hello", Token));

        Assert.Equal(GitHubFailure.Forbidden, exception.Failure);
        Assert.Contains("Resource not accessible by integration", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Manage Access", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANetworkFailureIsACalmMessageWithoutTheToken()
    {
        server.On(_ => true, () => throw new HttpRequestException("Name or service not known (api.github.com:443)"));

        var exception = await Assert.ThrowsAsync<GitHubException>(() => client.GetUserAsync(Token));

        Assert.Equal(GitHubFailure.Network, exception.Failure);
        Assert.Equal("GitHub could not be reached. Check the connection and try again.", exception.Message);
        Assert.DoesNotContain("ghu_token", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutASignInNothingIsSent()
    {
        var signedOut = new GitHubClient(http, new FixedTokens(null));

        var exception = await Assert.ThrowsAsync<GitHubException>(() => signedOut.GetUserAsync(Token));

        Assert.Equal(GitHubFailure.Unauthorized, exception.Failure);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task ListsPullRequestsWithTheirReviewsAndChecks()
    {
        server.On("/graphql", Recorded.PullRequests);

        var pullRequests = await client.GetOpenPullRequestsAsync("octocat", "hello", Token);

        Assert.Equal([42, 41, 40], pullRequests.Select(p => p.Number));
        Assert.Equal((ReviewState.Approved, ChecksState.Passing, false), (pullRequests[0].Review, pullRequests[0].Checks, pullRequests[0].IsDraft));
        Assert.Equal((ReviewState.ChangesRequested, ChecksState.Failing, true), (pullRequests[1].Review, pullRequests[1].Checks, pullRequests[1].IsDraft));
        Assert.Equal((ReviewState.None, ChecksState.None), (pullRequests[2].Review, pullRequests[2].Checks));
        Assert.Equal("ghost", pullRequests[1].Author);
        Assert.Equal("feature/clone", pullRequests[0].HeadBranch);
        var request = Assert.Single(server.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("\"owner\":\"octocat\"", request.Body, StringComparison.Ordinal);
        Assert.Contains("statusCheckRollup", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KeepsThePullRequestsWhenOnlyTheChecksAreForbidden()
    {
        server.On("/graphql", Recorded.PullRequestsWithoutChecksAccess);

        var pullRequest = Assert.Single(await client.GetOpenPullRequestsAsync("octocat", "hello", Token));

        Assert.Equal((ReviewState.ReviewRequired, ChecksState.None), (pullRequest.Review, pullRequest.Checks));
    }

    [Fact]
    public async Task AMissingRepositoryInGraphQLIsNotFound()
    {
        server.On("/graphql", Recorded.RepositoryNotFound);

        var exception = await Assert.ThrowsAsync<GitHubException>(() => client.GetOpenPullRequestsAsync("octocat", "missing", Token));

        Assert.Equal(GitHubFailure.NotFound, exception.Failure);
    }

    [Fact]
    public async Task CreatesAPullRequest()
    {
        server.On(request => request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/repos/octocat/hello/pulls",
            () => new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent(Recorded.CreatedPullRequest) });

        var created = await client.CreatePullRequestAsync("octocat", "hello", new GitHubNewPullRequest("Add the clone dialog", "feature/clone", "main", "Body", Draft: true), Token);

        Assert.Equal(43, created.Number);
        Assert.Equal("https://github.com/octocat/hello/pull/43", created.HtmlUrl);
        Assert.Equal("""{"title":"Add the clone dialog","head":"feature/clone","base":"main","body":"Body","draft":true}""", Assert.Single(server.Requests).Body);
    }

    [Fact]
    public async Task ARefusedPullRequestCarriesGitHubsReason()
    {
        server.On("/repos/octocat/hello/pulls", Recorded.PullRequestExists, HttpStatusCode.UnprocessableEntity);

        var exception = await Assert.ThrowsAsync<GitHubException>(() =>
            client.CreatePullRequestAsync("octocat", "hello", new GitHubNewPullRequest("Title", "feature/clone", "main", null, Draft: false), Token));

        Assert.Equal(GitHubFailure.Invalid, exception.Failure);
        Assert.Equal("A pull request already exists for octocat:feature/clone.", exception.Message);
    }

    [Fact]
    public async Task ReadsTheBranchesAndTheDefaultBranch()
    {
        server.On("/repos/octocat/hello", Recorded.Repository(1, "octocat", "hello"));
        server.On("/repos/octocat/hello/branches", """[{"name":"zeta"},{"name":"main"},{"name":"alpha"}]""");

        var names = await client.GetBranchNamesAsync("octocat", "hello", Token);
        var repository = await client.GetRepositoryAsync("octocat", "hello", Token);

        Assert.Equal(["zeta", "main", "alpha"], names);
        Assert.Equal("main", repository.DefaultBranch);
    }

    public void Dispose()
    {
        http.Dispose();
        server.Dispose();
    }
}
