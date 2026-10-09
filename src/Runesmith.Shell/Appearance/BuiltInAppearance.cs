using System.Composition;
using Runesmith.Languages.Highlighting;
using Runesmith.Sdk.Appearance;

namespace Runesmith.Shell.Appearance;

/// <summary>Runesmith's own color themes and file icon theme, added the way a plugin adds its own.</summary>
/// <remarks>The themes take every color from HammerUI's dark and light palettes, and the icon theme leaves every file its language's icon.</remarks>
[Export(typeof(IColorThemeContributor))]
[Export(typeof(IFileIconThemeContributor))]
public sealed class BuiltInAppearance : IColorThemeContributor, IFileIconThemeContributor
{
    public const string DarkTheme = "dark";
    public const string LightTheme = "light";
    public const string IconTheme = "runesmith";

    public static ColorTheme Dark { get; } = new(DarkTheme, "Runesmith Dark", IsDark: true) { ColorScheme = BuiltInColorSchemes.DarkId };

    public static ColorTheme Light { get; } = new(LightTheme, "Runesmith Light", IsDark: false) { ColorScheme = BuiltInColorSchemes.LightId };

    public static FileIconTheme Icons { get; } = new(IconTheme, "Runesmith");

    public IEnumerable<ColorTheme> Themes => [Dark, Light];

    public IEnumerable<FileIconTheme> IconThemes => [Icons];
}
