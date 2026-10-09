namespace Runesmith.Sdk.Appearance;

/// <summary>A syntax color scheme: the colors and font styles of the editor's tokens, as a TextMate theme file.</summary>
/// <remarks>A plugin adds schemes with an <see cref="IColorSchemeContributor"/>. The user picks one in Appearance, apart from the color
/// theme, or a <see cref="ColorTheme.ColorScheme"/> picks it. Runesmith reads the file's <c>tokenColors</c> (or <c>settings</c>) rules;
/// the editor's background and plain text keep the color theme's colors. A file that cannot be read is left out.</remarks>
/// <param name="Id">A unique id, such as <c>ember.dusk</c>; the <c>editor.colorScheme</c> setting stores it.</param>
/// <param name="Name">The name shown to the user.</param>
/// <param name="FilePath">The full path of the TextMate theme, in JSON format.</param>
public sealed record ColorScheme(string Id, string Name, string FilePath)
{
    /// <summary>Gets whether the scheme is made for a dark background, so Appearance can list it with the matching themes.</summary>
    public bool IsDark { get; init; } = true;
}

/// <summary>Adds syntax color schemes. Export it with <c>[Export(typeof(IColorSchemeContributor))]</c>.</summary>
/// <remarks>Runesmith reads <see cref="Schemes"/> once, at start, so the contributor should only return data.</remarks>
public interface IColorSchemeContributor
{
    IEnumerable<ColorScheme> Schemes { get; }
}
