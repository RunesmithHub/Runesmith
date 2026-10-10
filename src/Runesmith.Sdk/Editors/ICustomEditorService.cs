namespace Runesmith.Sdk.Editors;

/// <summary>Tells which editors can open a file and opens it in a chosen one.</summary>
/// <remarks><see cref="Documents.IEditorService.OpenAsync"/> opens a file in its default editor: the one the user chose for its kind of
/// file, or else the custom editor that is the default for it, or else the text editor.</remarks>
public interface ICustomEditorService
{
    /// <summary>Gets the editors that can open a file: the text editor first, then the custom editors whose patterns match it.</summary>
    IReadOnlyList<EditorChoice> GetChoices(string filePath);

    /// <summary>Opens a file in an editor, by its id or <see cref="EditorProviderDefinition.TextEditorId"/>, replacing the tab of another
    /// editor that has the file open; asks first when that one has unsaved changes. Returns whether the file opened.</summary>
    /// <exception cref="ArgumentException">No editor with the id can open the file.</exception>
    Task<bool> OpenWithAsync(string filePath, string editorId, bool activate = true);
}

/// <summary>An editor that can open a file.</summary>
/// <param name="Id">The editor's id, as in <see cref="EditorProviderDefinition.Id"/>.</param>
/// <param name="Name">The name to show, such as "Image Viewer".</param>
/// <param name="IsDefault">Whether the file opens in this editor by default.</param>
public sealed record EditorChoice(string Id, string Name, bool IsDefault);
