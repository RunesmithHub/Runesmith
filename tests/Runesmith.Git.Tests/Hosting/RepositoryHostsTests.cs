using Runesmith.Git.Hosting;

namespace Runesmith.Git.Tests.Hosting;

public sealed class RepositoryHostsTests
{
    [Fact]
    public void ListsTheProvidersByNameAndTheirHostsInTheirOrder()
    {
        var codeberg = new FakeHost("forgejo:codeberg.org", "codeberg.org");
        var gitHub = new FakeHost("github", "github.com", "octocat");
        using var hosts = new RepositoryHosts([new FakeProvider("GitHub", gitHub), new FakeProvider("Forgejo", codeberg)]);

        Assert.Equal(["Forgejo", "GitHub"], hosts.Providers.Select(p => p.Name));
        Assert.Equal([codeberg, gitHub], hosts.Hosts);
    }

    [Fact]
    public void PrefersOriginThenOtherRemotesThenUpstream()
    {
        using var hosts = new RepositoryHosts([new FakeProvider("Forgejo", new FakeHost("a", "codeberg.org"))]);
        var repository = FakeRepositoryService.Repository(("upstream", "https://codeberg.org/team/hello.git"), ("fork", "https://codeberg.org/bob/hello.git"),
            ("origin", "https://example.com/alice/hello.git"));

        Assert.Equal("fork", hosts.Find(repository)?.Remote.Name);
    }

    [Fact]
    public void CanPreferTheRemoteOfTheUpstream()
    {
        using var hosts = new RepositoryHosts([new FakeProvider("Forgejo", new FakeHost("a", "codeberg.org"))]);
        var forked = FakeRepositoryService.Repository(("origin", "https://codeberg.org/alice/hello.git"), ("fork", "https://codeberg.org/bob/hello.git"));
        var repository = forked with { Upstream = "fork/main" };

        Assert.Equal("origin", hosts.Find(repository)?.Remote.Name);
        Assert.Equal("fork", hosts.Find(repository, preferUpstream: true)?.Remote.Name);
    }

    [Fact]
    public void ASignedInHostOwnsARemoteBeforeASignedOutOneOnTheSameServer()
    {
        var signedOut = new FakeHost("a", "git.example.com");
        var signedIn = new FakeHost("b", "git.example.com", "alice");
        using var hosts = new RepositoryHosts([new FakeProvider("Gitea", signedOut, signedIn)]);

        Assert.Same(signedIn, hosts.Owner("https://git.example.com/alice/hello.git"));
        Assert.Null(hosts.Owner("https://elsewhere.example.com/alice/hello.git"));
        Assert.Null(hosts.Find(null));
    }

    [Fact]
    public async Task TellsWhenAHostSignsInOrAProviderAddsOne()
    {
        var host = new FakeHost("a", "codeberg.org") { SignsInAs = "alice" };
        var provider = new FakeProvider("Forgejo", host) { NextAccount = new FakeHost("b", "git.example.com") };
        using var hosts = new RepositoryHosts([provider]);
        var changes = 0;
        hosts.Changed += (_, _) => changes++;

        await host.SignInAsync(TestContext.Current.CancellationToken);
        var added = (FakeHost)(await provider.AddAccountAsync(TestContext.Current.CancellationToken))!;
        await added.SignOutAsync();

        Assert.Equal(3, changes);
    }
}
