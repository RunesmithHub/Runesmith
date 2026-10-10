using System.Composition;
using System.Text.Json;
using Avalonia.Media;
using HammerUI.Theming;
using Runesmith.Composition;
using Runesmith.Sdk.Appearance;

namespace Runesmith.Shell.Appearance;

/// <summary>A color theme that passed the checks, with its palette and the plugin it came from, or null for Runesmith's own.</summary>
public sealed record ThemeEntry(ColorTheme Theme, ThemePalette Palette, PluginInfo? Plugin);

/// <summary>A syntax color scheme and the plugin it came from, or null for Runesmith's own.</summary>
public sealed record SchemeEntry(ColorScheme Scheme, PluginInfo? Plugin);

/// <summary>A file icon theme and the plugin it came from, or null for Runesmith's own.</summary>
public sealed record IconThemeEntry(FileIconTheme Theme, PluginInfo? Plugin);

/// <summary>Every color theme, syntax color scheme and file icon theme, Runesmith's own first, collected once at start.</summary>
/// <remarks>A contribution with an id already taken, or one that fails the checks, is left out and named in <see cref="Problems"/>.</remarks>
[Export]
[Shared]
public sealed class AppearanceCatalog
{
    /// <summary>The <c>appearance.theme</c> value that follows the system between Runesmith's dark and light themes.</summary>
    public const string System = "system";

    private readonly List<string> problems = [];

    [ImportingConstructor]
    public AppearanceCatalog(
        [ImportMany] IEnumerable<IColorThemeContributor> themes,
        [ImportMany] IEnumerable<IColorSchemeContributor> schemes,
        [ImportMany] IEnumerable<IFileIconThemeContributor> iconThemes)
        : this(themes, schemes, iconThemes, contributor => PluginCallers.Of(contributor.GetType().Assembly))
    {
    }

    internal AppearanceCatalog(
        IEnumerable<IColorThemeContributor> themes,
        IEnumerable<IColorSchemeContributor> schemes,
        IEnumerable<IFileIconThemeContributor> iconThemes,
        Func<object, PluginInfo?> ownerOf)
    {
        Themes = Collect(themes, c => c.Themes, t => t.Id, t => t.Name, ownerOf, "color theme")
            .Select(e => ThemePalettes.Create(e.Item, e.Item.IsDark ? ThemePalette.Dark : ThemePalette.Light, problems) is { } palette ? new ThemeEntry(e.Item, palette, e.Plugin) : null)
            .OfType<ThemeEntry>()
            .ToList();
        Schemes = [.. Collect(schemes, c => c.Schemes, s => s.Id, s => s.Name, ownerOf, "syntax color scheme").Where(e => CheckScheme(e.Item)).Select(e => new SchemeEntry(e.Item, e.Plugin))];
        IconThemes = [.. Collect(iconThemes, c => c.IconThemes, t => t.Id, t => t.Name, ownerOf, "file icon theme").Where(e => CheckIconTheme(e.Item)).Select(e => new IconThemeEntry(e.Item, e.Plugin))];
        Dark = FindTheme(BuiltInAppearance.DarkTheme) ?? new ThemeEntry(BuiltInAppearance.Dark, ThemePalette.Dark, null);
        Light = FindTheme(BuiltInAppearance.LightTheme) ?? new ThemeEntry(BuiltInAppearance.Light, ThemePalette.Light, null);
    }

    public IReadOnlyList<ThemeEntry> Themes { get; }

    public IReadOnlyList<SchemeEntry> Schemes { get; }

    public IReadOnlyList<IconThemeEntry> IconThemes { get; }

    /// <summary>Gets Runesmith's own dark theme.</summary>
    public ThemeEntry Dark { get; }

    /// <summary>Gets Runesmith's own light theme.</summary>
    public ThemeEntry Light { get; }

    /// <summary>Gets what was left out and why, and what may be hard to read.</summary>
    public IReadOnlyList<string> Problems => problems;

