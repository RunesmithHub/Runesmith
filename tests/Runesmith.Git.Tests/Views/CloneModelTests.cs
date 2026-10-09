using Runesmith.Git.Hosting;
using Runesmith.Git.Tests.Hosting;
using Runesmith.Git.Views.Clone;

namespace Runesmith.Git.Tests.Views;

public sealed class CloneModelTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void ShowsATabPerHostWithTheSignedInOnesFirstAndTheUrlTabLast()
    {
        var gitHub = new FakeHost("github", "github.com");
        var codeberg = new FakeHost("forgejo:codeberg.org", "codeberg.org", "alice");
        var gitea = new FakeHost("gitea:gitea.com", "gitea.com", "alice");
        using var hosts = new RepositoryHosts([new FakeProvider("GitHub", gitHub), new FakeProvider("Forgejo", codeberg), new FakeProvider("Gitea", gitea)]);

        var sources = new CloneModel(hosts, new HostedRepositories()).Sources;

        Assert.Equal([codeberg, gitea, gitHub], sources.OfType<HostSource>().Select(s => s.Host));
        Assert.Equal([true, true, false], sources.OfType<HostSource>().Select(s => s.IsSignedIn));
        Assert.IsType<UrlSource>(sources[^1]);
    }

    [Fact]
    public void WithoutHostingPluginsOnlyTheUrlTabShows()
    {
        using var hosts = new RepositoryHosts([]);
        var model = new CloneModel(hosts, new HostedRepositories());

        Assert.Equal([UrlSource.Instance], model.Sources);
        Assert.Empty(model.Providers);
    }

    [Fact]
    public async Task AddingAnAccountGivesItsTab()
    {
        var added = new FakeHost("forgejo:git.example.com", "git.example.com", "alice");
        var provider = new FakeProvider("Forgejo", new FakeHost("forgejo:codeberg.org", "codeberg.org")) { NextAccount = added };
        using var hosts = new RepositoryHosts([provider]);
        var model = new CloneModel(hosts, new HostedRepositories());
        var changes = 0;
        model.Changed += (_, _) => changes++;

        var source = await model.AddAccountAsync(model.Providers[0], Token);

        Assert.Same(added, Assert.IsType<HostSource>(source).Host);
        Assert.Contains(model.Sources, s => s is HostSource { Host: var host } && host == added);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task AddingAnOrganizationRunsTheHostsOwnCommandWhenItCanRun()
    {
        var gitHub = new FakeHost("github", "github.com", "alice");
        var codeberg = new FakeHost("forgejo:codeberg.org", "codeberg.org", "alice");
        using var hosts = new RepositoryHosts([new FakeProvider("GitHub", gitHub), new FakeProvider("Forgejo", codeberg)]);
        var commands = new FakeCommands("github.addOrganization");
        var model = new CloneModel(hosts, new HostedRepositories(), commands);

        await model.AddOrganizationAsync(gitHub);

        Assert.True(model.CanAddOrganization(gitHub));
        Assert.False(model.CanAddOrganization(codeberg));
        Assert.Equal(["github.addOrganization"], commands.Executed);
        commands.CanRun = false;
        Assert.False(model.CanAddOrganization(gitHub));
    }

    [Fact]
    public void WithoutCommandsNoOrganizationCanBeAdded()
    {
        using var hosts = new RepositoryHosts([]);

        Assert.False(new CloneModel(hosts, new HostedRepositories()).CanAddOrganization(new FakeHost("github", "github.com", "alice")));
    }

    [Fact]
    public async Task ACancelledAccountGivesNoTab()
    {
        using var hosts = new RepositoryHosts([new FakeProvider("Gitea")]);
        var model = new CloneModel(hosts, new HostedRepositories());

        Assert.Null(await model.AddAccountAsync(model.Providers[0], Token));
        Assert.Equal([UrlSource.Instance], model.Sources);
    }
}
