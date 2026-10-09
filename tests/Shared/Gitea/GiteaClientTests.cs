using System.Net;

namespace Runesmith.Plugins.Gitea.Tests;

public sealed class GiteaClientTests : IDisposable
{
    private static readonly GiteaServer Codeberg = GiteaServer.Of("codeberg.org");

    private readonly FakeServer server = new();
    private readonly HttpClient http;
    private readonly FixedTokens tokens = new(new GiteaToken("t0k3n", SignInKind.Token));

    public GiteaClientTests() => http = new HttpClient(server);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => http.Dispose();

    private GiteaClient Client(GiteaServer? at = null, IGiteaTokenSource? source = null) => new(http, at ?? Codeberg, "Codeberg", source ?? tokens);

    [Fact]
    public async Task ReadsTheAccountAndSendsTheUserAgentAndToken()
    {
        server.On("/api/v1/user", Responses.Read("user.json"));

        var user = await Client().GetUserAsync(Token);

        Assert.Equal("forgejo", user.Login);
        Assert.Equal("Forgejo", user.FullName);
        Assert.Equal("https://codeberg.org/avatars/dae8ab126a96f6fbd6942cf08ab92382", user.AvatarUrl);
        var request = Assert.Single(server.Requests);
        Assert.Equal("Runesmith", request.UserAgent);
        Assert.Equal("token t0k3n", request.Authorization);
        Assert.Equal("https://codeberg.org/api/v1/user", request.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task SendsOAuthTokensAsBearer()
    {
        server.On("/api/v1/user", Responses.User);

        await Client(source: new FixedTokens(new GiteaToken("oauth", SignInKind.OAuth))).GetUserAsync(Token);

        Assert.Equal("Bearer oauth", Assert.Single(server.Requests).Authorization);
    }

    [Fact]
    public async Task UsesTheApiUnderAServersPathAndPort()
    {
        server.On("/git/api/v1/user", Responses.User);

        await Client(GiteaServer.Of("git.example.com:3000/git")).GetUserAsync(Token);

        Assert.Equal("https://git.example.com:3000/git/api/v1/user", Assert.Single(server.Requests).Uri.AbsoluteUri);
    }

    [Fact]
    public async Task ReadsARecordedRepository()
    {
        server.On("/api/v1/repos/forgejo/forgejo", Responses.Read("repository.json"));

        var repository = await Client().GetRepositoryAsync("forgejo", "forgejo", Token);

        Assert.Equal("forgejo", repository.DefaultBranch);
        Assert.Equal("https://codeberg.org/forgejo/forgejo.git", repository.CloneUrl);
        Assert.Equal("forgejo", repository.Owner.Login);
    }

    [Fact]
    public async Task FollowsTheLinkHeaderThroughEveryPage()
    {
        server.On(request => request.RequestUri!.Query.Contains("page=1", StringComparison.Ordinal), (_, _) => Page(Responses.Read("pulls.json"),
            "<https://codeberg.org/api/v1/repos/forgejo/forgejo/pulls?state=open&sort=recentupdate&limit=50&page=2>; rel=\"next\""));
        server.On(request => request.RequestUri!.Query.Contains("page=2", StringComparison.Ordinal), (_, _) =>
            Page(Responses.List(Responses.Read("pull-draft.json")), null));

        var pulls = await Client().GetOpenPullRequestsAsync("forgejo", "forgejo", Token);

        Assert.Equal([14764, 14762, 14662], pulls.Select(pull => pull.Number));
        Assert.Equal(2, server.Requests.Count);
        Assert.Contains("limit=50", server.Requests[0].Uri.Query, StringComparison.Ordinal);
        Assert.Contains("state=open", server.Requests[0].Uri.Query, StringComparison.Ordinal);
        Assert.True(pulls[2].Draft);
    }

    [Fact]
    public async Task StopsAtTheLastPageOfTheRecordedHeaders()
    {
        var headers = Responses.Read("pulls.headers.txt");
        var link = headers.Split('\n').First(line => line.StartsWith("link:", StringComparison.Ordinal))["link:".Length..].Trim();
        using var response = Page("[]", link);
        var next = Client().NextPage(response, new Uri("https://codeberg.org/api/v1/repos/forgejo/forgejo/pulls?limit=2&page=1&state=open"), 2, 2);

        Assert.Equal("https://codeberg.org/api/v1/repos/forgejo/forgejo/pulls?limit=2&page=2&state=open", next?.AbsoluteUri);
    }

    [Fact]
    public void NeverFollowsALinkOffTheServersApi()
    {
        using var response = Page("[]", "<https://evil.example/api/v1/user/repos?page=2>; rel=\"next\"");

        Assert.Null(Client().NextPage(response, new Uri("https://codeberg.org/api/v1/user/repos?limit=50&page=1"), 50, 50));
    }

    [Fact]
    public void CountsPagesFromTheTotalWithoutALinkHeader()
    {
        var current = new Uri("https://codeberg.org/api/v1/user/repos?limit=50&page=1");
        using var more = Page("[]", null, total: 120);
        using var done = Page("[]", null, total: 50);

        Assert.Equal("https://codeberg.org/api/v1/user/repos?limit=50&page=2", Client().NextPage(more, current, 50, 50)?.AbsoluteUri);
        Assert.Null(Client().NextPage(done, current, 50, 50));
    }

    [Fact]
    public async Task ListsRepositoriesAPageAtATimeUntilTheTotal()
    {
        var first = Enumerable.Range(1, 50).Select(id => Responses.Repository(id, "alice", "r" + id)).ToArray();
        server.On(request => request.RequestUri!.AbsolutePath == "/api/v1/user/repos" && request.RequestUri.Query.Contains("page=1", StringComparison.Ordinal),
            (_, _) => Page(Responses.List(first), null, total: 51));
        server.On(request => request.RequestUri!.AbsolutePath == "/api/v1/user/repos" && request.RequestUri.Query.Contains("page=2", StringComparison.Ordinal),
            (_, _) => Page(Responses.List(Responses.Repository(51, "alice", "r51")), null, total: 51));

        var repositories = await Client().GetUserRepositoriesAsync(Token);

        Assert.Equal(51, repositories.Count);
        Assert.Equal(2, server.Requests.Count);
    }

    [Fact]
    public async Task ARefusedTokenAsksTheAccountToSignInAgain()
    {
        server.On("/api/v1/user", Responses.Unauthorized, HttpStatusCode.Unauthorized);

        var failure = await Assert.ThrowsAsync<GiteaException>(() => Client().GetUserAsync(Token));

        Assert.Equal(GiteaFailure.Unauthorized, failure.Failure);
        Assert.Contains("Sign in to Codeberg again", failure.Message, StringComparison.Ordinal);
        Assert.Equal(1, tokens.Refusals);
    }

    [Fact]
    public async Task CheckingATokenDoesNotSignTheAccountOut()
    {
        server.On("/api/v1/user", Responses.Unauthorized, HttpStatusCode.Unauthorized);

        await Assert.ThrowsAsync<GiteaException>(() => Client().GetUserAsync(new GiteaToken("pasted", SignInKind.Token), Token));

        Assert.Equal(0, tokens.Refusals);
    }

    [Fact]
    public async Task AMissingScopeSaysSoWithoutTheToken()
    {
        server.On("/api/v1/user", Responses.TokenScopeMissing, HttpStatusCode.Forbidden);

        var failure = await Assert.ThrowsAsync<GiteaException>(() => Client().GetUserAsync(Token));

        Assert.Equal(GiteaFailure.Forbidden, failure.Failure);
        Assert.Contains("read:user", failure.Message, StringComparison.Ordinal);
        Assert.Contains("scopes", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("t0k3n", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnExistingPullRequestIsReportedCalmly()
    {
        server.On("/api/v1/repos/alice/notes/pulls", Responses.PullRequestExists, HttpStatusCode.Conflict);

        var failure = await Assert.ThrowsAsync<GiteaException>(() =>
            Client().CreatePullRequestAsync("alice", "notes", new GiteaNewPullRequest("index", "main", "Add the index", null), Token));

        Assert.Equal(GiteaFailure.Invalid, failure.Failure);
        Assert.StartsWith("Codeberg did not accept it: pull request already exists", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreatesAPullRequestWithItsBranchesTitleAndBody()
    {
        server.On("/api/v1/repos/alice/notes/pulls", Responses.CreatedPullRequest, HttpStatusCode.Created);

        var created = await Client().CreatePullRequestAsync("alice", "notes", new GiteaNewPullRequest("index", "main", "WIP: Add the index", "Lists every note."), Token);

        Assert.Equal(12, created.Number);
        var request = Assert.Single(server.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("""{"head":"index","base":"main","title":"WIP: Add the index","body":"Lists every note."}""", request.Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, GiteaFailure.NotFound)]
    [InlineData(HttpStatusCode.UnprocessableEntity, GiteaFailure.Invalid)]
    [InlineData(HttpStatusCode.InternalServerError, GiteaFailure.Server)]
    [InlineData(HttpStatusCode.BadGateway, GiteaFailure.Server)]
    public async Task TellsFailuresApart(HttpStatusCode status, object expected)
    {
        server.On("/api/v1/repos/alice/notes", "<html>oops</html>", status);

        var failure = await Assert.ThrowsAsync<GiteaException>(() => Client().GetRepositoryAsync("alice", "notes", Token));

        Assert.Equal((GiteaFailure)expected, failure.Failure);
        Assert.StartsWith("Codeberg ", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnreachableServerIsANetworkFailure()
    {
        server.Unreachable = true;

        var failure = await Assert.ThrowsAsync<GiteaException>(() => Client().GetUserAsync(Token));

        Assert.Equal(GiteaFailure.Network, failure.Failure);
        Assert.Equal("Codeberg could not be reached. Check the connection and try again.", failure.Message);
    }

    [Fact]
    public async Task AnAnswerItCannotReadIsAServerFailure()
    {
        server.On("/api/v1/user", "{not json");

        var failure = await Assert.ThrowsAsync<GiteaException>(() => Client().GetUserAsync(Token));

        Assert.Equal(GiteaFailure.Server, failure.Failure);
    }

    [Fact]
    public async Task WithoutATokenItAsksToSignInAndSendsNothing()
    {
        var failure = await Assert.ThrowsAsync<GiteaException>(() => Client(source: new FixedTokens(null)).GetUserAsync(Token));

        Assert.Equal(GiteaFailure.Unauthorized, failure.Failure);
        Assert.Equal("Sign in to Codeberg to continue.", failure.Message);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task ReadsRecordedReviewsAndStatuses()
    {
        server.On("/api/v1/repos/forgejo/forgejo/pulls/14758/reviews", Responses.Read("reviews-14758.json"));
        server.On("/api/v1/repos/forgejo/forgejo/commits/b873c80508c6e34a8898a370b6db73ea9ed85753/status", Responses.Read("status.json"));

        var reviews = await Client().GetReviewsAsync("forgejo", "forgejo", 14758, Token);
        var status = await Client().GetCombinedStatusAsync("forgejo", "forgejo", "b873c80508c6e34a8898a370b6db73ea9ed85753", Token);

        Assert.Contains(reviews, review => review is { State: "APPROVED", User.Login: "0ko" });
        Assert.Equal("pending", status.State);
        Assert.Equal(16, status.TotalCount);
    }

    private static HttpResponseMessage Page(string json, string? link, int? total = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
        if (link is not null)
            response.Headers.Add("Link", link);
        if (total is { } count)
            response.Headers.Add("X-Total-Count", count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return response;
    }
}
