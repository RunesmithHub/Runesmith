using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using HammerUI.Controls;
using Runesmith.Composition;

namespace Runesmith.Shell.Appearance;

/// <summary>One entry of an appearance picker: the value the setting stores, its name, where it comes from, and a few of its colors.</summary>
internal sealed record AppearanceChoice(string Value, string Title, string? Detail = null)
{
    public IReadOnlyList<Color> Swatch { get; init; } = [];
}

/// <summary>The pickers of the color theme, the accent color, the syntax colors and the file icons, shared by the settings page and the
/// welcome screen's Customize page.</summary>
internal static class AppearancePickers
{
    /// <summary>The accent colors offered as swatches; any other is typed as a hex color.</summary>
    public static IList<Color> Accents { get; } =
    [
        Color.Parse("#7073F6"), Color.Parse("#3B82F6"), Color.Parse("#0EA5E9"), Color.Parse("#14B8A6"), Color.Parse("#22C55E"),
        Color.Parse("#EAB308"), Color.Parse("#F97316"), Color.Parse("#EF4444"), Color.Parse("#EC4899"), Color.Parse("#A855F7"),
    ];

    public static List<AppearanceChoice> Themes(AppearanceCatalog catalog) =>
    [
        new(AppearanceCatalog.System, "Follow the system", $"{catalog.Dark.Theme.Name} or {catalog.Light.Theme.Name}")
        {
            Swatch = [catalog.Dark.Palette.Surface, catalog.Light.Palette.Surface],
        },
        .. catalog.Themes.Select(e => new AppearanceChoice(e.Theme.Id, e.Theme.Name, Source(e.Plugin, e.Theme.IsDark ? "Dark" : "Light"))
        {
            Swatch = [e.Palette.Surface, e.Palette.TextPrimary, e.Palette.Accent],
        }),
    ];

    public static List<AppearanceChoice> Schemes(AppearanceCatalog catalog) =>
    [
        new("", "The color theme's own"),
        .. catalog.Schemes.Select(e => new AppearanceChoice(e.Scheme.Id, e.Scheme.Name, Source(e.Plugin, e.Scheme.IsDark ? "Dark" : "Light"))),
    ];

    public static List<AppearanceChoice> IconThemes(AppearanceCatalog catalog) =>
        [.. catalog.IconThemes.Select(e => new AppearanceChoice(e.Theme.Id, e.Theme.Name, Source(e.Plugin, null)))];

    /// <summary>Creates a drop-down of <paramref name="choices"/> showing <paramref name="current"/>, which calls <paramref name="set"/> with
    /// the value of the entry picked. A value that is no longer offered shows as not installed.</summary>
    public static ComboBox Picker(List<AppearanceChoice> choices, string current, Action<string> set)
    {
        var selected = choices.FirstOrDefault(c => string.Equals(c.Value, current, StringComparison.OrdinalIgnoreCase));
        if (selected is null)
        {
            selected = new AppearanceChoice(current, current, "Not installed");
            choices.Add(selected);
        }

        var box = new ComboBox
        {
            ItemsSource = choices,
            SelectedItem = selected,
            MinWidth = 160,
            ItemTemplate = new FuncDataTemplate<AppearanceChoice?>((choice, _) => choice is null ? new Panel() : Entry(choice)),
        };
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is AppearanceChoice choice && choice != selected)
            {
                selected = choice;
                set(choice.Value);
            }
        };
        return box;
    }

    /// <summary>Creates the accent color's swatches and hex field, and a button back to the theme's own accent.</summary>
    /// <param name="current">The accent set, or an empty text for the theme's own.</param>
    /// <param name="themeAccent">The accent of the theme that shows, shown while <paramref name="current"/> is empty.</param>
    public static WrapPanel Accent(string current, Color themeAccent, Action<string> set)
    {
        var hasColor = Color.TryParse(current, out var color);
        var picker = new ColorSwatchPicker { Colors = Accents, SelectedColor = hasColor ? color : themeAccent };
        picker.PropertyChanged += (_, e) =>
        {
            if (e.Property == ColorSwatchPicker.SelectedColorProperty && e.NewValue is Color picked && picked != (hasColor ? color : themeAccent))
                set($"#{picked.R:X2}{picked.G:X2}{picked.B:X2}");
        };
        var themeDefault = new Button { Classes = { "small" }, Content = "Theme default", IsEnabled = hasColor, VerticalAlignment = VerticalAlignment.Center };
        themeDefault.Click += (_, _) => set("");
        return new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Children = { picker, themeDefault } };
    }

    private static string? Source(PluginInfo? plugin, string? kind) =>
        (plugin, kind) switch
        {
            (null, null) => null,
            (null, _) => kind,
            (_, null) => plugin.Manifest.Name,
            _ => $"{kind}, {plugin.Manifest.Name}",
        };

    private static DockPanel Entry(AppearanceChoice choice)
    {
        var row = new DockPanel();
        if (choice.Swatch.Count > 0)
        {
            var swatch = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            foreach (var color in choice.Swatch)
                swatch.Children.Add(new ColorSwatch { Color = color, Width = 10, Height = 14, CornerRadius = new CornerRadius(2) });
            DockPanel.SetDock(swatch, Dock.Left);
            row.Children.Add(swatch);
        }

        var title = new TextBlock { Text = choice.Title, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        if (choice.Detail is { } detail)
        {
            DockPanel.SetDock(title, Dock.Left);
            row.Children.Add(title);
            row.Children.Add(new TextBlock
            {
                Text = detail,
                Classes = { "caption", "muted" },
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }
        else
        {
            row.Children.Add(title);
        }

        return row;
    }
}
