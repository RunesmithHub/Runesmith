using System.Net;
using Runesmith.GitHub.GitHub;

namespace Runesmith.GitHub.Tests.GitHub;

public sealed class GitHubAccountTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeServer server = new();
    private readonly HttpClient http;
    private readonly MemorySecretStore secrets = new();
    private readonly ManualClock clock = new(Start);

    public GitHubAccountTests()
    {
        http = new HttpClient(server);
        server.On("/user", Recorded.User);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ADeviceSignInKeepsBothTokensAndTheirExpiryInTheSecretStore()
    {
        using var account = CreateAccount();

        var user = await account.SignInAsync(new GitHubTokens("ghu_a", Start.AddHours(8), "ghr_a", Start.AddDays(180)), Token);

        Assert.Equal("octocat", user.Login);
        Assert.Equal(GitHubSignInKind.App, account.Kind);
        var stored = Assert.Single(secrets.Secrets);
        Assert.StartsWith("runesmith.github/", stored.Key, StringComparison.Ordinal);
        Assert.Contains("ghu_a", stored.Value, StringComparison.Ordinal);
        Assert.Contains("ghr_a", stored.Value, StringComparison.Ordinal);
        Assert.Contains("access_token_expires", stored.Value, StringComparison.Ordinal);
        Assert.Contains("refresh_token_expires", stored.Value, StringComparison.Ordinal);
        Assert.Equal("Bearer ghu_a", Assert.Single(server.Requests).Authorization);
    }

    [Fact]
    public async Task ReadsTheSessionBackFromTheSecretStore()
    {
        using (var first = CreateAccount())
            await first.SignInAsync(new GitHubTokens("ghu_a", Start.AddHours(8), "ghr_a", Start.AddDays(180)), Token);

        using var account = CreateAccount();
        await account.LoadAsync(Token);

        Assert.Equal("octocat", account.User?.Login);
        Assert.Equal("ghu_a", await account.GetAccessTokenAsync(Token));
    }

    [Fact]
    public async Task RefreshesATokenAboutToExpireAndKeepsTheNewOnes()
    {
        using var account = CreateAccount();
        await account.SignInAsync(new GitHubTokens("ghu_old", Start.AddHours(8), "ghr_old", Start.AddDays(180)), Token);
        server.On("/login/oauth/access_token", Recorded.Tokens);
        clock.Now = Start.AddHours(8).AddMinutes(-1);

        var token = await account.GetAccessTokenAsync(Token);

        Assert.Equal("ghu_16C7e42F292c6912E7710c838347Ae178B4a", token);
        Assert.Contains("refresh_token=ghr_old", server.Requests[^1].Body, StringComparison.Ordinal);
        Assert.Contains("ghr_1B4a2e", secrets.Secrets[GitHubAccount.SessionKey], StringComparison.Ordinal);
        Assert.Equal(token, await account.GetAccessTokenAsync(Token));
        Assert.Single(server.Requests, request => request.Uri.AbsolutePath == "/login/oauth/access_token");
    }

    [Fact]
    public async Task ARefusedRefreshSignsOutAndDeletesTheTokens()
    {
        using var account = CreateAccount();
        await account.SignInAsync(new GitHubTokens("ghu_old", Start.AddHours(8), "ghr_old", Start.AddDays(180)), Token);
        server.On("/login/oauth/access_token", Recorded.BadRefreshToken);
        clock.Now = Start.AddHours(9);

        Assert.Null(await account.GetAccessTokenAsync(Token));

        Assert.False(account.IsSignedIn);
        Assert.Empty(secrets.Secrets);
    }

    [Fact]
    public async Task AnExpiredRefreshTokenSignsOutWithoutAsking()
    {
        using var account = CreateAccount();
        await account.SignInAsync(new GitHubTokens("ghu_old", Start.AddHours(8), "ghr_old", Start.AddDays(180)), Token);
        clock.Now = Start.AddDays(200);

        Assert.Null(await account.GetAccessTokenAsync(Token));

        Assert.False(account.IsSignedIn);
        Assert.DoesNotContain(server.Requests, request => request.Uri.AbsolutePath == "/login/oauth/access_token");
    }

    [Fact]
    public async Task ANetworkFailureWhileRefreshingKeepsTheSession()
    {
        using var account = CreateAccount();
        await account.SignInAsync(new GitHubTokens("ghu_old", Start.AddHours(8), "ghr_old", Start.AddDays(180)), Token);
        server.On(request => request.RequestUri!.AbsolutePath == "/login/oauth/access_token", () => throw new HttpRequestException("offline"));
        clock.Now = Start.AddHours(9);

        var exception = await Assert.ThrowsAsync<GitHubException>(() => account.GetAccessTokenAsync(Token));

        Assert.Equal(GitHubFailure.Network, exception.Failure);
        Assert.True(account.IsSignedIn);
    }

    [Fact]
    public async Task APersonalTokenIsCheckedBeforeItIsKept()
    {
        using var account = CreateAccount();
        server.On(request => request.Headers.Authorization?.Parameter == "ghp_wrong",
            () => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent(Recorded.BadCredentials) });

        await Assert.ThrowsAsync<GitHubException>(() => account.SignInWithTokenAsync("ghp_wrong", Token));
        Assert.Empty(secrets.Secrets);

        await account.SignInWithTokenAsync("  ghp_right\n", Token);
        Assert.Equal(GitHubSignInKind.PersonalToken, account.Kind);
        Assert.Equal("ghp_right", await account.GetAccessTokenAsync(Token));
    }

    [Fact]
    public async Task SigningOutDeletesTheTokens()
    {
        using var account = CreateAccount();
        await account.SignInWithTokenAsync("ghp_right", Token);

        await account.SignOutAsync();

        Assert.False(account.IsSignedIn);
        Assert.Empty(secrets.Secrets);
        Assert.Null(await account.GetAccessTokenAsync(Token));
    }

    [Fact]
    public async Task ARefusedTokenSignsOut()
    {
        using var account = CreateAccount();
        await account.SignInWithTokenAsync("ghp_right", Token);
        server.On("/user/repos", Recorded.BadCredentials, HttpStatusCode.Unauthorized);

        await Assert.ThrowsAsync<GitHubException>(() => account.Client.GetUserRepositoriesAsync(Token));

        Assert.False(account.IsSignedIn);
        Assert.Empty(secrets.Secrets);
    }

    [Fact]
    public void ManageAccessOpensTheAppsInstallationPageOnceItsNameIsKnown()
    {
        using var account = CreateAccount();
        Assert.Equal("https://github.com/settings/installations", account.ManageAccessUri.AbsoluteUri);

        account.RememberAppSlug("runesmith");

        Assert.Equal("https://github.com/apps/runesmith/installations/new", account.ManageAccessUri.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://github.com/octocat/hello.git", true)]
    [InlineData("https://GitHub.com/octocat/hello", true)]
    [InlineData("git@github.com:octocat/hello.git", false)]
    [InlineData("ssh://git@github.com/octocat/hello.git", false)]
    [InlineData("https://gitlab.example.com/octocat/hello.git", false)]
    [InlineData("http://github.com/octocat/hello.git", false)]
    public async Task GivesGitTheTokenOnlyForHttpsRemotesOnGitHub(string remote, bool expected)
    {
        using var account = CreateAccount();
        await account.SignInWithTokenAsync("ghp_right", Token);
        var source = new GitHubCredentialSource(account);

        var credentials = await source.GetAsync(remote, Token);

        Assert.Equal(expected ? new Runesmith.Sdk.VersionControl.GitCredentials("github.com", "x-access-token", "ghp_right") : null, credentials);
    }

    [Fact]
    public async Task GivesGitNothingWhileSignedOut()
    {
        using var account = CreateAccount();

        Assert.Null(await new GitHubCredentialSource(account).GetAsync("https://github.com/octocat/hello.git", Token));
    }

    private GitHubAccount CreateAccount() => new(http, new FixedSettings(new() { [GitHubSettings.ClientId] = "Iv23liExample" }), secrets, notifications: null, commands: null, clock, (_, _) => Task.CompletedTask);

    public void Dispose()
    {
        http.Dispose();
        server.Dispose();
    }
}