    public ThemeEntry? FindTheme(string? id) => Themes.FirstOrDefault(e => string.Equals(e.Theme.Id, id, StringComparison.OrdinalIgnoreCase));

    public SchemeEntry? FindScheme(string? id) => Schemes.FirstOrDefault(e => string.Equals(e.Scheme.Id, id, StringComparison.OrdinalIgnoreCase));

    public IconThemeEntry? FindIconTheme(string? id) => IconThemes.FirstOrDefault(e => string.Equals(e.Theme.Id, id, StringComparison.OrdinalIgnoreCase));

    private List<(T Item, PluginInfo? Plugin)> Collect<TContributor, T>(IEnumerable<TContributor> contributors, Func<TContributor, IEnumerable<T>> items,
        Func<T, string> idOf, Func<T, string> nameOf, Func<object, PluginInfo?> ownerOf, string kind)
        where TContributor : notnull
    {
        var collected = new List<(T Item, PluginInfo? Plugin)>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { System };
        foreach (var contributor in contributors)
        {
            var plugin = ownerOf(contributor);
            var source = plugin is null ? "Runesmith" : $"The plugin {plugin.Manifest.Name}";
            try
            {
                foreach (var item in items(contributor))
                {
                    if (item is null || string.IsNullOrWhiteSpace(idOf(item)) || string.IsNullOrWhiteSpace(nameOf(item)))
                        problems.Add($"{source} added a {kind} without an id or a name; it is left out.");
                    else if (!ids.Add(idOf(item)))
                        problems.Add($"{source} added the {kind} {idOf(item)}, but another has that id; it is left out.");
                    else
                        collected.Add((item, plugin));
                }
            }
            catch (Exception exception)
            {
                problems.Add($"{source} could not list its {kind}s: {exception.Message}");
            }
        }

        return [.. collected.OrderBy(e => e.Plugin is null ? 0 : 1).ThenBy(e => e.Plugin is null ? "" : nameOf(e.Item), StringComparer.CurrentCultureIgnoreCase)];
    }

    private bool CheckScheme(ColorScheme scheme)
    {
        if (!scheme.Json.IsEmpty)
            return CheckSchemeJson(scheme);

        if (!string.IsNullOrWhiteSpace(scheme.FilePath) && Path.IsPathFullyQualified(scheme.FilePath) && File.Exists(scheme.FilePath))
            return true;

        problems.Add($"The syntax color scheme {scheme.Name} ({scheme.Id}) is left out: its file {scheme.FilePath} does not exist.");
        return false;
    }

    private bool CheckSchemeJson(ColorScheme scheme)
    {
        try
        {
            using var json = JsonDocument.Parse(scheme.Json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (json.RootElement.ValueKind == JsonValueKind.Object)
                return true;

            problems.Add($"The syntax color scheme {scheme.Name} ({scheme.Id}) is left out: its JSON is not an object.");
        }
        catch (JsonException exception)
        {
            problems.Add($"The syntax color scheme {scheme.Name} ({scheme.Id}) is left out: its JSON cannot be read: {exception.Message}");
        }

        return false;
    }

    private bool CheckIconTheme(FileIconTheme theme)
    {
        var icons = new[] { theme.File, theme.Folder }.OfType<FileIcon>()
            .Concat(theme.FileNames?.Values ?? []).Concat(theme.Extensions?.Values ?? []).Concat(theme.Languages?.Values ?? []).Concat(theme.FolderNames?.Values ?? []);
        var bad = icons.Where(icon => icon is null || string.IsNullOrWhiteSpace(icon.Data) || !IsColor(icon.Color) || !IsColor(icon.LightColor)).ToList();
        if (bad.Count == 0)
            return true;

        problems.Add($"The file icon theme {theme.Name} ({theme.Id}) is left out: {bad.Count} of its icons have no path data or a color that is not a hex color.");
        return false;
    }

    private static bool IsColor(string? value) => value is null || Color.TryParse(value, out _);
}
