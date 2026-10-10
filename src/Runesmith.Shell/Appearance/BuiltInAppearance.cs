using System.Composition;
using Runesmith.Languages.Highlighting;
using Runesmith.Sdk.Appearance;

namespace Runesmith.Shell.Appearance;

/// <summary>Runesmith's own color themes and file icon theme, added the way a plugin adds its own.</summary>
/// <remarks>The dark and light themes take every color from HammerUI's dark and light palettes, the low contrast themes soften them, and the
/// icon theme leaves every file its language's icon.</remarks>
[Export(typeof(IColorThemeContributor))]
[Export(typeof(IFileIconThemeContributor))]
public sealed class BuiltInAppearance : IColorThemeContributor, IFileIconThemeContributor
{
    public const string DarkTheme = "dark";
    public const string LightTheme = "light";
    public const string LowContrastDarkTheme = "runesmith-low-contrast-dark";
    public const string LowContrastLightTheme = "runesmith-low-contrast-light";
    public const string IconTheme = "runesmith";

    public static ColorTheme Dark { get; } = new(DarkTheme, "Runesmith Dark", IsDark: true) { ColorScheme = BuiltInColorSchemes.DarkId };

    public static ColorTheme Light { get; } = new(LightTheme, "Runesmith Light", IsDark: false) { ColorScheme = BuiltInColorSchemes.LightId };

    /// <summary>Gets a dark theme with lifted backgrounds and gentler text that still reads at 4.5:1 or more.</summary>
    public static ColorTheme LowContrastDark { get; } = new(LowContrastDarkTheme, "Runesmith Low Contrast Dark", IsDark: true)
    {
        ColorScheme = BuiltInColorSchemes.LowContrastDarkId,
        Colors = new ThemeColors
        {
            Accent = "#8A94C8",
            AccentForeground = "#1B1D24",
            Background = "#25272C",
            Surface = "#2A2C32",
            SurfaceRaised = "#33363D",
            SurfaceSunken = "#26282D",
            SurfaceHover = "#0FFFFFFF",
            SurfacePressed = "#1AFFFFFF",
            BorderSubtle = "#32353B",
            BorderStrong = "#3D4048",
            TextPrimary = "#B9BDC5",
            TextSecondary = "#959AA3",
            TextMuted = "#80858E",
            TextDisabled = "#5E626A",
            Success = "#82B08A",
            Warning = "#C9A162",
            Danger = "#D08585",
            Info = "#7FA0CF",
            Scrim = "#80101114",
        },
    };

    /// <summary>Gets a light theme with toned down backgrounds and gentler text that still reads at 4.5:1 or more.</summary>
    public static ColorTheme LowContrastLight { get; } = new(LowContrastLightTheme, "Runesmith Low Contrast Light", IsDark: false)
    {
        ColorScheme = BuiltInColorSchemes.LowContrastLightId,
        Colors = new ThemeColors
        {
            Accent = "#5763A0",
            AccentForeground = "#FFFFFF",
            Background = "#E6E7E9",
            Surface = "#EEEFF1",
            SurfaceRaised = "#F4F5F6",
            SurfaceSunken = "#E9EAEC",
            SurfaceHover = "#0C1E2533",
            SurfacePressed = "#161E2533",
            BorderSubtle = "#DCDEE1",
            BorderStrong = "#CDD0D4",
            TextPrimary = "#45494F",
            TextSecondary = "#62676F",
            TextMuted = "#7A7F87",
            TextDisabled = "#A8ACB2",
            Success = "#4E8A5A",
            Warning = "#A0742B",
            Danger = "#A84D55",
            Info = "#46699F",
            Scrim = "#40202328",
        },
    };

    public static FileIconTheme Icons { get; } = new(IconTheme, "Runesmith");

    public IEnumerable<ColorTheme> Themes => [Dark, Light, LowContrastDark, LowContrastLight];

    public IEnumerable<FileIconTheme> IconThemes => [Icons];
}
