using Runesmith.Composition;
using Runesmith.Hub;
using Runesmith.Hub.Catalog;
using Runesmith.Hub.Enforcement;
using Runesmith.Hub.State;
using Runesmith.Hub.Updates;
using Runesmith.Sdk.Shell;
using Runesmith.Workspace.Settings;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Resolution;
using RunesmithHub.Protocol.Versioning;
using PluginState = Runesmith.Composition.PluginState;

namespace Runesmith.Shell.Hub;

/// <summary>The sections of the plugin manager.</summary>
public enum PluginManagerTab
{
    Installed,
    Updates,
    Browse,
}

/// <summary>Where a plugin comes from, as the plugin manager shows it.</summary>
public enum InstalledSource
{
    Bundled,
    Hub,

    /// <summary>A hub plugin that runs in place of the bundled copy.</summary>
    BundledFromHub,
    Local,
}

/// <summary>What became of a copy in the user's plugins folder of a plugin Runesmith ships or the hub installed.</summary>
public enum LocalCopyState
{
    /// <summary>It runs in place of that plugin, with its own secrets and storage.</summary>
    Replacing,

    /// <summary>It does not load, because the user has not let local copies replace plugins.</summary>
    Refused,
}

/// <summary>A plugin page the plugin manager shows.</summary>
/// <param name="Version">The version to select, or null for the newest that can be installed.</param>
/// <param name="FromLink">The link that opened the page, or null.</param>
/// <param name="IgnoredLinks">How many links that came just before this one were ignored.</param>
public sealed record PageTarget(string PluginId, SemanticVersion? Version = null, string? FromLink = null, int IgnoredLinks = 0);

/// <summary>One row of the Installed section.</summary>
/// <param name="Note">What the user should know: where it comes from, or why it is off.</param>
/// <param name="IsProblem">Whether it does not run as it should, which sorts it first.</param>
/// <param name="CanToggle">Whether the user can turn it on or off here; a blocked plugin cannot be turned back on.</param>
public sealed record InstalledRow(
    string Id, string Name, string Version, PluginTier? Tier, InstalledSource Source, string Note, bool IsProblem, bool IsEnabled, bool CanToggle)
{
    /// <summary>Gets the state badge to show beside the name, if any.</summary>
    public NoticeKind? Badge { get; init; }

    /// <summary>Gets what became of the plugin when it is a local copy of a plugin Runesmith ships or the hub installed, or null.</summary>
    public LocalCopyState? LocalCopy { get; init; }

    /// <summary>Gets the plugin's folder, for its icon.</summary>
    public string? Folder { get; init; }
}

/// <summary>What the plugin manager shows: the installed plugins with their notices, the updates, the catalog and plugin pages. It holds the
/// navigation, so the welcome screen and the Plugins tool window show the same place.</summary>
public sealed class PluginManagerModel
{
    private readonly HubClient client;
    private readonly Func<HubPreferences> preferences;
    private readonly Func<IReadOnlyList<PluginInfo>> plugins;
    private readonly Func<IReadOnlySet<string>> disabled;
    private readonly Func<HubStartupReport> startup;
    private PluginManagerTab tab;
    private PageTarget? page;

    /// <param name="plugins">The plugins Runesmith found at start.</param>
    /// <param name="disabled">The ids of the plugins the user turned off.</param>
    public PluginManagerModel(
        HubClient client, Func<HubPreferences> preferences, Func<IReadOnlyList<PluginInfo>> plugins, Func<IReadOnlySet<string>> disabled, Func<HubStartupReport> startup,
        bool isSafeMode)
    {
        this.client = client;
        this.preferences = preferences;
        this.plugins = plugins;
        this.disabled = disabled;
        this.startup = startup;
        IsSafeMode = isSafeMode;
    }

    /// <summary>Raised, on any thread, when the place to show or the data behind it changes.</summary>
    public event EventHandler? Changed;

    public HubClient Client => client;

    public bool IsSafeMode { get; }

    public HubPreferences Preferences => preferences();

    public PluginManagerTab Tab => tab;

    /// <summary>Gets the plugin page shown over the sections, or null.</summary>
    public PageTarget? Page => page;

