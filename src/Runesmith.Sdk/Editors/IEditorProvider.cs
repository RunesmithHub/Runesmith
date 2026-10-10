namespace Runesmith.Sdk.Editors;

/// <summary>Opens files that are not text, such as images or diagrams, in an editor tab of its own. Export it with
/// <c>[Export(typeof(IEditorProvider))]</c>.</summary>
/// <remarks>Files that match <see cref="EditorProviderDefinition.FilePatterns"/> open in this provider's editor when it is their default, and
/// <b>Open With</b> offers it beside the text editor for them.</remarks>
public interface IEditorProvider
{
    EditorProviderDefinition Definition { get; }

    /// <summary>Creates the editor of a file; called on the UI thread. Read the file here, so the tab opens with it loaded.</summary>
    /// <exception cref="IOException">The file cannot be read; Runesmith tells the user and opens no tab.</exception>
    /// <exception cref="UnauthorizedAccessException">The file cannot be read.</exception>
    /// <exception cref="InvalidDataException">The file is not in a format the editor reads.</exception>
    Task<CustomEditor> CreateEditorAsync(CustomEditorContext context, CancellationToken cancellationToken);
}

/// <summary>Whether an editor opens the files it handles by default.</summary>
public enum EditorPriority
{
    /// <summary>The files open in this editor, unless the user chose another one for them.</summary>
    Default,

    /// <summary>The files open in their usual editor; this one is offered by <b>Open With</b>.</summary>
    Optional,
}

/// <summary>Describes an editor provider.</summary>
/// <param name="Id">A unique id, such as <c>acme.diagrams.editor</c>; the user's choice of default editor refers to it.</param>
/// <param name="Name">The name <b>Open With</b> shows, such as "Diagram Editor".</param>
/// <param name="FilePatterns">The file names it handles, such as <c>*.drawio</c> or <c>Dockerfile</c>; <c>*</c> and <c>?</c> are wildcards,
/// matched against the file's name without case.</param>
public sealed record EditorProviderDefinition(string Id, string Name, IReadOnlyList<string> FilePatterns)
{
    /// <summary>The id of Runesmith's text editor, which every file can open in.</summary>
    public const string TextEditorId = "runesmith.text";

    /// <summary>Gets whether the files open in this editor by default; when several editors are the default for a file, plugins' come before
    /// Runesmith's own.</summary>
    public EditorPriority Priority { get; init; } = EditorPriority.Default;

    /// <summary>Gets the name of the tab's icon, such as <c>image</c>, or null for the file's own icon.</summary>
    public string? Icon { get; init; }
}

/// <summary>The file a custom editor opens.</summary>
/// <param name="FilePath">The file's full path.</param>
/// <param name="IsReadOnly">Whether the file cannot be written, so the editor should not offer changes.</param>
public sealed record CustomEditorContext(string FilePath, bool IsReadOnly)
{
    /// <summary>Gets the file's name, such as <c>logo.png</c>.</summary>
    public string Name => Path.GetFileName(FilePath);
}
