using System.Composition;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Settings;
using RunesmithHub.Protocol.Catalog;
using RunesmithHub.Protocol.Index;

namespace Runesmith.Shell.Hub;

/// <summary>An official plugin on the hub that Runesmith suggests for a file or a folder.</summary>
/// <param name="PluginId">The plugin's id on the hub.</param>
/// <param name="Name">The plugin's name, as the suggestion shows it.</param>
/// <param name="Handles">What it was suggested for, such as <c>.toml</c> or <c>Dockerfile</c>.</param>
public sealed record SuggestedPlugin(string PluginId, string Name, string Handles)
{
    /// <summary>Gets what the suggestion says.</summary>
    public string Message => Strings.PluginsSuggestion(Name, Handles);
}

/// <summary>An official plugin the hub offers for this Runesmith, with the keywords of its listing.</summary>
internal sealed record OfficialPlugin(string Id, string Name, IReadOnlyList<string> Keywords);

/// <summary>Suggests installing an official plugin for a file no installed plugin knows, or for what is at the top of the open folder.</summary>
/// <remarks>Runesmith knows no language and no tool by itself, so it has no list of its own to suggest from. A plugin says what it is for in
/// the <c>keywords</c> of its manifest, which the hub's catalog carries: the plugin whose keywords hold a file's extension without the dot,
/// such as <c>toml</c>, or the name of a file or folder that has none, such as <c>dockerfile</c> or <c>git</c>, is the one suggested. Each
/// plugin is suggested once in a session, and never again once the user said not to.</remarks>
[Export]
[Shared]
public sealed class PluginSuggestions
{
    private readonly ISettingsService settings;
    private readonly ILanguageRegistry languages;
    private readonly Func<bool> canInstall;
    private readonly Func<IEnumerable<OfficialPlugin>> official;
    private readonly Func<string, bool> isInstalled;
    private readonly Action<string> showPage;
    private readonly HashSet<string> suggested = new(StringComparer.Ordinal);

    [ImportingConstructor]
    public PluginSuggestions(ISettingsService settings, ILanguageRegistry languages, Lazy<HubService> hub)
        : this(
            settings,
            languages,
            () => hub.Value.Client.Status.HasCatalog,
            () => Official(hub.Value.Client.Catalog, hub.Value.Client.Host),
            id => hub.Value.Model.Installed().Any(row => string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase)),
            id => hub.Value.ShowPage(id))
    {
    }

    /// <param name="canInstall">Whether the hub is set up and turned on.</param>
    /// <param name="official">Gets the official plugins the hub offers for this Runesmith.</param>
    /// <param name="isInstalled">Whether a plugin is installed, from anywhere and turned on or off.</param>
    /// <param name="showPage">Opens a plugin's page in the plugin manager.</param>
    internal PluginSuggestions(ISettingsService settings, ILanguageRegistry languages, Func<bool> canInstall, Func<IEnumerable<OfficialPlugin>> official,
        Func<string, bool> isInstalled, Action<string> showPage)
    {
        this.settings = settings;
        this.languages = languages;
        this.canInstall = canInstall;
        this.official = official;
        this.isInstalled = isInstalled;
        this.showPage = showPage;
    }

    /// <summary>Gets the plugin to suggest for a file the user opened, or null, and counts it as suggested for the session. A file of a
    /// language an installed plugin brings gets no suggestion.</summary>
    public SuggestedPlugin? For(string? filePath) => filePath is not null && !IsKnown(filePath) ? Suggest(Path.GetFileName(filePath)) : null;

    /// <summary>Gets the next plugin to suggest for the open folder, or null, and counts it as suggested for the session: the first one that
    /// is for a file or a folder at its top.</summary>
    public SuggestedPlugin? ForFolder(string? folder)
    {
        if (folder is null || !canInstall())
            return null;

        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder).Order(StringComparer.OrdinalIgnoreCase))
            {
                if ((Directory.Exists(entry) || !IsKnown(entry)) && Suggest(Path.GetFileName(entry)) is { } plugin)
                    return plugin;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }

        return null;
    }

    /// <summary>Opens the plugin's page in the plugin manager, where the user can install it.</summary>
    public void Install(SuggestedPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        showPage(plugin.PluginId);
    }

    /// <summary>Stops suggesting the plugin, in this session and later ones.</summary>
    public void StopSuggesting(SuggestedPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        Decline(settings, plugin.PluginId);
    }

    /// <summary>Gets the ids of the plugins the user said not to suggest again.</summary>
    internal static HashSet<string> Declined(ISettingsService settings) =>
        settings.Get<string>(HubSettings.DeclinedSuggestions).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Remembers not to suggest a plugin again.</summary>
    internal static void Decline(ISettingsService settings, string pluginId)
    {
        var declined = Declined(settings);
        if (declined.Add(pluginId))
            settings.Set(HubSettings.DeclinedSuggestions, string.Join(';', declined.Order(StringComparer.Ordinal)));
    }

    /// <summary>Gets the official plugins a catalog offers that this Runesmith can install.</summary>
    internal static IEnumerable<OfficialPlugin> Official(HubCatalog? catalog, RunesmithHost host) => catalog is null
        ? []
        : catalog.Plugins.Where(plugin => plugin.Tier == PluginTier.Official && catalog.GetInstallableVersions(plugin.Id, host) is [_, ..])
            .OrderBy(plugin => plugin.Id, StringComparer.Ordinal)
            .Select(plugin => new OfficialPlugin(plugin.Id, plugin.Name, plugin.Keywords));

    /// <summary>Gets the keyword that stands for a file or folder name: its extension without the dot, or the whole name when it has none,
    /// in lowercase, so <c>Program.cs</c> gives <c>cs</c>, <c>Dockerfile</c> gives <c>dockerfile</c> and <c>.git</c> gives <c>git</c>.</summary>
    internal static string KeywordOf(string name)
    {
        var extension = Path.GetExtension(name);
        return (extension.Length > 1 ? extension : name).TrimStart('.').ToLowerInvariant();
    }

    private bool IsKnown(string filePath) => languages.GetLanguageForFile(filePath).Id != ILanguageRegistry.PlainText;

    private SuggestedPlugin? Suggest(string name)
    {
        if (!canInstall() || KeywordOf(name) is not { Length: > 0 } keyword)
            return null;

        var declined = Declined(settings);
        var plugin = official().FirstOrDefault(candidate => candidate.Keywords.Contains(keyword, StringComparer.OrdinalIgnoreCase)
            && !suggested.Contains(candidate.Id) && !declined.Contains(candidate.Id) && !isInstalled(candidate.Id));
        if (plugin is null)
            return null;

        suggested.Add(plugin.Id);
        var extension = Path.GetExtension(name);
        return new SuggestedPlugin(plugin.Id, plugin.Name, extension.Length > 1 ? extension : name);
    }
}
