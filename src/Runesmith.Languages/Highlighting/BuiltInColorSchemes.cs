using System.Composition;
using Runesmith.Sdk.Appearance;

namespace Runesmith.Languages.Highlighting;

/// <summary>Runesmith's own syntax color schemes, added the way a plugin adds its own.</summary>
[Export(typeof(IColorSchemeContributor))]
public sealed class BuiltInColorSchemes : IColorSchemeContributor
{
    public const string DarkId = "runesmith-dark";
    public const string LightId = "runesmith-light";

    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "ColorSchemes");

    /// <summary>Gets the scheme of Runesmith's dark theme.</summary>
    public static ColorScheme Dark { get; } = new(DarkId, "Runesmith Dark", Path.Combine(Folder, "RunesmithDark.json"));

    /// <summary>Gets the scheme of Runesmith's light theme.</summary>
    public static ColorScheme Light { get; } = new(LightId, "Runesmith Light", Path.Combine(Folder, "RunesmithLight.json")) { IsDark = false };

    public IEnumerable<ColorScheme> Schemes => [Dark, Light];
}
