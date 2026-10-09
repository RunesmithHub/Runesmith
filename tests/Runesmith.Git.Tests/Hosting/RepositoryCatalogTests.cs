using Runesmith.Git.Hosting;
using Runesmith.Sdk.VersionControl;

namespace Runesmith.Git.Tests.Hosting;

public sealed class RepositoryCatalogTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GroupsByOwnerWithTheAccountFirstAndTheNewestRepositoriesOnTop()
    {
        var catalog = new RepositoryCatalog(
        [
            Repository("zeta-org", "api", daysAgo: 1, organization: true),
            Repository("octocat", "old", daysAgo: 30),
            Repository("alpha-org", "site", daysAgo: 2, organization: true),
            Repository("octocat", "new", daysAgo: 0),
        ]);

        var rows = catalog.Rows("octocat", search: null, owner: null);

        Assert.Equal(["octocat", "new", "old", "alpha-org", "site", "zeta-org", "api"], rows.Select(Name));
        Assert.True(((OwnerRow)rows[0]).IsAccount);
        Assert.Equal(2, ((OwnerRow)rows[0]).Count);
        Assert.Equal(["octocat", "alpha-org", "zeta-org"], catalog.Owners("octocat").Select(o => o.Login));
    }

    [Fact]
    public void SearchMatchesEveryWordInTheNameOwnerOrDescription()
    {
        var catalog = new RepositoryCatalog(
        [
            Repository("octocat", "hello", description: "A friendly greeting"),
            Repository("octocat", "tools", description: "Build scripts"),
            Repository("github", "docs", description: "Friendly documentation", organization: true),
        ]);

        Assert.Equal(["octocat", "hello", "github", "docs"], catalog.Rows("octocat", "friendly", null).Select(Name));
        Assert.Equal(["github", "docs"], catalog.Rows("octocat", "GITHUB friendly", null).Select(Name));
        Assert.Empty(catalog.Rows("octocat", "nothing", null));
    }

    [Fact]
    public void TheOwnerFilterShowsOneOwner()
    {
        var catalog = new RepositoryCatalog([Repository("octocat", "hello"), Repository("github", "docs", organization: true)]);

        Assert.Equal(["github", "docs"], catalog.Rows("octocat", null, "github").Select(Name));
    }

    [Fact]
    public async Task AnEmptyListIsAskedForAgainBecauseAccessMayJustHaveBeenGranted()
    {
        var host = new FakeHost("github", "github.com", "alice");
        var repositories = new HostedRepositories();
        var token = TestContext.Current.CancellationToken;

        Assert.Empty((await repositories.LoadAsync(host, refresh: false, token)).Repositories);
        host.Repositories.Add(Repository("alice", "hello"));

        Assert.Single((await repositories.LoadAsync(host, refresh: false, token)).Repositories);
        Assert.Equal(2, host.RepositoryLoads);
    }

    [Fact]
    public async Task KeepsAHostsListUntilRefreshedOrAnotherAccountSignsIn()
    {
        var host = new FakeHost("forgejo:codeberg.org", "codeberg.org", "alice");
        host.Repositories.Add(Repository("alice", "hello"));
        var repositories = new HostedRepositories();
        var token = TestContext.Current.CancellationToken;

        await repositories.LoadAsync(host, refresh: false, token);
        await repositories.LoadAsync(host, refresh: false, token);
        Assert.Equal(1, host.RepositoryLoads);

        await repositories.LoadAsync(host, refresh: true, token);
        host.Account = "bob";
        var catalog = await repositories.LoadAsync(host, refresh: false, token);

        Assert.Equal(3, host.RepositoryLoads);
        Assert.Equal(["hello"], catalog.Repositories.Select(r => r.Name));
    }

    [Fact]
    public async Task ForgetsTheListsWhenAHostReportsAChangeSuchAsANewOrganization()
    {
        var host = new FakeHost("github", "github.com", "alice");
        host.Repositories.Add(Repository("alice", "hello"));
        using var hosts = new RepositoryHosts([new FakeProvider("GitHub", host)]);
        var repositories = new HostedRepositories(hosts);
        var token = TestContext.Current.CancellationToken;

        await repositories.LoadAsync(host, refresh: false, token);
        host.Repositories.Add(Repository("lumen-labs", "aurora"));
        await host.SignInAsync(token);
        var catalog = await repositories.LoadAsync(host, refresh: false, token);

        Assert.Equal(2, host.RepositoryLoads);
        Assert.Equal(["aurora", "hello"], catalog.Repositories.Select(r => r.Name).Order());
    }

    private static string Name(RepositoryListRow row) => row switch
    {
        OwnerRow owner => owner.Owner.Login,
        RepositoryRow repository => repository.Repository.Name,
        _ => "",
    };

    private static HostedRepository Repository(string owner, string name, int daysAgo = 0, bool organization = false, string? description = null) =>
        new(owner, name, $"https://github.com/{owner}/{name}.git", new Uri($"https://github.com/{owner}/{name}"))
        {
            Description = description,
            OwnerIsOrganization = organization,
            UpdatedAt = Now.AddDays(-daysAgo),
        };
}
