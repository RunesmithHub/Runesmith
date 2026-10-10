using Runesmith.Composition;
using Runesmith.Hub;
using Runesmith.Hub.Enforcement;
using Runesmith.Sdk.Shell;
using Runesmith.Shell.Hub;
using Runesmith.Tests.Hub;
using PluginState = Runesmith.Composition.PluginState;

namespace Runesmith.Shell.Tests.Hub;

public sealed class LocalCopyTests : IDisposable
{
    private readonly HubFixture fixture = new();

    public void Dispose() => fixture.Dispose();

    [Fact]
    public void ARunningLocalCopySaysItReplacesTheInstalledPluginAndComesFirst()
    {
        var rows = Installed(
            Plugin("runesmith.csharp", "C#", "0.1.0", PluginSource.Bundled),
            Plugin("runesmith.git", "Git", "0.3.0", PluginSource.Bundled) with { State = PluginState.Replaced, Error = "Version 0.3.0 from your plugins folder runs instead." },
            Plugin("runesmith.git", "Git", "0.3.0", PluginSource.Local) with { IsLocalCopy = true });

        Assert.Equal(["Git", "C#"], rows.Select(r => r.Name));
        var row = rows[0];
        Assert.Equal((InstalledSource.Local, LocalCopyState.Replacing, true, true, false), (row.Source, row.LocalCopy, row.IsEnabled, row.CanToggle, row.IsProblem));
        Assert.Null(row.Tier);
        Assert.Equal("Local copy, running in place of version 0.3.0 that comes with Runesmith. It has its own secrets and storage, so sign in to it again.", row.Note);
    }

    [Fact]
    public void ARefusedLocalCopySaysWhyAndCannotBeTurnedOn()
    {
        var rows = Installed(
            Plugin("runesmith.git", "Git", "0.3.0", PluginSource.Bundled),
            Plugin("runesmith.git", "Git", "0.4.0", PluginSource.Local) with { IsLocalCopy = true, State = PluginState.Refused, Error = "Turn on plugins.allowLocalOverrides." });

        var refused = rows.Single(r => r.LocalCopy is not null);
        Assert.Equal((LocalCopyState.Refused, "Turn on plugins.allowLocalOverrides.", true, false, false), (refused.LocalCopy, refused.Note, refused.IsProblem, refused.IsEnabled, refused.CanToggle));
        var official = rows.Single(r => r.LocalCopy is null);
        Assert.Equal((InstalledSource.Bundled, "Comes with Runesmith.", true), (official.Source, official.Note, official.IsEnabled));
        Assert.Equal("0.3.0", Model(Plugin("runesmith.git", "Git", "0.4.0", PluginSource.Local) with { IsLocalCopy = true, State = PluginState.Refused },
            Plugin("runesmith.git", "Git", "0.3.0", PluginSource.Bundled)).RunningVersion("runesmith.git"));
    }

    [Fact]
    public void AnOrdinaryLocalPluginHasNoLocalCopyBadge()
    {
        var row = Assert.Single(Installed(Plugin("acme.scratch", "Scratchpad", "0.0.1", PluginSource.Local)));

        Assert.Null(row.LocalCopy);
        Assert.Equal("From your plugins folder. Never updated or reported to the hub.", row.Note);
        Assert.Empty(PluginManagerModel.LocalCopyNotices([Plugin("acme.scratch", "Scratchpad", "0.0.1", PluginSource.Local)]));
    }

    [Fact]
    public void StartTellsAboutRunningAndRefusedLocalCopies()
    {
        var notices = PluginManagerModel.LocalCopyNotices(
        [
            Plugin("runesmith.git", "Git", "0.3.0", PluginSource.Bundled) with { State = PluginState.Replaced },
            Plugin("runesmith.git", "Git", "0.3.0", PluginSource.Local) with { IsLocalCopy = true },
            Plugin("runesmith.java", "Java", "0.2.0", PluginSource.Bundled),
            Plugin("runesmith.java", "Java", "0.2.0", PluginSource.Local) with { IsLocalCopy = true, State = PluginState.Refused },
        ]);

        Assert.Equal(2, notices.Count);
        Assert.Equal((NotificationKind.Warning, "A local copy of Git is running"), (notices[0].Kind, notices[0].Title));
        Assert.Equal(
            "Git from your plugins folder runs in place of the installed plugin, with its own secrets and storage. Turn off plugins.allowLocalOverrides in Settings to stop this.",
            notices[0].Message);
        Assert.Equal((NotificationKind.Warning, "A local copy of Java was not loaded"), (notices[1].Kind, notices[1].Title));
    }

    [Fact]
    public void SeveralRunningLocalCopiesShareOneNotice()
    {
        var notice = Assert.Single(PluginManagerModel.LocalCopyNotices(
        [
            Plugin("runesmith.git", "Git", "0.3.0", PluginSource.Local) with { IsLocalCopy = true },
            Plugin("runesmith.java", "Java", "0.2.0", PluginSource.Local) with { IsLocalCopy = true },
        ]));

        Assert.Equal("2 local copies of plugins are running", notice.Title);
        Assert.StartsWith("Git and Java from your plugins folder run in place of the installed plugins, with their own", notice.Message, StringComparison.Ordinal);
    }

    private IReadOnlyList<InstalledRow> Installed(params PluginInfo[] plugins) => Model(plugins).Installed();

    private PluginManagerModel Model(params PluginInfo[] plugins) =>
        new(fixture.CreateClient(), () => HubPreferences.Default, () => plugins, () => new HashSet<string>(), () => HubStartupReport.Empty, isSafeMode: false);

    private static PluginInfo Plugin(string id, string name, string version, PluginSource source) =>
        new(new PluginManifest(id, name, version) { SchemaVersion = 1, Capabilities = [] }, Path.Combine(Path.GetTempPath(), id), source);
}
