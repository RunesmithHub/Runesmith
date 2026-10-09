using Runesmith.Text;

namespace Runesmith.Sdk.Documents;

/// <summary>Opens documents in editors and moves between them.</summary>
public interface IEditorService
{
    /// <summary>Gets the editor that has, or last had, the keyboard focus, or null when no editor is open.</summary>
    IEditorView? ActiveEditor { get; }

    /// <summary>Gets the open editors.</summary>
    IReadOnlyList<IEditorView> Editors { get; }

    /// <summary>Opens a file in an editor, or switches to its editor, and moves the caret to <paramref name="position"/> when it is given.</summary>
    /// <returns>The editor, or null when the file could not be opened; the user has been told why.</returns>
    Task<IEditorView?> OpenAsync(string filePath, TextPosition? position = null, bool activate = true);

    /// <summary>Opens a document that is open already, such as a new untitled one, in an editor.</summary>
    IEditorView Open(IDocument document, bool activate = true);

    /// <summary>Closes an editor; with unsaved changes it asks first. Returns whether it closed.</summary>
    Task<bool> CloseAsync(IEditorView editor);

    /// <summary>Raised when another editor becomes the active one, or the last one closes.</summary>
    event EventHandler? ActiveEditorChanged;
}

/// <summary>An editor showing a document.</summary>
public interface IEditorView
{
    IDocument Document { get; }

    /// <summary>Gets or sets the caret's offset in the document's text.</summary>
    int CaretOffset { get; set; }

    /// <summary>Gets the selected text's span, empty at the caret when nothing is selected.</summary>
    TextSpan Selection { get; }

    /// <summary>Selects a span; the caret goes to its end.</summary>
    void Select(TextSpan span);

    /// <summary>Scrolls so that the offset is in view, centering it when it is far away.</summary>
    void ScrollTo(int offset);

    /// <summary>Gives the editor the keyboard focus.</summary>
    void Focus();

    /// <summary>Gets or sets how the caret is drawn, such as a block for a modal editing plugin.</summary>
    EditorCaretStyle CaretStyle { get; set; }

    /// <summary>Raised when the caret moves or the selection changes.</summary>
    event EventHandler? CaretMoved;
}
