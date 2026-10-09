using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Runesmith.Composition;
using Runesmith.Hub;
using Runesmith.Hub.Enforcement;
using Runesmith.Shell.Hub;
using Runesmith.Tests.Hub;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Resolution;
using PluginState = Runesmith.Composition.PluginState;

namespace Runesmith.Shell.Tests.Hub;

public sealed class InstallPlanModelTests : IDisposable
{
    private readonly HubFixture fixture = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => fixture.Dispose();

    [Fact]
    public async Task InstallingAnUnverifiedPluginWaitsForItsAcknowledgement()
    {
        var model = await PlanAsync("lumen.todo", bundled: [Bundled("runesmith.git", "0.1.0", "process", "network")]);

        Assert.Equal("Install Lumen Todo", model.Title);
        Assert.Equal("This changes 3 plugins.", model.Summary);
        var warning = Assert.Single(model.Warnings);
        Assert.Equal(
            "Lumen Todo hasn't been reviewed by Runesmith. Its code was checked automatically, but no person has read it. Plugins can access everything you can: your files, your projects and your accounts. Install it only if you trust Lumen Labs.",
            warning.Text);
        Assert.Equal("I understand that Lumen Todo is unverified and I trust its publisher.", warning.Acknowledgement);
        Assert.False(warning.IsAcknowledged);
        Assert.False(model.CanInstall);

        warning.IsAcknowledged = true;

        Assert.True(model.CanInstall);
    }

    [Fact]
    public async Task ThePlanSaysWhatChangesWhyAndWhatItMayDo()
    {
        var model = await PlanAsync("lumen.todo", bundled: [Bundled("runesmith.git", "0.1.0", "process", "network")]);

        var harbor = model.Rows.Single(r => r.Id == "harbor.issues");
        Assert.Equal((PluginTier.Verified, "1.3.2"), (harbor.Tier!.Value, harbor.Change));
        Assert.StartsWith("Install, needed by Lumen Todo · ", harbor.What, StringComparison.Ordinal);
        var git = model.Rows.Single(r => r.Id == "runesmith.git");
        Assert.Equal("0.1.0 to 0.3.4", git.Change);
        Assert.StartsWith("Update, Lumen Todo needs >=0.3.1 <0.4.0", git.What, StringComparison.Ordinal);

        Assert.Equal([("network", true, false), ("credentials", true, true), ("process", false, true)],
            model.Capabilities.Select(c => (c.Id, c.IsNew, c.IsHighRisk)));
        Assert.Equal("Harbor Issues", model.Capabilities[0].From);
    }

    [Fact]
    public async Task AVerifiedPluginNeedsNoAcknowledgementAndSaysWhenItWasReviewed()
    {
        var model = await PlanAsync("tide.formatter");

        Assert.Empty(model.Warnings);
        Assert.True(model.CanInstall);
        Assert.StartsWith("Reviewed by Runesmith on ", model.Review, StringComparison.Ordinal);
    }

