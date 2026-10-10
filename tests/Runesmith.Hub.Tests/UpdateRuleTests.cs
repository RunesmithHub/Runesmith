using Runesmith.Hub.State;
using Runesmith.Hub.Updates;
using Runesmith.Tests.Hub;
using RunesmithHub.Protocol.Catalog;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Resolution;
using RunesmithHub.Protocol.Updating;
using RunesmithHub.Protocol.Versioning;

namespace Runesmith.Hub.Tests;

public sealed class UpdateRuleTests : IDisposable
{
    private readonly HubFixture fixture = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => fixture.Dispose();

    [Fact]
    public async Task OfficialAndVerifiedPluginsUpdateByThemselvesWithinTheirLine()
    {
        var updates = await FindAsync(Installed(("runesmith.git", "0.3.0", true), ("harbor.issues", "1.2.0", true)));

        Assert.Equal(UpdateKind.Automatic, updates["runesmith.git"].Kind);
        Assert.Equal("0.3.4", updates["runesmith.git"].To.Version.ToString());
        Assert.Equal(UpdateKind.Automatic, updates["harbor.issues"].Kind);
        Assert.Equal("1.3.2", updates["harbor.issues"].To.Version.ToString());
    }

    [Fact]
    public async Task UnverifiedPluginsNeverUpdateByThemselves()
    {
        var updates = await FindAsync(Installed(("lumen.todo", "0.9.2", true), ("harbor.issues", "1.3.2", false), ("runesmith.git", "0.3.4", false)));

        Assert.Equal(UpdateKind.Manual, updates["lumen.todo"].Kind);
        Assert.False(updates.ContainsKey("harbor.issues"));
    }

    [Fact]
    public async Task ANewMajorLineNeedsReview()
    {
        var update = (await FindAsync(Installed(("northwind.charts", "1.9.4", true))))["northwind.charts"];

        Assert.Equal(UpdateKind.NeedsReview, update.Kind);
        Assert.Equal(["It moves to a new line, 2.x."], update.Reasons);
    }

    [Fact]
    public async Task AnUpdateThatAddsACapabilityNeedsReview()
    {
        var view = await ViewAsync();
        var git = view.Plugins["runesmith.git"];
        var newest = git.Versions[0];
        var changed = git with
        {
            Versions = [newest with { Capabilities = newest.Capabilities with { Declared = [.. newest.Capabilities.Declared, new DeclaredCapability { Id = "network", Reason = "Fetches." }] } }, .. git.Versions.Skip(1)],
        };
        var catalog = new HubCatalog([.. view.Plugins.Values.Where(p => p.Id != git.Id), changed], view.Publishers, view.Moderation.Entries);

        var update = Find(catalog, Installed(("runesmith.git", "0.3.0", true)), HubPreferences.Default).Single();

        Assert.Equal(UpdateKind.NeedsReview, update.Kind);
        Assert.Equal(["network"], update.NewCapabilities);
        Assert.Equal(["It adds a capability."], update.Reasons);
    }

    [Fact]
    public async Task AnUpdateThatAddsAnUnverifiedDependencySaysSoAndIsNeverAutomatic()
    {
        var view = await ViewAsync();
        var charts = view.Plugins["northwind.charts"];
        var newest = charts.Versions[0];
        var needsLumen = newest with { Version = new SemanticVersion(2, 1, 5), Dependencies = [new PluginDependency { Id = "lumen.todo", Range = VersionRange.Parse("^1.0.0") }] };
        var changed = charts with { Tier = PluginTier.Unverified, Versions = [needsLumen, .. charts.Versions] };
        var catalog = new HubCatalog([.. view.Plugins.Values.Where(p => p.Id != charts.Id), changed], view.Publishers, view.Moderation.Entries);

        var update = Find(catalog, Installed(("northwind.charts", "2.1.0", true)), HubPreferences.Default).Single();

        Assert.Contains("It adds Lumen Todo, which is unverified.", update.Reasons);
        Assert.Equal(UpdateKind.Manual, update.Kind);
    }

    [Fact]
    public async Task TurningAutomaticUpdatesOffForAllOrOnePluginMakesThemManual()
    {
        var off = await FindAsync(Installed(("runesmith.git", "0.3.0", true)), HubPreferences.Default with { AutoUpdate = false });
        Assert.Equal(UpdateKind.Manual, off["runesmith.git"].Kind);

        var state = Installed(("runesmith.git", "0.3.0", true));
        state = state with { Plugins = [state.Plugins[0] with { AutoUpdate = false }] };
        Assert.Equal(UpdateKind.Manual, (await FindAsync(state))["runesmith.git"].Kind);
    }

    private static HubState Installed(params (string Id, string Version, bool Requested)[] plugins) =>
        new() { Plugins = [.. plugins.Select(p => new InstalledHubPlugin(p.Id, p.Version, new string('0', 64), "official", p.Requested))] };

    private async Task<Dictionary<string, UpdateCandidate>> FindAsync(HubState state, HubPreferences? preferences = null) =>
        Find(new HubCatalog(await ViewAsync()), state, preferences ?? HubPreferences.Default).ToDictionary(u => u.PluginId, StringComparer.Ordinal);

    private static IReadOnlyList<UpdateCandidate> Find(HubCatalog catalog, HubState state, HubPreferences preferences) =>
        UpdateFinder.Find(catalog, request => new Resolver(catalog, new ResolutionSettings(HubFixture.Host) { AllowedTiers = preferences.AllowedTiers })
            .Resolve([.. state.Plugins.Select(p => new InstalledPlugin(p.Id, SemanticVersion.Parse(p.Version), p.Requested))], request), state, preferences, HubFixture.Host);

    private async Task<IndexView> ViewAsync()
    {
        var updater = new IndexUpdater(new DirectoryIndexFetcher(fixture.Index), new InMemoryTrustStore(), fixture.Configuration.TrustRoot!.Value, fixture.Time);
        return (await updater.UpdateAsync(Token)).View!;
    }
}
