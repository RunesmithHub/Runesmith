namespace Runesmith.Sdk.Appearance;

/// <summary>A color theme: the colors of Runesmith's window, panels, menus and editor, for a dark or a light look.</summary>
/// <remarks>A plugin adds themes with an <see cref="IColorThemeContributor"/>. The user picks one in Appearance; Runesmith applies it at once
/// and goes back to its own theme when the plugin is gone. A theme whose text would be hard to read on its backgrounds is left out.</remarks>
/// <param name="Id">A unique id, conventionally the plugin's id and a name, such as <c>ember.dusk</c>; the <c>appearance.theme</c> setting
/// stores it.</param>
/// <param name="Name">The name shown to the user, such as "Ember Dusk".</param>
/// <param name="IsDark">Whether the theme is dark; <see cref="ThemeColors.Background"/> has to agree.</param>
public sealed record ColorTheme(string Id, string Name, bool IsDark)
{
    /// <summary>Gets the theme's colors; the ones it leaves out come from Runesmith's own dark or light theme.</summary>
    public ThemeColors Colors { get; init; } = new();

    /// <summary>Gets the id of the <see cref="ColorScheme"/> the editor colors code with while the theme shows, unless the user picked
    /// another; null for Runesmith's own dark or light scheme.</summary>
    public string? ColorScheme { get; init; }
}

/// <summary>The colors of a <see cref="ColorTheme"/>, each a hex color such as <c>#1E1F24</c>, or <c>#AARRGGBB</c> with transparency.
/// A color left null comes from Runesmith's own dark or light theme.</summary>
public sealed record ThemeColors
{
    /// <summary>Gets the accent: the focused element, the primary button, the selected item and progress. The user's accent color, when
    /// set, replaces it.</summary>
    public string? Accent { get; init; }

    /// <summary>Gets the text and icons on the accent.</summary>
    public string? AccentForeground { get; init; }

    /// <summary>Gets the window's own surface: the toolbar, the stripes and the status bar.</summary>
    public string? Background { get; init; }

    /// <summary>Gets the surface of panels and the editor.</summary>
    public string? Surface { get; init; }

    /// <summary>Gets the surface of popups, menus and dialogs.</summary>
    public string? SurfaceRaised { get; init; }

    /// <summary>Gets the surface of fields and wells set into a panel.</summary>
    public string? SurfaceSunken { get; init; }

    /// <summary>Gets the tint over a hovered item, usually translucent.</summary>
    public string? SurfaceHover { get; init; }

    /// <summary>Gets the tint over a pressed item, usually translucent.</summary>
    public string? SurfacePressed { get; init; }

    public string? BorderSubtle { get; init; }

    public string? BorderStrong { get; init; }

    /// <summary>Gets the color of most text, including the editor's.</summary>
    public string? TextPrimary { get; init; }

    public string? TextSecondary { get; init; }

    /// <summary>Gets the color of hints, captions and line numbers.</summary>
    public string? TextMuted { get; init; }

    public string? TextDisabled { get; init; }

    public string? Success { get; init; }

    public string? Warning { get; init; }

    public string? Danger { get; init; }

    public string? Info { get; init; }

    /// <summary>Gets the translucent shade behind dialogs.</summary>
    public string? Scrim { get; init; }

    /// <summary>Gets the terminal's 16 ANSI colors: black, red, green, yellow, blue, magenta, cyan and white, then their bright forms in the
    /// same order. Null, or a list without 16 colors, has the terminal derive them from the theme's other colors.</summary>
    /// <remarks>Added in plugin API 0.1.2.</remarks>
    public IReadOnlyList<string>? TerminalColors { get; init; }
}

/// <summary>Adds color themes. Export it with <c>[Export(typeof(IColorThemeContributor))]</c>.</summary>
/// <remarks>Runesmith reads <see cref="Themes"/> once, at start, before plugins initialize, so the contributor should only return data.</remarks>
public interface IColorThemeContributor
{
    IEnumerable<ColorTheme> Themes { get; }
}
