using System.Composition;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Sdk.Appearance;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Settings;
using Runesmith.Sdk.Shell;
using Runesmith.Shell.Services;

namespace Runesmith.Shell.Appearance;

/// <summary>An icon to draw for a file or folder: its geometry on the 24 × 24 grid, its color, or null for the place's own, and whether it is
/// filled.</summary>
public sealed record ThemedIcon(Geometry Data, Color? Color = null, bool IsFilled = false)
{
    /// <summary>Gets a brush of <see cref="Color"/>, or null.</summary>
    public IBrush? Brush => Color is { } color ? new ImmutableSolidColorBrush(color) : null;

    /// <summary>Shows the icon in a <see cref="SymbolIcon"/>, in <see cref="Color"/> or else the theme brush named
    /// <paramref name="defaultBrush"/>.</summary>
    public void ApplyTo(SymbolIcon icon, string defaultBrush)
    {
        ArgumentNullException.ThrowIfNull(icon);
        icon.Data = Data;
        icon.IsFilled = IsFilled;
        if (Brush is { } brush)
            icon.Foreground = brush;
        else
            icon[!SymbolIcon.ForegroundProperty] = new DynamicResourceExtension(defaultBrush);
    }
}

/// <summary>The icons of files and folders from the chosen file icon theme, falling back to Runesmith's own; use it on the UI thread.</summary>
[Export]
[Shared]
public sealed class FileIcons
{
    private readonly AppearanceCatalog catalog;
    private readonly ISettingsService settings;
    private readonly ILanguageRegistry languages;
    private readonly IThemeService theme;
    private readonly Lazy<IOutputService> output;
    private readonly Dictionary<string, Geometry?> geometries = new(StringComparer.Ordinal);
    private Compiled compiled;

    [ImportingConstructor]
    public FileIcons(AppearanceCatalog catalog, ISettingsService settings, ILanguageRegistry languages, IThemeService theme, Lazy<IOutputService> output)
    {
        this.catalog = catalog;
        this.settings = settings;
        this.languages = languages;
        this.theme = theme;
        this.output = output;
        compiled = Compile();
        settings.Changed += (_, e) =>
        {
            if (e.Key != SettingKeys.FileIconTheme)
                return;

            var next = Compile();
            if (next.Entry == compiled.Entry)
                return;
            compiled = next;
            Changed?.Invoke(this, EventArgs.Empty);
        };
        theme.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Gets the file icon theme in use.</summary>
    public IconThemeEntry Current => compiled.Entry;

    /// <summary>Raised, on the UI thread, when icons may look different: another icon theme, or a switch between dark and light.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the icon of a file or a folder, for a file of the language <paramref name="languageId"/> when it is given.</summary>
    public ThemedIcon For(string path, bool isDirectory = false, string? languageId = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        var name = Path.GetFileName(path.TrimEnd('/', '\\'));
        if (isDirectory)
            return Themed(compiled.FolderNames.GetValueOrDefault(name) ?? compiled.Entry.Theme.Folder) ?? new ThemedIcon(Icons.Folder);

        var language = (languageId is null ? null : languages.Find(languageId)) ?? languages.GetLanguageForFile(path);
        return Themed(compiled.FileNames.GetValueOrDefault(name) ?? ByExtension(name) ?? compiled.Languages.GetValueOrDefault(language.Id) ?? compiled.Entry.Theme.File)
               ?? new ThemedIcon(Icons.Find(language.Icon) ?? Icons.File);
    }

    private FileIcon? ByExtension(string name)
    {
        for (var dot = name.IndexOf('.', 1); dot > 0; dot = name.IndexOf('.', dot + 1))
        {
            if (compiled.Extensions.GetValueOrDefault(name[dot..]) is { } icon)
                return icon;
        }

        return null;
    }

    private ThemedIcon? Themed(FileIcon? icon)
    {
        if (icon is null || Geometry(icon.Data) is not { } data)
            return null;

        var hex = !theme.IsDark && icon.LightColor is not null ? icon.LightColor : icon.Color;
        return new ThemedIcon(data, Avalonia.Media.Color.TryParse(hex, out var color) ? color : null, icon.IsFilled);
    }

    private Geometry? Geometry(string data)
    {
        if (geometries.TryGetValue(data, out var geometry))
            return geometry;

        try
        {
            geometry = StreamGeometry.Parse(data);
        }
        catch (Exception exception)
        {
            output.Value.GetChannel(PluginAccess.ChannelName)
                .AppendLine($"An icon of the file icon theme {compiled.Entry.Theme.Name} ({compiled.Entry.Theme.Id}) cannot be drawn, so Runesmith's own shows: {exception.Message}");
        }

        geometries[data] = geometry;
        return geometry;
    }

    private Compiled Compile()
    {
        var entry = catalog.FindIconTheme(settings.Get<string>(SettingKeys.FileIconTheme)) ?? catalog.FindIconTheme(BuiltInAppearance.IconTheme)
                    ?? new IconThemeEntry(BuiltInAppearance.Icons, null);
        var theme = entry.Theme;
        return new Compiled(entry, Map(theme.FileNames), Map(theme.Extensions), Map(theme.Languages), Map(theme.FolderNames));
    }

    private static Dictionary<string, FileIcon> Map(IReadOnlyDictionary<string, FileIcon>? icons)
    {
        var map = new Dictionary<string, FileIcon>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, icon) in icons ?? new Dictionary<string, FileIcon>())
            map.TryAdd(key, icon);
        return map;
    }

    private sealed record Compiled(
        IconThemeEntry Entry,
        Dictionary<string, FileIcon> FileNames,
        Dictionary<string, FileIcon> Extensions,
        Dictionary<string, FileIcon> Languages,
        Dictionary<string, FileIcon> FolderNames);
}