    [Fact]
    public Task TheInstallButtonIsNeverTheDefaultAndStaysOffUntilEveryWarningIsAcknowledged() => HeadlessSession.Value.Dispatch(async () =>
    {
        var model = await PlanAsync("lumen.todo");
        var dialog = InstallDialogs.PlanDialog(model, fixture.CreateClient(), out var install);
        var window = new Window { Content = dialog, Width = 800, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.False(install.IsDefault);
        Assert.False(install.IsEnabled);
        var acknowledge = dialog.GetLogicalDescendants().OfType<CheckBox>().Single();
        Assert.False(acknowledge.IsChecked);

        acknowledge.IsChecked = true;
        Assert.True(install.IsEnabled);
        acknowledge.IsChecked = false;
        Assert.False(install.IsEnabled);
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task APluginPageOpenedByALinkOffersInstallButNeverOnEnter() => HeadlessSession.Value.Dispatch(async () =>
    {
        var client = fixture.CreateClient();
        await client.RefreshAsync(Token);
        var model = new PluginManagerModel(client, () => HubPreferences.Default, () => [], () => new HashSet<string>(), () => HubStartupReport.Empty, isSafeMode: false);
        var target = new PageTarget("lumen.todo", null, "runesmith://hub/plugin/lumen.todo");
        var page = model.Describe(target);

        Assert.True(page.CanInstall);
        Assert.Contains(page.Notices, n => n.Title == "Unverified.");
        var clicks = 0;
        var install = PluginPageView.InstallButton("Install", DateTimeOffset.UtcNow, () => clicks++);
        var window = new Window { Content = install };
        window.Show();
        install.Focus();
        Press(install, Key.Space);
        Press(install, Key.Enter);
        Assert.Equal(0, clicks);

        var later = PluginPageView.InstallButton("Install", DateTimeOffset.UtcNow - PluginPageView.KeyGrace, () => clicks++);
        window.Content = later;
        later.Focus();
        Press(later, Key.Enter);
        Assert.Equal(0, clicks);
        Press(later, Key.Space);
        Assert.Equal(1, clicks);
        Assert.False(later.IsDefault);
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public async Task PagesExplainWhyTheyOfferNoInstall()
    {
        var client = fixture.CreateClient();
        await client.RefreshAsync(Token);
        var model = new PluginManagerModel(client, () => HubPreferences.Default with { AllowedTiers = RunesmithHub.Protocol.Catalog.AllowedTiers.OfficialAndVerified },
            () => [], () => new HashSet<string>(), () => HubStartupReport.Empty, isSafeMode: false);

        var unknown = model.Describe(new PageTarget("nobody.here"));
        var frozen = model.Describe(new PageTarget("pixel.icons"));
        var blocked = model.Describe(new PageTarget("fastcode.helper"));
        var excluded = model.Describe(new PageTarget("lumen.todo"));

        Assert.False(unknown.CanInstall);
        Assert.Equal("No plugin with the id nobody.here is on the hub.", unknown.Notices[0].Title);
        Assert.False(frozen.CanInstall);
        Assert.Equal(PageNoticeKind.Info, frozen.Notices[0].Kind);
        Assert.False(blocked.CanInstall);
        Assert.Equal(PageNoticeKind.Danger, blocked.Notices[0].Kind);
        Assert.False(excluded.CanInstall);
        Assert.Equal("Your settings allow only official and verified plugins.", excluded.Notices.Single(n => n.Kind == PageNoticeKind.Warning).Message);
    }

    [Fact]
    public async Task InstalledPluginsShowProblemsFirst()
    {
        var client = fixture.CreateClient();
        await client.RefreshAsync(Token);
        PluginInfo[] plugins =
        [
            Plugin("runesmith.git", "Git", "0.1.0", PluginSource.Bundled),
            Plugin("acme.scratch", "Scratchpad", "0.0.1", PluginSource.Local),
            Plugin("acme.broken", "Broken", "1.0.0", PluginSource.Local) with { State = PluginState.Failed, Error = "The assembly could not be loaded." },
        ];
        var model = new PluginManagerModel(client, () => HubPreferences.Default, () => plugins, () => new HashSet<string>(), () => HubStartupReport.Empty, isSafeMode: false);

        var rows = model.Installed();

        Assert.Equal(["Broken", "Git", "Scratchpad"], rows.Select(r => r.Name));
        Assert.True(rows[0].IsProblem);
        Assert.Equal((InstalledSource.Bundled, PluginTier.Official), (rows[1].Source, rows[1].Tier!.Value));
        Assert.Equal(InstalledSource.Local, rows[2].Source);
        Assert.Null(rows[2].Tier);
    }

    private async Task<InstallPlanModel> PlanAsync(string id, IReadOnlyList<PluginInfo>? bundled = null)
    {
        var client = fixture.CreateClient();
        await client.RefreshAsync(Token);
        var model = new PluginManagerModel(client, () => HubPreferences.Default, () => bundled ?? [], () => new HashSet<string>(), () => HubStartupReport.Empty, isSafeMode: false);
        return model.Plan(client.Resolve(ResolutionRequest.Install(id), HubPreferences.Default).Plan!);
    }

    private static void Press(Button button, Key key)
    {
        button.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = button });
        button.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key, Source = button });
        Dispatcher.UIThread.RunJobs();
    }

    private static PluginInfo Bundled(string id, string version, params string[] capabilities) =>
        Plugin(id, id, version, PluginSource.Bundled) with
        {
            Manifest = new PluginManifest(id, id, version) { SchemaVersion = 1, Capabilities = [.. capabilities.Select(c => new DeclaredCapability { Id = c, Reason = "For the tests." })] },
        };

    private static PluginInfo Plugin(string id, string name, string version, PluginSource source) =>
        new(new PluginManifest(id, name, version) { SchemaVersion = 1, Capabilities = [] }, Path.Combine(Path.GetTempPath(), id), source);
}
