using System.Net;
using Runesmith.GitHub.GitHub;

namespace Runesmith.GitHub.Tests.GitHub;

public sealed class GitHubRepositoriesTests : IDisposable
{
    private readonly FakeServer server = new();
    private readonly HttpClient http;

    public GitHubRepositoriesTests() => http = new HttpClient(server);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAppSignInListsTheRepositoriesOfEveryInstallationOnce()
    {
        server.On("/user", Recorded.User);
        server.On("/user/installations", Recorded.Installations);
        server.On("/user/installations/1001/repositories", Recorded.RepositoryPage(Recorded.Repository(1, "octocat", "hello"), Recorded.Repository(3, "github", "docs", "Organization")));
        server.On("/user/installations/1002/repositories", Recorded.RepositoryPage(Recorded.Repository(2, "github", "linguist", "Organization"),
            Recorded.Repository(3, "github", "docs", "Organization")));
        using var account = CreateAccount();
        await account.SignInAsync(new GitHubTokens("ghu_a", null, null, null), Token);

        var repositories = await new GitHubRepositories(account, new GitHubInstallations(account)).LoadAsync(Token);

        Assert.Equal([1L, 2L, 3L], repositories.Select(r => r.Id).Order());
        Assert.Equal("https://github.com/apps/runesmith/installations/new", account.ManageAccessUri.AbsoluteUri);
    }

    [Fact]
    public async Task AnAppWithoutInstallationsHasNoRepositories()
    {
        server.On("/user", Recorded.User);
        server.On("/user/installations", """{"total_count":0,"installations":[]}""");
        using var account = CreateAccount();
        await account.SignInAsync(new GitHubTokens("ghu_a", null, null, null), Token);

        Assert.Empty(await new GitHubRepositories(account, new GitHubInstallations(account)).LoadAsync(Token));
    }

    [Fact]
    public async Task OtherTokensListTheAccountsOwnRepositories()
    {
        server.On("/user", Recorded.User);
        server.On("/user/repos", "[" + Recorded.Repository(1, "octocat", "hello") + "]");
        using var account = CreateAccount();
        await account.SignInWithTokenAsync("ghp_right", Token);

        var repositories = await new GitHubRepositories(account, new GitHubInstallations(account)).LoadAsync(Token);

        Assert.Equal(["hello"], repositories.Select(r => r.Name));
        Assert.DoesNotContain(server.Requests, request => request.Uri.AbsolutePath == "/user/installations");
    }

    [Fact]
    public async Task TheScreenshotDataReadsLikeGitHubs()
    {
        using var fake = new HttpClient(new FakeGitHubHandler());
        var client = new GitHubClient(fake, new FixedTokens("fake"));

        var installations = await client.GetInstallationsAsync(Token);
        var repositories = await client.GetInstallationRepositoriesAsync(installations[1].Id, Token);
        var pullRequests = await client.GetOpenPullRequestsAsync("lumen-labs", "aurora", Token);

        Assert.Equal(4, installations.Count);
        Assert.NotEmpty(repositories);
        Assert.Equal(5, pullRequests.Count);
        Assert.Equal(HttpStatusCode.NotFound, (await fake.GetAsync(new Uri("https://avatars.githubusercontent.com/u/1"), Token)).StatusCode);
    }

    private GitHubAccount CreateAccount() => new(http, null, new MemorySecretStore(), null, null, TimeProvider.System);

    public void Dispose()
    {
        http.Dispose();
        server.Dispose();
    }
}
