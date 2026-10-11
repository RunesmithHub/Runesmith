using Runesmith.Composition;
using Runesmith.Hub;
using Runesmith.Hub.State;
using Runesmith.Localization;
using RunesmithHub.Protocol.Index;
using PluginState = Runesmith.Composition.PluginState;

namespace Runesmith.Shell.Localization;

/// <summary>A language pack to show text from.</summary>
/// <param name="AssemblyPath">The assembly that embeds its translations.</param>
internal sealed record InstalledLanguagePack(LanguagePack Pack, string Version, string AssemblyPath)
{
    /// <summary>Gets what tells one install of the pack from another: its version, its file and where it comes from.</summary>
    public string Key
    {
        get
        {
            var file = new FileInfo(AssemblyPath);
            return string.Join('|', Pack.Id, Pack.Language, Version, AssemblyPath, Pack.IsOfficial, Pack.Updated.UtcTicks, file.Exists ? file.Length : -1,
                file.Exists ? file.LastWriteTimeUtc.Ticks : 0);
        }
    }
}

/// <summary>Finds the language packs that are installed and turned on: at start among the plugins Runesmith found, and later in the plugin
/// folders again, so a pack that is installed, removed or turned on or off counts at once. A language pack runs no code, so it needs no
/// restart.</summary>
/// <remarks>A pack the hub installed waits in the hub's staging folder until the next start, and is read from there; one the hub removed
/// no longer counts. A pack is official when the hub installed it with the official tier.</remarks>
internal sealed class InstalledLanguagePacks(
    IReadOnlyList<PluginInfo> atStart, IReadOnlyList<(string Path, PluginSource Source)> folders, HubPaths paths, Func<HubState> hubState,
    Func<IReadOnlySet<string>> disabled, IReadOnlyDictionary<string, string> withheld, Func<bool> allowLocalOverrides)
{
    private (HubState State, string Off, IReadOnlyList<InstalledLanguagePack> Packs)? last;

    /// <summary>Gets the packs among the plugins found at start.</summary>
    public IReadOnlyList<InstalledLanguagePack> AtStart() => Packs(atStart, hubState());

    /// <summary>Looks in the plugin folders again, when what the hub installed or what is turned off changed since the last look.</summary>
    public IReadOnlyList<InstalledLanguagePack> Find()
    {
        if (folders.Count == 0)
            return [];

        var state = hubState();
        var pending = state.Pending;
        var off = disabled().Concat(pending?.Removed ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var offKey = string.Join(';', off.Order(StringComparer.OrdinalIgnoreCase));
        if (last is { } found && ReferenceEquals(found.State, state) && found.Off == offKey)
            return found.Packs;

        var staged = (pending?.Installed ?? []).Select(folder => folder.Transaction).Distinct(StringComparer.Ordinal)
            .Select(transaction => (Path.Combine(paths.Staging, transaction), PluginSource.Hub));
        var plugins = PluginDiscovery.Discover([.. staged, .. folders], off, state.Plugins.Select(p => p.Id).ToHashSet(StringComparer.Ordinal), withheld, allowLocalOverrides());
        var packs = Packs(plugins, state);
        last = (state, offKey, packs);
        return packs;
    }

    private static List<InstalledLanguagePack> Packs(IEnumerable<PluginInfo> plugins, HubState state) =>
    [
        .. plugins.Where(plugin => plugin.State == PluginState.Loaded && plugin.Manifest.LanguagePack is not null).Select(plugin =>
        {
            var declared = plugin.Manifest.LanguagePack!;
            var hub = plugin.Source == PluginSource.Hub && !plugin.IsLocalCopy ? state.Find(plugin.Manifest.Id) : null;
            var pack = new LanguagePack(plugin.Manifest.Id, declared.Language, declared.Name, declared.EnglishName)
            {
                IsOfficial = string.Equals(hub?.Tier, nameof(PluginTier.Official), StringComparison.OrdinalIgnoreCase),
                Updated = hub?.InstalledAt ?? Written(plugin.AssemblyPath),
            };
            return new InstalledLanguagePack(pack, plugin.Manifest.Version, plugin.AssemblyPath);
        }),
    ];

    private static DateTimeOffset Written(string path)
    {
        try
        {
            return new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return DateTimeOffset.MinValue;
        }
    }
}
