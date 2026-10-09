using Runesmith.Hub.Enforcement;
using Runesmith.Tests.Hub;

namespace Runesmith.Hub.Tests;

public sealed class KillSwitchTests : IDisposable
{
    private readonly HubFixture fixture = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => fixture.Dispose();

    [Fact]
    public async Task ABlockedPluginStopsAtTheNextStartGoesToQuarantineAndTakesItsDependentsWithIt()
    {
        InstalledPlugins.Write(fixture, ("harbor.issues", "1.3.1", "verified", false), ("lumen.todo", "1.0.0", "unverified", true), ("runesmith.git", "0.3.4", "official", false));
        var running = fixture.CreateClient();
        await running.RefreshAsync(Token);

        Assert.Equal(["harbor.issues"], running.Blocked().Select(p => p.Id));
        var blocked = Assert.Single(running.Notices(new Dictionary<string, IReadOnlyList<string>>(), HubPreferences.Default), n => n.IsDanger);
        Assert.Equal(NoticeKind.Blocked, blocked.Kind);
        Assert.Equal(["Lumen Todo"], blocked.Dependents);

        var report = HubStartup.Run(fixture.Configuration, fixture.Paths, hubEnabled: true, fixture.Time);

        Assert.Equal(["harbor.issues"], report.Quarantined);
        Assert.False(Directory.Exists(fixture.Paths.PluginFolder("harbor.issues")));
        Assert.Single(Directory.EnumerateDirectories(fixture.Paths.Quarantine), folder => Path.GetFileName(folder).StartsWith("harbor.issues-1.3.1-", StringComparison.Ordinal));
        Assert.Equal("It needs Harbor Issues, which Runesmith turned off because the hub blocked it.", report.Withheld["lumen.todo"]);
        Assert.False(report.Withheld.ContainsKey("runesmith.git"));

        var client = fixture.CreateClient();
        await client.RefreshAsync(Token);
        var notice = Assert.Single(client.Notices(new Dictionary<string, IReadOnlyList<string>>(), HubPreferences.Default), n => n.IsDanger);
        Assert.Equal(NoticeKind.Quarantined, notice.Kind);
        Assert.Equal("Harbor Issues was disabled because Runesmith found it harmful.", notice.Title);
        Assert.Equal("Reason: Vulnerability. Depended on a compromised version of a logging library. What to do: Update to 1.3.2.", notice.Message);
        Assert.Equal("1.3.2", notice.UpdateTo?.ToString());
        Assert.Equal(["lumen.todo"], notice.DependentIds);
    }

    [Fact]
    public async Task ABlockStaysWhenRunesmithIsOffline()
    {
        InstalledPlugins.Write(fixture, ("harbor.issues", "1.3.1", "verified", false));
        await fixture.CreateClient().RefreshAsync(Token);
        fixture.Time.Now = fixture.Time.Now.AddDays(30);

        var report = HubStartup.Run(fixture.Configuration, fixture.Paths, hubEnabled: true, fixture.Time);

        Assert.Equal(["harbor.issues"], report.Quarantined);
    }

    [Fact]
    public async Task QuarantinedFilesGoAfterThirtyDaysAndWhenTheUserDismissesThem()
    {
        InstalledPlugins.Write(fixture, ("harbor.issues", "1.3.1", "verified", false));
        await fixture.CreateClient().RefreshAsync(Token);
        HubStartup.Run(fixture.Configuration, fixture.Paths, hubEnabled: true, fixture.Time);
        Assert.Single(Directory.EnumerateDirectories(fixture.Paths.Quarantine));

        fixture.Time.Now = fixture.Time.Now.AddDays(29);
        HubStartup.Run(fixture.Configuration, fixture.Paths, hubEnabled: true, fixture.Time);
        Assert.Single(Directory.EnumerateDirectories(fixture.Paths.Quarantine));

        fixture.Time.Now = fixture.Time.Now.AddDays(2);
        HubStartup.Run(fixture.Configuration, fixture.Paths, hubEnabled: true, fixture.Time);
        Assert.Empty(Directory.EnumerateDirectories(fixture.Paths.Quarantine));

        var client = fixture.CreateClient();
        Assert.Single(client.State.Quarantined);
        client.Dismiss("harbor.issues");
        Assert.Empty(fixture.CreateClient().State.Quarantined);
    }

    [Fact]
    public async Task AFrozenPluginKeepsRunningWithANotice()
    {
        InstalledPlugins.Write(fixture, ("pixel.icons", "2.1.3", "unverified", true), ("copper.snippets", "1.2.0", "unverified", true));
        var client = fixture.CreateClient();
        await client.RefreshAsync(Token);

        var notices = client.Notices(new Dictionary<string, IReadOnlyList<string>>(), HubPreferences.Default);
        var report = HubStartup.Run(fixture.Configuration, fixture.Paths, hubEnabled: true, fixture.Time);

        var frozen = Assert.Single(notices, n => n.Kind == NoticeKind.Frozen);
        Assert.Equal("New installs of Pixel File Icons are paused while Runesmith looks into a report. You can keep using it.", frozen.Title);
        Assert.False(frozen.IsDanger);
        var deprecated = Assert.Single(notices, n => n.Kind == NoticeKind.Deprecated);
        Assert.Equal("Copper Snippets is no longer maintained.", deprecated.Title);
        Assert.Empty(report.Withheld);
        Assert.Empty(report.Quarantined);
        Assert.DoesNotContain(client.Updates([], HubPreferences.Default), u => u.PluginId == "pixel.icons");
    }

    [Fact]
    public async Task ABlockedPublisherHidesItsPluginsAndTheirPagesOfferNoInstall()
    {
        var client = fixture.CreateClient();
        await client.RefreshAsync(Token);

        var result = client.Resolve(RunesmithHub.Protocol.Resolution.ResolutionRequest.Install("fastcode.helper"), HubPreferences.Default);

        Assert.False(result.Succeeded);
        Assert.DoesNotContain(client.Search(new Catalog.CatalogQuery(""), HubPreferences.Default).Plugins, p => p.Id == "fastcode.helper");
    }
}
