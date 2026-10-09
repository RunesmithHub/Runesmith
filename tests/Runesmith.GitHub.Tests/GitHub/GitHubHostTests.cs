using Runesmith.GitHub.GitHub;
using Runesmith.GitHub.Views.Account;
using Runesmith.Sdk.VersionControl;

namespace Runesmith.GitHub.Tests.GitHub;

public sealed class GitHubHostTests : IDisposable
{
    private const string Remote = "git@github.com:octocat/hello.git";

    private readonly FakeServer server = new();
    private readonly HttpClient http;
    private readonly GitHubAccount account;
    private readonly GitHubHost host;

    public GitHubHostTests()
    {
        http = new HttpClient(server);
        account = new GitHubAccount(http, null, new MemorySecretStore(), null, null, TimeProvider.System);
        var installations = new GitHubInstallations(account);
        host = new GitHubHost(account, new GitHubRepositories(account, installations), installations, new AccountActions(account, null, null, null, null));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("https://github.com/octocat/hello.git", true)]
    [InlineData(Remote, true)]
    [InlineData("https://codeberg.org/octocat/hello.git", false)]
    public void OwnsTheRemotesOnGitHub(string url, bool owns) => Assert.Equal(owns, host.Owns(url));

    [Fact]
    public void LinksEveryKindOfTarget()
    {
        Assert.Equal("https://github.com/octocat/hello", Link(new WebTarget(WebTargetKind.Repository)));
        Assert.Equal("https://github.com/octocat/hello/blob/main/src/App.cs#L4-L9", Link(new WebTarget(WebTargetKind.File, "main", "src/App.cs") { FirstLine = 4, LastLine = 9 }));
        Assert.Equal("https://github.com/octocat/hello/tree/main/src", Link(new WebTarget(WebTargetKind.Folder, "main", "src")));
        Assert.Equal("https://github.com/octocat/hello/commit/abc123", Link(new WebTarget(WebTargetKind.Commit, "abc123")));
        Assert.Equal("https://github.com/octocat/hello/tree/feature/login", Link(new WebTarget(WebTargetKind.Branch, "feature/login")));
        Assert.Null(host.GetWebUrl("https://codeberg.org/octocat/hello.git", new WebTarget(WebTargetKind.Repository)));
    }

    [Fact]
    public async Task IsSignedOutUntilTheAccountSignsIn()
    {
        server.On("/user", Recorded.User);
        var changes = 0;
        host.Changed += (_, _) => changes++;
        Assert.Null(host.Account);

        await account.SignInWithTokenAsync("ghp_right", Token);

        Assert.Equal("octocat", host.Account);
        Assert.True(changes > 0);
        Assert.Equal("https://github.com/settings/personal-access-tokens", host.ManageAccessUrl?.AbsoluteUri);
    }

    [Fact]
    public async Task ListsPullRequestsWithTheirReviewsAndChecks()
    {
        using var fake = new HttpClient(new FakeGitHubHandler());
        using var signedIn = new GitHubAccount(fake, null, new MemorySecretStore(), null, null, TimeProvider.System);
        await signedIn.SignInWithTokenAsync("fake", Token);
        var installations = new GitHubInstallations(signedIn);
        var fakeHost = new GitHubHost(signedIn, new GitHubRepositories(signedIn, installations), installations, new AccountActions(signedIn, null, null, null, null));

        var pullRequests = await fakeHost.GetPullRequestsAsync("https://github.com/lumen-labs/aurora.git", Token);

        Assert.Equal(5, pullRequests.Count);
        Assert.All(pullRequests, p => Assert.Equal($"refs/pull/{p.Number}/head", p.HeadRef));
        Assert.Contains(pullRequests, p => p.Review == ReviewState.Approved && p.Checks == ChecksState.Passing);
        Assert.Contains(pullRequests, p => p.IsDraft);
    }

    [Fact]
    public void ProvidesItsOneHost()
    {
        var provider = new GitHubHostProvider(host);

        Assert.Equal("GitHub", provider.Name);
        Assert.Equal([host], provider.Hosts);
    }

    [Fact]
    public async Task AddingAnAccountWhileSignedInGivesTheHostWithoutASignIn()
    {
        server.On("/user", Recorded.User);
        await account.SignInWithTokenAsync("ghp_right", Token);

        Assert.Same(host, await new GitHubHostProvider(host).AddAccountAsync(Token));
    }

    [Fact]
    public void ShowsARepositoryAsTheCloneDialogListsIt()
    {
        var repository = new GitHubRepository(1, "hello", "octo-org/hello", new GitHubOwner("octo-org", "https://avatars.githubusercontent.com/u/2", "Organization"), true,
            "Hi", "C#", 12, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "https://github.com/octo-org/hello.git", "https://github.com/octo-org/hello", "main", false, false);

        var hosted = GitHubHost.ToHosted(repository);

        Assert.Equal(("octo-org", "hello", "https://github.com/octo-org/hello.git"), (hosted.Owner, hosted.Name, hosted.CloneUrl));
        Assert.True(hosted.IsPrivate);
        Assert.True(hosted.OwnerIsOrganization);
        Assert.Equal(12, hosted.Stars);
    }

    public void Dispose()
    {
        account.Dispose();
        http.Dispose();
        server.Dispose();
    }

    private string? Link(WebTarget target) => host.GetWebUrl(Remote, target)?.AbsoluteUri;
}
