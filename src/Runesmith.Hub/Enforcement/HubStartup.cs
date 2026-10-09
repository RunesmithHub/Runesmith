using System.Globalization;
using Runesmith.Hub.Installing;
using Runesmith.Hub.State;
using RunesmithHub.Protocol.Catalog;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Updating;

namespace Runesmith.Hub.Enforcement;

/// <summary>What the hub decided before plugins load.</summary>
/// <param name="HubPlugins">The ids of the plugins the hub installed, which load as hub plugins.</param>
/// <param name="Withheld">The plugins that must not load, with why.</param>
/// <param name="ChangedFiles">The hub plugins whose files changed since they were installed, with what changed; they are withheld too.</param>
/// <param name="Quarantined">The ids of the plugins moved to quarantine at this start.</param>
/// <param name="Problems">What went wrong, for the log.</param>
public sealed record HubStartupReport(
    IReadOnlySet<string> HubPlugins,
    IReadOnlyDictionary<string, string> Withheld,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ChangedFiles,
    IReadOnlyList<string> Quarantined,
    IReadOnlyList<string> Problems)
{
    public static HubStartupReport Empty { get; } = new(new HashSet<string>(), new Dictionary<string, string>(), new Dictionary<string, IReadOnlyList<string>>(), [], []);
}

/// <summary>Prepares the user's hub plugins before they load: puts committed changes into effect, moves blocked plugins to quarantine, checks
/// every hub plugin's files, and works out which plugins must stay off and why.</summary>
public static class HubStartup
{
    /// <summary>How long a quarantined plugin's files are kept.</summary>
    public static readonly TimeSpan QuarantineAge = TimeSpan.FromDays(30);

    private const string StampFormat = "yyyyMMddHHmmss";

    /// <param name="hubEnabled">Whether the user has the hub on; when it is off, no hub plugin loads.</param>
    public static HubStartupReport Run(HubConfiguration configuration, HubPaths paths, bool hubEnabled, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(time);
        var problems = new List<string>();
        var store = new HubStateStore(paths.StateFile);
        var state = store.Load();
        if (store.LoadError is { } loadError)
            problems.Add($"The installed state could not be read: {loadError}");
        var hadPending = state.Pending is not null;
        state = PendingChanges.Apply(paths, state, problems);
        var dirty = hadPending;
        CleanQuarantine(paths, time.GetUtcNow());

        var withheld = new Dictionary<string, string>(StringComparer.Ordinal);
        var changed = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var quarantined = new List<string>();
        var catalog = SavedCatalog(configuration, paths, time);
        if (catalog is not null)
            state = QuarantineBlocked(paths, state, catalog, quarantined, problems, time.GetUtcNow());
        dirty |= quarantined.Count > 0;

        var checkedPlugins = new List<InstalledHubPlugin>();
        foreach (var plugin in state.Plugins)
        {
            if (state.IsPending(plugin.Id))
            {
                checkedPlugins.Add(plugin);
                continue;
            }

            var result = PluginFiles.Check(paths.PluginFolder(plugin.Id), plugin.Files);
            if (!result.IsIntact)
            {
                changed[plugin.Id] = result.Problems;
                withheld[plugin.Id] = "Its files changed since it was installed, so Runesmith turned it off. Reinstall it from the plugin manager.";
            }

            var refreshed = result.IsIntact && !result.Files.SequenceEqual(plugin.Files);
            dirty |= refreshed;
            checkedPlugins.Add(refreshed ? plugin with { Files = result.Files } : plugin);
        }

        state = state with { Plugins = checkedPlugins };
        if (catalog is not null)
            WithholdDependents(catalog, state, changed.Keys, withheld);

        if (!hubEnabled)
        {
            foreach (var plugin in state.Plugins)
                withheld.TryAdd(plugin.Id, "The plugin hub is turned off in Settings, so plugins from the hub do not load.");
        }

        if (dirty)
        {
            try
            {
                store.Save(state);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                problems.Add($"The installed state could not be saved: {exception.Message}");
            }
        }

        return new HubStartupReport(state.Plugins.Select(p => p.Id).ToHashSet(StringComparer.Ordinal), withheld, changed, quarantined, problems);
    }