    public void ShowTab(PluginManagerTab value)
    {
        tab = value;
        page = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ShowPage(PageTarget target)
    {
        page = target;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Leaves a plugin page for the section it was opened from.</summary>
    public void Back() => ShowTab(tab);

    /// <summary>Tells the views to show the data again.</summary>
    public void Refresh() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Gets every plugin, problems first, then local copies of plugins Runesmith ships or the hub installed.</summary>
    public IReadOnlyList<InstalledRow> Installed()
    {
        var state = client.State;
        var catalog = client.Catalog;
        var report = startup();
        var off = disabled();
        var rows = new List<InstalledRow>();
        var shown = new HashSet<string>(StringComparer.Ordinal);
        var all = plugins();
        foreach (var plugin in all.Where(p => p.State != PluginState.Replaced))
        {
            var id = plugin.Manifest.Id;
            shown.Add(id);
            if (plugin.IsLocalCopy)
            {
                rows.Add(LocalCopyRow(plugin, all, off));
                continue;
            }

            var hub = state.Find(id);
            var replacesBundled = all.Any(p => p.Manifest.Id == id && p.State == PluginState.Replaced && p.Source == PluginSource.Bundled);
            var source = plugin.Source switch
            {
                PluginSource.Bundled => InstalledSource.Bundled,
                PluginSource.Hub => replacesBundled ? InstalledSource.BundledFromHub : InstalledSource.Hub,
                _ => InstalledSource.Local,
            };
            var tier = source is InstalledSource.Hub or InstalledSource.BundledFromHub || source == InstalledSource.Bundled ? catalog?.FindPlugin(id)?.Tier : null;
            if (source == InstalledSource.Bundled && tier is null)
                tier = PluginTier.Official;

            var replacedBy = all.FirstOrDefault(p => p.Manifest.Id == id && p.State == PluginState.Replaced && p.Source is PluginSource.Hub or PluginSource.Local);
            var (note, problem) = Note(plugin, hub, state, source, replacesBundled, replacedBy);
            rows.Add(new InstalledRow(id, plugin.Manifest.Name, plugin.Manifest.Version, source == InstalledSource.Local ? null : tier, source, note, problem,
                !off.Contains(id) && !report.Withheld.ContainsKey(id), CanToggle: !report.Withheld.ContainsKey(id) || plugin.Source == PluginSource.Bundled)
            {
                Badge = report.ChangedFiles.ContainsKey(id) ? NoticeKind.FilesChanged : catalog is not null && hub is not null ? Badge(catalog, hub) : null,
                Folder = plugin.Directory,
            });
        }

        foreach (var hub in state.Plugins.Where(p => !shown.Contains(p.Id)))
        {
            var name = catalog?.FindPlugin(hub.Id)?.Name ?? hub.Id;
            var note = IsSafeMode && !state.IsPending(hub.Id) ? "Off in safe mode, which loads only the plugins that come with Runesmith." : "Installed. Restart Runesmith to start it.";
            rows.Add(new InstalledRow(hub.Id, name, hub.Version, Enum.TryParse<PluginTier>(hub.Tier, true, out var tier) ? tier : null, InstalledSource.Hub, note,
                IsProblem: false, IsEnabled: !off.Contains(hub.Id), CanToggle: true)
            {
                Folder = client.Paths.PluginFolder(hub.Id),
            });
        }

        foreach (var entry in state.Quarantined.Where(q => state.Find(q.Id) is null && !shown.Contains(q.Id)))
        {
            rows.Add(new InstalledRow(entry.Id, entry.Name, entry.Version, catalog?.FindPlugin(entry.Id)?.Tier, InstalledSource.Hub,
                "Moved to quarantine. It can't be turned back on.", IsProblem: true, IsEnabled: false, CanToggle: false)
            { Badge = NoticeKind.Quarantined });
        }

        return [.. rows.OrderByDescending(r => r.IsProblem).ThenByDescending(r => r.LocalCopy is not null).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>Gets the notices about installed plugins, problems first.</summary>
    public IReadOnlyList<PluginNotice> Notices() => client.Notices(startup().ChangedFiles, preferences());

    /// <summary>Gets the available updates.</summary>
    public IReadOnlyList<UpdateCandidate> Updates() => client.Updates(Bundled(), preferences());

    /// <summary>Gets the plugins Runesmith ships, for updates and install plans.</summary>
    public IReadOnlyList<BundledPlugin> Bundled() =>
        [.. plugins().Where(p => p.Source == PluginSource.Bundled).Select(p => new BundledPlugin(p.Manifest.Id, p.Manifest.Version, [.. (p.Manifest.Capabilities ?? []).Select(c => c.Id)]))];

    public CatalogResults Browse(CatalogQuery query) => client.Search(query, preferences());

    /// <summary>Gets the version that runs now of a plugin, from the hub, Runesmith or the user's folder, or null.</summary>
    public string? RunningVersion(string id) =>
        plugins().FirstOrDefault(p => p.Manifest.Id == id && IsRunningCopy(p))?.Manifest.Version;

    /// <summary>Gets the capabilities a plugin has now, for marking the ones an install plan adds; null when it is not installed.</summary>
    public IReadOnlySet<string>? CurrentCapabilities(string id)
    {
        if (client.State.Find(id) is { } hub && client.Catalog is { } catalog && Moderation.Record(catalog, hub) is { } record)
            return record.Capabilities.Declared.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        return plugins().FirstOrDefault(p => p.Manifest.Id == id && IsRunningCopy(p))?.Manifest.Capabilities?.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Gets the model of an install plan's dialog.</summary>
    public InstallPlanModel Plan(InstallPlan plan) => new(plan, client, CurrentCapabilities, BundledVersion);

    /// <summary>Gets what a plugin's page shows.</summary>
    public PluginPageModel Describe(PageTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var running = plugins().FirstOrDefault(p => p.Manifest.Id == target.PluginId && IsRunningCopy(p));
        var problem = running?.State switch
        {
            PluginState.Failed => $"It could not load: {running.Error}",
            PluginState.Disabled => running.Error ?? "It is turned off.",
            _ => null,
        };
        return PluginPageModel.Create(client, target, preferences(), running?.Manifest.Version, plugins().Any(p => p.Manifest.Id == target.PluginId && p.Source == PluginSource.Bundled)) with
        {
            Problem = problem,
        };
    }

    /// <summary>Gets the notices to show once at start about local copies of plugins Runesmith ships or the hub installed: those that run in
    /// their place, and those that were refused.</summary>
    public static IReadOnlyList<(NotificationKind Kind, string Title, string Message)> LocalCopyNotices(IReadOnlyList<PluginInfo> plugins)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        var notices = new List<(NotificationKind, string, string)>();
        var replacing = plugins.Where(p => p.IsLocalCopy && p.State == PluginState.Loaded).Select(p => p.Manifest.Name).ToList();
        if (replacing.Count > 0)
        {
            notices.Add((NotificationKind.Warning,
                replacing.Count == 1 ? $"A local copy of {replacing[0]} is running" : $"{replacing.Count} local copies of plugins are running",
                $"{Names(replacing)} from your plugins folder {(replacing.Count == 1 ? "runs" : "run")} in place of the installed {(replacing.Count == 1 ? "plugin" : "plugins")}, with "
                + $"{(replacing.Count == 1 ? "its" : "their")} own secrets and storage. Turn off {CoreSettings.AllowLocalOverrides} in Settings to stop this."));
        }

        var refused = plugins.Where(p => p.State == PluginState.Refused).Select(p => p.Manifest.Name).Distinct(StringComparer.Ordinal).ToList();
        if (refused.Count > 0)
        {
            notices.Add((NotificationKind.Warning,
                refused.Count == 1 ? $"A local copy of {refused[0]} was not loaded" : $"{refused.Count} local copies of plugins were not loaded",
                $"Your plugins folder has a copy of {Names(refused)}, which Runesmith ships or the hub installed. The plugin manager says how to run it."));
        }

        return notices;
    }

    private static bool IsRunningCopy(PluginInfo plugin) => plugin.State is not (PluginState.Replaced or PluginState.Refused);

    private static InstalledRow LocalCopyRow(PluginInfo plugin, IReadOnlyList<PluginInfo> all, IReadOnlySet<string> off)
    {
        var id = plugin.Manifest.Id;
        if (plugin.State == PluginState.Refused)
        {
            return new InstalledRow(id, plugin.Manifest.Name, plugin.Manifest.Version, null, InstalledSource.Local, plugin.Error ?? "", IsProblem: true, IsEnabled: false, CanToggle: false)
            {
                LocalCopy = LocalCopyState.Refused,
                Folder = plugin.Directory,
            };
        }

        var replaced = all.FirstOrDefault(p => p.Manifest.Id == id && p.State == PluginState.Replaced && p.Source == PluginSource.Hub)
            ?? all.FirstOrDefault(p => p.Manifest.Id == id && p.State == PluginState.Replaced && p.Source == PluginSource.Bundled);
        var instead = replaced is null ? "the installed plugin" : $"version {replaced.Manifest.Version} {(replaced.Source == PluginSource.Hub ? "from the hub" : "that comes with Runesmith")}";
        var (note, problem) = plugin.State switch
        {
            PluginState.Failed => ($"Could not load: {plugin.Error}", true),
            PluginState.Disabled => ("Turned off.", false),
            _ => ($"Local copy, running in place of {instead}. It has its own secrets and storage, so sign in to it again.", false),
        };
        return new InstalledRow(id, plugin.Manifest.Name, plugin.Manifest.Version, null, InstalledSource.Local, note, problem, !off.Contains(id), CanToggle: true)
        {
            LocalCopy = LocalCopyState.Replacing,
            Folder = plugin.Directory,
        };
    }

    private static string Names(List<string> names) => names.Count switch
    {
        1 => names[0],
        _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
    };

    private string? BundledVersion(string id) => plugins().FirstOrDefault(p => p.Manifest.Id == id && p.Source == PluginSource.Bundled)?.Manifest.Version;

    private static NoticeKind? Badge(RunesmithHub.Protocol.Catalog.HubCatalog catalog, InstalledHubPlugin hub) => Moderation.StateOf(catalog, hub)?.State switch
    {
        VersionState.Blocked => NoticeKind.Blocked,
        VersionState.Frozen => NoticeKind.Frozen,
        VersionState.Yanked => NoticeKind.Yanked,
        VersionState.Deleted => NoticeKind.Deleted,
        _ => catalog.FindPlugin(hub.Id)?.State == RunesmithHub.Protocol.Index.PluginState.Deprecated ? NoticeKind.Deprecated : null,
    };

    private static (string Note, bool Problem) Note(PluginInfo plugin, InstalledHubPlugin? hub, HubState state, InstalledSource source, bool replacesBundled, PluginInfo? replacedBy)
    {
        if (plugin.State == PluginState.Failed)
            return ($"Could not load: {plugin.Error}", true);
        if (source != InstalledSource.Local && replacedBy?.Error is { } fallback)
            return (fallback, true);
        if (plugin.State == PluginState.Disabled && plugin.Error is { } reason)
            return (reason, true);
        if (hub is not null && state.IsPending(hub.Id))
            return (hub.Version == plugin.Manifest.Version ? "Restart Runesmith to finish the change." : $"Version {hub.Version} is installed. Restart Runesmith to start it.", false);
        if (source != InstalledSource.Bundled && state.Pending?.Removed.Contains(plugin.Manifest.Id) == true)
            return ("Removed. Restart Runesmith to finish.", false);
        if (plugin.State == PluginState.Disabled)
            return ("Turned off.", false);

        return source switch
        {
            InstalledSource.Bundled => ("Comes with Runesmith.", false),
            InstalledSource.BundledFromHub => (replacesBundled ? "Comes with Runesmith, updated from the hub." : "From the hub.", false),
            InstalledSource.Hub => (hub?.Requested == false ? "From the hub, installed because another plugin needs it." : "From the hub.", false),
            _ => ("From your plugins folder. Never updated or reported to the hub.", false),
        };
    }
}
