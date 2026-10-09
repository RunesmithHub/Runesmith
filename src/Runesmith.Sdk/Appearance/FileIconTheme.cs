namespace Runesmith.Sdk.Appearance;

/// <summary>A file icon theme: the icons of files and folders in the Explorer, the editor tabs, Quick Open and the search results.</summary>
/// <remarks>A plugin adds icon themes with an <see cref="IFileIconThemeContributor"/>, and the user picks one in Appearance. A file takes
/// the first icon that matches, in this order: its name in <see cref="FileNames"/>, its extension in <see cref="Extensions"/> (the longest
/// first, so <c>.test.ts</c> wins over <c>.ts</c>), its language in <see cref="Languages"/>, then <see cref="File"/>. A folder takes its
/// name in <see cref="FolderNames"/>, then <see cref="Folder"/>. Anything the theme does not cover keeps Runesmith's own icon. Names and
/// extensions ignore case.</remarks>
/// <param name="Id">A unique id, such as <c>ember.icons</c>; the <c>appearance.fileIconTheme</c> setting stores it.</param>
/// <param name="Name">The name shown to the user.</param>
public sealed record FileIconTheme(string Id, string Name)
{
    /// <summary>Gets the icon of files nothing else matches, or null for Runesmith's icon of the file's language.</summary>
    public FileIcon? File { get; init; }

    /// <summary>Gets the icon of folders <see cref="FolderNames"/> does not match, or null for Runesmith's folder icon.</summary>
    public FileIcon? Folder { get; init; }

    /// <summary>Gets icons by whole file name, such as <c>Dockerfile</c> or <c>package.json</c>.</summary>
    public IReadOnlyDictionary<string, FileIcon> FileNames { get; init; } = new Dictionary<string, FileIcon>();

    /// <summary>Gets icons by file extension, with the dot, such as <c>.cs</c> or <c>.d.ts</c>.</summary>
    public IReadOnlyDictionary<string, FileIcon> Extensions { get; init; } = new Dictionary<string, FileIcon>();

    /// <summary>Gets icons by language id, such as <c>csharp</c>, for files of that language that no name or extension matches.</summary>
    public IReadOnlyDictionary<string, FileIcon> Languages { get; init; } = new Dictionary<string, FileIcon>();

    /// <summary>Gets icons by folder name, such as <c>src</c> or <c>.git</c>.</summary>
    public IReadOnlyDictionary<string, FileIcon> FolderNames { get; init; } = new Dictionary<string, FileIcon>();
}

/// <summary>An icon of a <see cref="FileIconTheme"/>, drawn from vector path data the way Runesmith's own icons are.</summary>
/// <param name="Data">SVG path data on a 24 × 24 grid, such as <c>M6 2h9l5 5v15H6z</c>; several figures go in one string, each starting with
/// <c>M</c>. Stroked figures use a 2 unit wide pen with round caps and joins.</param>
/// <param name="Color">The icon's color as a hex color such as <c>#E4A23C</c>, or null for the theme's text color.</param>
public sealed record FileIcon(string Data, string? Color = null)
{
    /// <summary>Gets the color used while a light color theme shows, or null for <see cref="Color"/>.</summary>
    public string? LightColor { get; init; }

    /// <summary>Gets whether the figures are filled rather than stroked.</summary>
    public bool IsFilled { get; init; }
}

/// <summary>Adds file icon themes. Export it with <c>[Export(typeof(IFileIconThemeContributor))]</c>.</summary>
/// <remarks>Runesmith reads <see cref="IconThemes"/> once, at start, so the contributor should only return data.</remarks>
public interface IFileIconThemeContributor
{
    IEnumerable<FileIconTheme> IconThemes { get; }
}
