using Runesmith.Sdk.Documents;

namespace Runesmith.Workspace.Documents;

/// <summary>Moves open documents along with their files, for operations that rename or move files on disk.</summary>
public interface IDocumentLocations
{
    /// <summary>Points the open documents of a file, or of the files in a folder, at their new paths, after the file or folder moved on disk.
    /// Their text, unsaved changes and undo history stay.</summary>
    void Move(string oldPath, string newPath);

    /// <summary>Raised after a document moved, on the UI thread.</summary>
    event EventHandler<DocumentMovedEventArgs>? Moved;
}

/// <summary>A document that moved, and where it was.</summary>
public sealed class DocumentMovedEventArgs(IDocument document, string oldPath) : EventArgs
{
    public IDocument Document { get; } = document;

    public string OldPath { get; } = oldPath;
}
