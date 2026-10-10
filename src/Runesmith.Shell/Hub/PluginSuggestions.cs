using System.Composition;
using Runesmith.Sdk.Settings;

namespace Runesmith.Shell.Hub;

/// <summary>An official plugin on the hub that brings a language's features, and the files that tell Runesmith to suggest it.</summary>
/// <param name="PluginId">The plugin's id on the hub.</param>
/// <param name="Language">The language's name, as the suggestion shows it.</param>
/// <param name="Extensions">The file extensions, with their dot.</param>
/// <param name="FileNames">Whole file names, such as <c>pom.xml</c>.</param>
public sealed record SuggestedPlugin(string PluginId, string Language, IReadOnlyList<string> Extensions, IReadOnlyList<string> FileNames)
{
    /// <summary>Gets what the suggestion says.</summary>
    public string Message => $"Install the {Language} plugin for completion, problems and builds.";

    /// <summary>Gets whether a file is one of the language's.</summary>
    public bool Matches(string filePath)
    {
        var name = Path.GetFileName(filePath);
        return FileNames.Contains(name, StringComparer.OrdinalIgnoreCase)
            || Extensions.Any(extension => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Suggests installing the official plugin of a language when the user opens one of its files and the plugin is not installed: once
/// per language in a session, and never again for a language the user said not to.</summary>
[Export]
[Shared]
public sealed class PluginSuggestions
{
    /// <summary>The official language plugins Runesmith suggests.</summary>
    public static IReadOnlyList<SuggestedPlugin> Plugins { get; } =
    [
        new("runesmith.csharp", "C#", [".cs", ".csproj", ".sln", ".slnx", ".razor"], []),
        new("runesmith.java", "Java", [".java"], ["pom.xml", "build.gradle", "build.gradle.kts"]),
    ];

    private readonly ISettingsService settings;
    private readonly Func<bool> canInstall;
    private readonly Func<string, bool> isInstalled;
    private readonly Action<string> showPage;
    private readonly HashSet<string> suggested = new(StringComparer.Ordinal);

    [ImportingConstructor]
    public PluginSuggestions(ISettingsService settings, Lazy<HubService> hub)
        : this(
            settings,
            () => hub.Value.Client.Status.HasCatalog,
            id => hub.Value.Model.Installed().Any(row => string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase)),
            id => hub.Value.ShowPage(id))
    {
    }

    /// <param name="canInstall">Whether the hub is set up and turned on.</param>
    /// <param name="isInstalled">Whether a plugin is installed, from anywhere and turned on or off.</param>
    /// <param name="showPage">Opens a plugin's page in the plugin manager.</param>
    internal PluginSuggestions(ISettingsService settings, Func<bool> canInstall, Func<string, bool> isInstalled, Action<string> showPage)
    {
        this.settings = settings;
        this.canInstall = canInstall;
        this.isInstalled = isInstalled;
        this.showPage = showPage;
    }

    /// <summary>Gets the plugin to suggest for a file the user opened, or null, and counts it as suggested for the session.</summary>
    public SuggestedPlugin? For(string? filePath)
    {
        if (filePath is null || Plugins.FirstOrDefault(p => p.Matches(filePath)) is not { } plugin || suggested.Contains(plugin.PluginId))
            return null;
        if (Declined().Contains(plugin.PluginId) || !canInstall() || isInstalled(plugin.PluginId))
            return null;

        suggested.Add(plugin.PluginId);
        return plugin;
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
        var declined = Declined();
        if (declined.Add(plugin.PluginId))
            settings.Set(HubSettings.DeclinedSuggestions, string.Join(';', declined.Order(StringComparer.Ordinal)));
    }

    private HashSet<string> Declined() =>
        settings.Get<string>(HubSettings.DeclinedSuggestions).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