    private static HubCatalog? SavedCatalog(HubConfiguration configuration, HubPaths paths, TimeProvider time)
    {
        if (configuration.TrustRoot is not { } root)
            return null;

        try
        {
            var updater = new IndexUpdater(new DirectoryIndexFetcher(paths.TrustStore), new FileTrustStore(paths.TrustStore), root, time);
            return updater.LoadSavedView() is { } view ? new HubCatalog(view) : null;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static HubState QuarantineBlocked(HubPaths paths, HubState state, HubCatalog catalog, List<string> quarantined, List<string> problems, DateTimeOffset now)
    {
        var blocked = state.Plugins.Where(plugin => !state.IsPending(plugin.Id) && Moderation.StateOf(catalog, plugin)?.State == VersionState.Blocked).ToList();
        if (blocked.Count == 0)
            return state;

        var entries = state.Quarantined.ToList();
        var removed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var plugin in blocked)
        {
            var effective = Moderation.StateOf(catalog, plugin)!;
            var folder = Path.Combine(paths.Quarantine, $"{plugin.Id}-{plugin.Version}-{now.ToString(StampFormat, CultureInfo.InvariantCulture)}");
            try
            {
                if (Directory.Exists(paths.PluginFolder(plugin.Id)))
                    Folders.Move(paths.PluginFolder(plugin.Id), folder);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{plugin.Id} {plugin.Version} is blocked but could not be moved to quarantine: {exception.Message}");
                continue;
            }

            var moderation = effective.Moderation;
            var dependents = Moderation.Dependents(catalog, state.Plugins.Where(p => p.Id != plugin.Id), [plugin.Id]).Select(p => p.Id).ToList();
            entries.RemoveAll(entry => entry.Id == plugin.Id);
            entries.Add(new QuarantinedPlugin(plugin.Id, catalog.FindPlugin(plugin.Id)?.Name ?? plugin.Id, plugin.Version, folder, now, moderation?.Reason.ToString(), moderation?.Note)
            {
                Advisory = moderation?.Advisory,
                Remediation = moderation?.Remediation,
                Dependents = dependents,
            });
            removed.Add(plugin.Id);
            quarantined.Add(plugin.Id);
        }

        return state with { Plugins = [.. state.Plugins.Where(p => !removed.Contains(p.Id))], Quarantined = entries };
    }

    private static void WithholdDependents(HubCatalog catalog, HubState state, IEnumerable<string> changed, Dictionary<string, string> withheld)
    {
        foreach (var entry in state.Quarantined)
        {
            if (state.Find(entry.Id) is not null)
                continue;
            foreach (var dependent in Moderation.Dependents(catalog, state.Plugins, [entry.Id]))
                withheld.TryAdd(dependent.Id, $"It needs {entry.Name}, which Runesmith turned off because the hub blocked it.");
        }

        foreach (var id in changed.ToList())
        {
            var name = catalog.FindPlugin(id)?.Name ?? id;
            foreach (var dependent in Moderation.Dependents(catalog, state.Plugins, [id]))
                withheld.TryAdd(dependent.Id, $"It needs {name}, whose files changed since it was installed.");
        }
    }

    private static void CleanQuarantine(HubPaths paths, DateTimeOffset now)
    {
        if (!Directory.Exists(paths.Quarantine))
            return;

        foreach (var folder in Directory.EnumerateDirectories(paths.Quarantine))
        {
            var name = Path.GetFileName(folder);
            var stamp = name.Length > StampFormat.Length + 1 ? name[^StampFormat.Length..] : "";
            var at = DateTimeOffset.TryParseExact(stamp, StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed
                : new DateTimeOffset(Directory.GetLastWriteTimeUtc(folder), TimeSpan.Zero);
            if (now - at >= QuarantineAge)
                Folders.TryDelete(folder);
        }
    }
}
