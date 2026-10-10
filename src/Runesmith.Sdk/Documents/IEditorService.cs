using Runesmith.Sdk.Documents.Snippets;
using Runesmith.Text;

namespace Runesmith.Sdk.Documents;

/// <summary>Opens documents in editors and moves between them.</summary>
public interface IEditorService
{
    /// <summary>Gets the editor that has, or last had, the keyboard focus, or null when no editor is open.</summary>
    IEditorView? ActiveEditor { get; }

    /// <summary>Gets the open editors.</summary>
    IReadOnlyList<IEditorView> Editors { get; }

    /// <summary>Opens a file in its default editor, or switches to its editor, and moves the caret to <paramref name="position"/> when it is given;
    /// with a position, a file that is not open in a custom editor opens in the text editor.</summary>
    /// <returns>The text editor, or null when the file opened in a custom editor, or could not be opened and the user has been told why.</returns>
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

    /// <summary>Replaces the selection with a snippet, as one undo step, and selects its first tab stop; Tab and Shift+Tab then move between
    /// the tab stops, and Esc leaves them. See <see cref="SnippetString"/> for the syntax. Call it on the UI thread.</summary>
    /// <remarks>A snippet that uses <c>$CLIPBOARD</c> is inserted once the clipboard has been read. An editor that has no tab stops inserts
    /// the snippet's text with the caret at its final position. Added in plugin API 0.1.2.</remarks>
    void InsertSnippet(string snippet)
    {
        var selection = Selection;
        var snapshot = Document.Buffer.Current;
        var context = new SnippetContext(Document.FilePath, Document.Name, snapshot, selection);
        var expansion = SnippetExpansion.Create(SnippetParser.Parse(snippet), name => SnippetVariables.Resolve(name, context), SnippetVariables.IndentOf(snapshot, selection.Start));
        var changes = Document.Buffer.Replace(selection, expansion.Text);
        Document.History.Push(changes, null, null);
        CaretOffset = selection.Start + expansion.FinalOffset;
    }

    /// <summary>Replaces the selection with a snippet built with <see cref="SnippetString"/>; see <see cref="InsertSnippet(string)"/>.</summary>
    /// <remarks>Added in plugin API 0.1.2.</remarks>
    void InsertSnippet(SnippetString snippet)
    {
        ArgumentNullException.ThrowIfNull(snippet);
        InsertSnippet(snippet.Value);
    }
}
