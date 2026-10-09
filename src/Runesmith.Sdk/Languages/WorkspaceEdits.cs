using Runesmith.Text;

namespace Runesmith.Sdk.Languages;

/// <summary>Changes to one file's text.</summary>
/// <param name="FilePath">The file's full path.</param>
/// <param name="Changes">The changes, in the coordinates of the file's text as Runesmith reads it: the open document's text, or the file on disk
/// with its line breaks as <c>\n</c>. They must not overlap.</param>
public sealed record DocumentEdit(string FilePath, IReadOnlyList<TextChange> Changes)
{
    /// <summary>Gets the snapshot of the open document the changes were computed for; when the document has changed since, the edit is not
    /// applied. Null skips the check.</summary>
    public TextSnapshot? Snapshot { get; init; }
}

/// <summary>Changes to the text of one or more files, applied together, such as a rename.</summary>
public sealed record WorkspaceEdit(IReadOnlyList<DocumentEdit> Documents)
{
    /// <summary>Gets an edit that changes nothing.</summary>
    public static WorkspaceEdit Empty { get; } = new([]);

    /// <summary>Gets whether the edit changes nothing.</summary>
    public bool IsEmpty => Documents.All(document => document.Changes.Count == 0);
}

/// <summary>Applies workspace edits: each file's changes become one step of that file's undo history, and files that are not open open in
/// editor tabs, so every change can be seen, undone and saved.</summary>
public interface IWorkspaceEditService
{
    /// <summary>Applies an edit on the UI thread. Nothing is changed, and the user is told why, when a file cannot be opened, is read-only, has
    /// changed since the edit was computed, or has overlapping changes.</summary>
    /// <returns>Whether the edit was applied.</returns>
    Task<bool> ApplyAsync(WorkspaceEdit edit, CancellationToken cancellationToken = default);
}

/// <summary>Thrown by a language feature provider to tell the user why it cannot do what was asked, such as "This element cannot be
/// renamed"; the editor shows the message instead of logging a failure.</summary>
public sealed class LanguageFeatureException : Exception
{
    public LanguageFeatureException()
    {
    }

    public LanguageFeatureException(string message)
        : base(message)
    {
    }

    public LanguageFeatureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
