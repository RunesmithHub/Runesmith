using Runesmith.Tests.Hub;
using RunesmithHub.Protocol.Updating;

namespace Runesmith.Hub.Tests;

public sealed class IndexUpdateTests : IDisposable
{
    private readonly HubFixture fixture = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => fixture.Dispose();

    [Fact]
    public async Task UpdatingVerifiesTheIndexAndKeepsItForTheNextStart()
    {
        var client = fixture.CreateClient();
        Assert.Equal(HubStatusKind.Checking, client.Status.Kind);
        Assert.False(client.Status.CanInstall);

        var status = await client.RefreshAsync(Token);

        Assert.Equal(HubStatusKind.Verified, status.Kind);
        Assert.True(status.CanInstall);
        Assert.Equal(13, client.Catalog!.Plugins.Count());
        Assert.Equal("Lumen Todo", client.Catalog.FindPlugin("lumen.todo")!.Name);

        var later = fixture.CreateClient(fetchers: _ => new UnreachableFetcher());
        Assert.NotNull(later.Catalog);
        Assert.Equal(HubStatusKind.Offline, (await later.RefreshAsync(Token)).Kind);
        Assert.False(later.Status.CanInstall);
        Assert.NotNull(later.Catalog);
    }

    [Fact]
    public async Task AnExpiredTimestampMeansThePluginsSafetyCannotBeConfirmedAndNothingInstalls()
    {
        var client = fixture.CreateClient();
        await client.RefreshAsync(Token);
        fixture.Time.Now = fixture.Time.Now.AddDays(3);

        var status = await client.RefreshAsync(Token);

        Assert.Equal(HubStatusKind.Unverifiable, status.Kind);
        Assert.NotNull(status.Since);
        Assert.False(status.CanInstall);
        Assert.Contains("can't confirm the safety status of your plugins since", status.Describe(), StringComparison.Ordinal);
        var plan = client.Resolve(RunesmithHub.Protocol.Resolution.ResolutionRequest.Install("ember.themes"), HubPreferences.Default);
        await Assert.ThrowsAsync<Installing.InstallException>(() => client.ApplyAsync(plan.Plan!, cancellationToken: Token));
        Assert.Empty(client.State.Plugins);
    }

    [Fact]
    public async Task ASavedViewThatExpiredIsUnverifiableFromTheStart()
    {
        await fixture.CreateClient().RefreshAsync(Token);
        fixture.Time.Now = fixture.Time.Now.AddDays(5);

        var client = fixture.CreateClient(fetchers: _ => new UnreachableFetcher());

        Assert.Equal(HubStatusKind.Unverifiable, client.Status.Kind);
        Assert.Equal(HubStatusKind.Unverifiable, (await client.RefreshAsync(Token)).Kind);
    }

    [Fact]
    public async Task AnIndexThatFailsVerificationLeavesTheSavedViewAndOffersNoInstalls()
    {
        var index = HubFixture.CopyIndex();
        using var tampered = new HubFixture(index);
        var snapshot = Directory.EnumerateFiles(Path.Combine(index, "snapshot")).Single();
        var bytes = File.ReadAllBytes(snapshot);
        bytes[^10] ^= 1;
        File.WriteAllBytes(snapshot, bytes);

        var status = await tampered.CreateClient().RefreshAsync(Token);

        Assert.Equal(HubStatusKind.VerificationFailed, status.Kind);
        Assert.False(status.CanInstall);
    }

    [Fact]
    public async Task ABuildWithoutATrustRootHasNoHub()
    {
        var client = new HubClient(new HubConfiguration(null, [HubFixture.BaseUrl]), fixture.Paths, HubFixture.Host, "0.1.0",
            new FixtureDownloader(fixture.Index), _ => new DirectoryIndexFetcher(fixture.Index), fixture.Time);

        Assert.Equal(HubStatusKind.NotSetUp, (await client.RefreshAsync(Token)).Kind);
        Assert.Null(client.Catalog);
        Assert.Contains("not set up in this build", client.Status.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TurningTheHubOffHidesTheCatalog()
    {
        var client = fixture.CreateClient();
        await client.RefreshAsync(Token);

        client.SetEnabled(false);

        Assert.Equal(HubStatusKind.TurnedOff, client.Status.Kind);
        Assert.Null(client.Catalog);
        Assert.Equal(HubStatusKind.TurnedOff, (await client.RefreshAsync(Token)).Kind);
    }

    [Fact]
    public void TheRootFileAndIndexComeFromTheApplicationFolderUnlessADevelopmentBuildOverridesThem()
    {
        var app = Directory.CreateDirectory(Path.Combine(fixture.Home, "app")).FullName;
        Assert.False(HubConfiguration.Load(app, _ => null).IsSetUp);

        Directory.CreateDirectory(Path.Combine(app, "Hub"));
        File.Copy(Path.Combine(fixture.Index, "root", "1.json"), Path.Combine(app, "Hub", "root.json"));
        var shipped = HubConfiguration.Load(app, _ => null);
        Assert.True(shipped.IsSetUp);
        Assert.Equal([HubConfiguration.DefaultIndexUrl], shipped.IndexUrls);

        var development = HubConfiguration.Load(Path.Combine(fixture.Home, "elsewhere"), name => name switch
        {
            HubConfiguration.RootVariable => Path.Combine(fixture.Index, "root", "1.json"),
            HubConfiguration.IndexUrlVariable => "http://127.0.0.1:47613/",
            _ => null,
        });
        Assert.True(development.IsSetUp);
        Assert.Equal([HubFixture.BaseUrl], development.IndexUrls);
    }

    private sealed class UnreachableFetcher : IIndexFetcher
    {
        public Task<FetchResult> FetchAsync(string path, long maxBytes, CancellationToken cancellationToken) =>
            throw new IndexUnavailableException("The index could not be reached.");
    }
}
