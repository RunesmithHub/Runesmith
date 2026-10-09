using Runesmith.Hub.Catalog;
using Runesmith.Hub.Reports;
using Runesmith.Tests.Hub;
using RunesmithHub.Protocol.Catalog;
using RunesmithHub.Protocol.Index;

namespace Runesmith.Hub.Tests;

public sealed class CatalogTests : IDisposable
{
    private readonly HubFixture fixture = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => fixture.Dispose();

    [Fact]
    public async Task SearchRanksTheBestMatchFirstAndHidesWhatTheTierSettingExcludes()
    {
        var client = fixture.CreateClient();
        await client.RefreshAsync(Token);

        Assert.Equal("lumen.todo", client.Search(new CatalogQuery("todo"), HubPreferences.Default).Plugins[0].Id);
        Assert.Equal("harbor.issues", client.Search(new CatalogQuery("harbor.issues"), HubPreferences.Default).Plugins[0].Id);
        Assert.Equal("tide.formatter", client.Search(new CatalogQuery("formater"), HubPreferences.Default).Plugins[0].Id);

        var all = client.Search(new CatalogQuery(""), HubPreferences.Default);
        Assert.Equal(0, all.HiddenByTierSetting);
        Assert.Equal("copper.snippets", all.Plugins[^1].Id);
        Assert.DoesNotContain(all.Plugins, p => p.State == PluginState.Blocked);

        var reviewedOnly = client.Search(new CatalogQuery(""), HubPreferences.Default with { AllowedTiers = AllowedTiers.OfficialAndVerified });
        Assert.All(reviewedOnly.Plugins, p => Assert.NotEqual(PluginTier.Unverified, p.Tier));
        Assert.True(reviewedOnly.HiddenByTierSetting > 0);
    }

    [Fact]
    public async Task BrowseFiltersByTierCategoryAndCompatibility()
    {
        var client = fixture.CreateClient();
        await client.RefreshAsync(Token);

        Assert.All(client.Search(new CatalogQuery("", Tier: PluginTier.Official), HubPreferences.Default).Plugins, p => Assert.Equal(PluginTier.Official, p.Tier));
        Assert.All(client.Search(new CatalogQuery("", Category: "Version control"), HubPreferences.Default).Plugins, p => Assert.Contains("Version control", p.Categories));
        Assert.NotEmpty(client.Search(new CatalogQuery("", CompatibleOnly: true), HubPreferences.Default).Plugins);
    }

    [Fact]
    public void ReportsOpenPrefilledPagesAndNeverSendAnything()
    {
        var subject = new ReportSubject("lumen.todo", "1.0.0", "https://github.com/lumen-labs/lumen-todo", "0.1.0", "Linux");

        Assert.Equal("https://github.com/RunesmithHub/registry/security/advisories/new", ReportLinks.Security().AbsoluteUri);
        var broken = ReportLinks.Broken(subject, "[12:00] Lumen Todo: failed")!.AbsoluteUri;
        Assert.StartsWith("https://github.com/lumen-labs/lumen-todo/issues/new?title=Broken%3A%20lumen.todo%201.0.0&body=", broken, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("Runesmith version: 0.1.0"), broken, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("[12:00] Lumen Todo: failed"), broken, StringComparison.Ordinal);
        Assert.Null(ReportLinks.Broken(subject with { Repository = "https://example.com/x" }));
        Assert.StartsWith("https://github.com/RunesmithHub/registry/issues/new?template=policy.yml&title=Policy%3A%20lumen.todo", ReportLinks.Policy(subject).AbsoluteUri, StringComparison.Ordinal);
        Assert.StartsWith("mailto:security@runesmith.dev?subject=", ReportLinks.SecurityByEmail(subject).OriginalString, StringComparison.Ordinal);
    }
}
