using Runesmith.GitHub.GitHub;

namespace Runesmith.GitHub.Tests.GitHub;

public sealed class GitHubInstallationsTests : IDisposable
{
    private const string OnlyTheUser = """
        {"total_count":1,"installations":[
          {"id":1001,"account":{"login":"octocat","avatar_url":"https://avatars.githubusercontent.com/u/583231?v=4","type":"User"},
           "repository_selection":"selected","app_slug":"runesmith-editor"}]}
        """;

    private static readonly DateTimeOffset Start = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeServer server = new();
    private readonly HttpClient http;

    public GitHubInstallationsTests()
    {
        http = new HttpClient(server);
        server.On("/user", Recorded.User);
        server.On("/user/installations/1001/repositories", Recorded.RepositoryPage(Recorded.Repository(1, "octocat", "hello"), Recorded.Repository(2, "octocat", "notes")));
        server.On("/user/installations/1002/repositories", Recorded.RepositoryPage(Recorded.Repository(3, "github", "docs", "Organization")));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void TheInstallPageIsGitHubsOwnTargetPicker() =>
        Assert.Equal("https://github.com/apps/runesmith-editor/installations/new", GitHubAppUrls.Install("runesmith-editor").AbsoluteUri);

    [Fact]
    public void AnInstallationsSettingsPageDependsOnWhoOwnsIt()
    {
        var user = new GitHubInstallation(1001, new GitHubOwner("octocat", "", "User"), "selected", "runesmith-editor", null);
        var organization = new GitHubInstallation(1002, new GitHubOwner("lumen-labs", "", "Organization"), "all", "runesmith-editor", null);

        Assert.Equal("https://github.com/settings/installations/1001", GitHubAppUrls.Configure(user).AbsoluteUri);
        Assert.Equal("https://github.com/organizations/lumen-labs/settings/installations/1002", GitHubAppUrls.Configure(organization).AbsoluteUri);
    }

    [Fact]
    public async Task TheCardShowsTheUserFirstThenOrganizationsWithWhatEachGives()
    {
        server.On("/user/installations", Recorded.Installations);
        using var account = await SignedInAsync();

        var rows = (await new GitHubInstallations(account).LoadAsync(Token)).Rows("octocat");

        Assert.Equal(["octocat", "github"], rows.Select(r => r.Login));
        Assert.Equal([false, true], rows.Select(r => r.IsOrganization));
        Assert.Equal(["2 selected", "All repositories"], rows.Select(r => r.Scope));
        Assert.Equal("https://github.com/settings/installations/1001", rows[0].ConfigureUri.AbsoluteUri);
        Assert.Equal("https://github.com/organizations/github/settings/installations/1002", rows[1].ConfigureUri.AbsoluteUri);
    }

    [Fact]
    public void OrganizationsAreOrderedByName()
    {
        var snapshot = new InstallationsSnapshot(
        [
            new GitHubInstallation(3, new GitHubOwner("zeta-org", "", "Organization"), "all", null, null),
            new GitHubInstallation(2, new GitHubOwner("Alpha-Org", "", "Organization"), "selected", null, null),
            new GitHubInstallation(1, new GitHubOwner("octocat", "", "User"), "all", null, null),
        ], new Dictionary<long, IReadOnlyList<GitHubRepository>>());

        Assert.Equal(["octocat", "Alpha-Org", "zeta-org"], snapshot.Rows("octocat").Select(r => r.Login));
        Assert.Equal("0 selected", snapshot.Rows("octocat")[1].Scope);
    }

    [Fact]
    public async Task ATokenSignInHasNoInstallations()
    {
        using var account = new GitHubAccount(http, null, new MemorySecretStore(), null, null, TimeProvider.System);
        await account.SignInWithTokenAsync("ghp_right", Token);

        Assert.Empty((await new GitHubInstallations(account).LoadAsync(Token)).Installations);
        Assert.DoesNotContain(server.Requests, request => request.Uri.AbsolutePath == "/user/installations");
    }

    [Fact]
    public async Task ComingBackAfterAnOrganizationInstalledTheAppFindsIt()
    {
        server.Sequence("/user/installations", OnlyTheUser, Recorded.Installations);
        using var account = await SignedInAsync();
        var installations = new GitHubInstallations(account);
        var changes = 0;
        installations.Changed += (_, _) => changes++;
        var watch = new InstallReturn(installations, new ManualClock(Start));

        await watch.BeginAsync(Token);
        var added = await watch.ReturnAsync(Token);

        Assert.Equal(["github"], added.Select(i => i.Account.Login));
        Assert.Equal("Runesmith can now use repositories of github", InstallReturn.Announcement(added));
        Assert.False(watch.IsWaiting);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task ComingBackWithoutANewInstallationSaysNothingAndKeepsWatching()
    {
        server.Sequence("/user/installations", OnlyTheUser, OnlyTheUser, Recorded.Installations);
        using var account = await SignedInAsync();
        var clock = new ManualClock(Start);
        var watch = new InstallReturn(new GitHubInstallations(account), clock);

        await watch.BeginAsync(Token);
        var first = await watch.ReturnAsync(Token);
        clock.Now = Start.AddMinutes(10);
        var second = await watch.ReturnAsync(Token);

        Assert.Empty(first);
        Assert.Null(InstallReturn.Announcement(first));
        Assert.Equal(["github"], second.Select(i => i.Account.Login));
    }

    [Fact]
    public async Task AReturnAfterWatchingStoppedReloadsButAnnouncesNothing()
    {
        server.Sequence("/user/installations", OnlyTheUser, Recorded.Installations);
        using var account = await SignedInAsync();
        var clock = new ManualClock(Start);
        var watch = new InstallReturn(new GitHubInstallations(account), clock);

        await watch.BeginAsync(Token);
        clock.Now = Start + InstallReturn.Patience;

        Assert.False(watch.IsWaiting);
        Assert.Empty(await watch.ReturnAsync(Token));
    }

    [Fact]
    public void SeveralNewOrganizationsAreNamedTogether()
    {
        GitHubInstallation Organization(long id, string login) => new(id, new GitHubOwner(login, "", "Organization"), "all", null, null);

        Assert.Equal("Runesmith can now use repositories of lumen-labs, northwind-tools and harbor-collective",
            InstallReturn.Announcement([Organization(1, "lumen-labs"), Organization(2, "northwind-tools"), Organization(3, "harbor-collective")]));
    }

    private async Task<GitHubAccount> SignedInAsync()
    {
        var account = new GitHubAccount(http, null, new MemorySecretStore(), null, null, TimeProvider.System);
        await account.SignInAsync(new GitHubTokens("ghu_a", null, null, null), Token);
        return account;
    }

    public void Dispose()
    {
        http.Dispose();
        server.Dispose();
    }
}
