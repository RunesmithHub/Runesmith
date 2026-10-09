using System.Net;

namespace Runesmith.Plugins.Gitea.Tests;

public sealed class GiteaProviderTests : IDisposable
{
    private static readonly GiteaServer SelfHosted = GiteaServer.Of("git.example.com:3000/forge");

    private readonly Harness harness = new();
    private readonly string folder = Path.Combine(Path.GetTempPath(), "runesmith-gitea-tests-" + Guid.NewGuid().ToString("N"));

    public GiteaProviderTests() => harness.Server.On(request => request.RequestUri!.AbsolutePath.EndsWith("/api/v1/user", StringComparison.Ordinal),
        (_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Responses.User) });

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string StateFile => Path.Combine(folder, "accounts.json");

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void OffersTheDefaultServerBeforeTheFirstAccount()
    {
        var provider = new GiteaHostProvider(harness.Context, new AccountStore(StateFile));

        var host = Assert.Single(provider.Accounts);

        Assert.Equal(Harness.DefaultServer, host.Server);
        Assert.Null(host.Login);
        Assert.Null(host.Account);
        Assert.Equal(Subject.Flavor.Name, provider.Name);
    }

    [Fact]
    public async Task AddsAnAccountOnAnotherServerAndRemembersIt()
    {
        var provider = new GiteaHostProvider(harness.Context, new AccountStore(StateFile))
        {
            AddAccountPrompt = new FixedServer(SelfHosted),
            SignInPrompt = new TokenPrompt("pasted"),
        };

        var added = await provider.AddAccountAsync(Token);

        Assert.NotNull(added);
        Assert.Equal($"{Subject.Flavor.IdPrefix}:git.example.com:3000/forge:alice", added.Id);
        Assert.Equal("alice", added.Account);
        Assert.Equal("https://git.example.com:3000/forge/api/v1/user", harness.Server.Requests.Single().Uri.AbsoluteUri);
        Assert.Same(added, Assert.Single(provider.Hosts));

        var reopened = new GiteaHostProvider(harness.Context, new AccountStore(StateFile));
        await reopened.LoadAsync(Token);
        var host = Assert.Single(reopened.Accounts);
        Assert.Equal(SelfHosted, host.Server);
        Assert.True(host.IsSignedIn);
        Assert.DoesNotContain("pasted", await File.ReadAllTextAsync(StateFile, Token), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellingTheServerOrTheSignInAddsNothing()
    {
        var provider = new GiteaHostProvider(harness.Context, new AccountStore(StateFile)) { AddAccountPrompt = new FixedServer(null) };
        Assert.Null(await provider.AddAccountAsync(Token));

        provider.AddAccountPrompt = new FixedServer(SelfHosted);
        provider.SignInPrompt = new TokenPrompt(null);
        Assert.Null(await provider.AddAccountAsync(Token));

        Assert.Null(Assert.Single(provider.Accounts).Login);
        Assert.False(File.Exists(StateFile));
    }

    [Fact]
    public async Task SigningInToTheSameAccountTwiceKeepsOneEntry()
    {
        var provider = new GiteaHostProvider(harness.Context, new AccountStore(StateFile))
        {
            AddAccountPrompt = new FixedServer(SelfHosted),
            SignInPrompt = new TokenPrompt("first"),
        };
        var first = await provider.AddAccountAsync(Token);
        provider.SignInPrompt = new TokenPrompt("second");

        var second = await provider.AddAccountAsync(Token);

        Assert.Same(first, second);
        Assert.Single(provider.Accounts);
        Assert.Equal("second", (await ((GiteaHost)first!).GetTokenAsync(Token))?.Value);
    }

    [Fact]
    public async Task KeepsAccountsOnSeveralServersApart()
    {
        var provider = new GiteaHostProvider(harness.Context, new AccountStore(StateFile)) { SignInPrompt = new TokenPrompt("a") };
        await Assert.Single(provider.Accounts).SignInAsync(Token);
        provider.AddAccountPrompt = new FixedServer(SelfHosted);
        provider.SignInPrompt = new TokenPrompt("b");
        await provider.AddAccountAsync(Token);

        Assert.Equal(2, provider.Accounts.Count);
        Assert.Equal(2, harness.Secrets.Secrets.Count);
        Assert.Contains(harness.SecretKey("alice"), harness.Secrets.Secrets.Keys);
        Assert.Contains(harness.SecretKey("alice", SelfHosted), harness.Secrets.Secrets.Keys);
    }

    [Fact]
    public async Task RemovingAnAccountForgetsItAndItsToken()
    {
        var provider = new GiteaHostProvider(harness.Context, new AccountStore(StateFile)) { SignInPrompt = new TokenPrompt("a") };
        var host = Assert.Single(provider.Accounts);
        await host.SignInAsync(Token);

        await provider.RemoveAsync(host);

        Assert.Empty(harness.Secrets.Secrets);
        Assert.Null(Assert.Single(provider.Accounts).Login);
        Assert.Equal("[]", await File.ReadAllTextAsync(StateFile, Token));
    }

    [Fact]
    public async Task GivesGitTheTokenOfTheAccountForHttpsRemotesOnly()
    {
        var provider = new GiteaHostProvider(harness.Context, new AccountStore(StateFile)) { SignInPrompt = new TokenPrompt("pasted") };
        await Assert.Single(provider.Accounts).SignInAsync(Token);
        var source = new GiteaCredentialSource(provider);
        var server = Harness.DefaultServer.Key;

        var credentials = await source.GetAsync($"https://{server}/alice/notes.git", Token);

        Assert.Equal(new Runesmith.Sdk.VersionControl.GitCredentials(server, "alice", "pasted"), credentials);
        Assert.Null(await source.GetAsync($"git@{server}:alice/notes.git", Token));
        Assert.Null(await source.GetAsync("https://github.com/alice/notes.git", Token));
        Assert.Null(await source.GetAsync($"https://{server}.evil.example/alice/notes.git", Token));
    }

    [Fact]
    public async Task GivesTheTokenOfTheAccountTheRemoteNames()
    {
        harness.Server.On(request => request.Headers.Authorization?.Parameter == "bob-token", (_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Responses.User.Replace("\"alice\"", "\"bob\"", StringComparison.Ordinal)) });
        var provider = new GiteaHostProvider(harness.Context, new AccountStore(StateFile)) { SignInPrompt = new TokenPrompt("alice-token") };
        await Assert.Single(provider.Accounts).SignInAsync(Token);
        provider.AddAccountPrompt = new FixedServer(Harness.DefaultServer);
        provider.SignInPrompt = new TokenPrompt("bob-token");
        await provider.AddAccountAsync(Token);
        var source = new GiteaCredentialSource(provider);
        var server = Harness.DefaultServer.Key;

        Assert.Equal("bob-token", (await source.GetAsync($"https://bob@{server}/team/api.git", Token))?.Token);
        Assert.Equal("alice-token", (await source.GetAsync($"https://{server}/team/api.git", Token))?.Token);
    }

    [Fact]
    public async Task GivesNothingWhileSignedOut()
    {
        var provider = new GiteaHostProvider(harness.Context, new AccountStore(StateFile));

        Assert.Null(await new GiteaCredentialSource(provider).GetAsync($"https://{Harness.DefaultServer.Key}/alice/notes.git", Token));
    }

    private sealed class FixedServer(GiteaServer? server) : IAddAccountPrompt
    {
        public Task<GiteaServer?> AskServerAsync(CancellationToken cancellationToken) => Task.FromResult(server);
    }
}
