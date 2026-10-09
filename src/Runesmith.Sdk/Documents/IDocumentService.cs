namespace Runesmith.Sdk.Documents;

/// <summary>Opens, saves and closes documents, and keeps one document per file.</summary>
public interface IDocumentService
{
    /// <summary>Gets the open documents, in the order they were opened.</summary>
    IReadOnlyList<IDocument> Documents { get; }

    /// <summary>Finds the open document of a file, or returns null.</summary>
    IDocument? Find(string filePath);

    /// <summary>Opens a file, or returns its document when it is open already.</summary>
    /// <exception cref="IOException">The file cannot be read.</exception>
    /// <exception cref="UnauthorizedAccessException">The file cannot be read.</exception>
    Task<IDocument> OpenAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>Creates a document that has no file yet.</summary>
    IDocument CreateUntitled(string? languageId = null);

    /// <summary>Saves a document to its file; a document without a file needs <see cref="SaveAsAsync"/>.</summary>
    /// <exception cref="IOException">The file cannot be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The file cannot be written.</exception>
    Task SaveAsync(IDocument document, CancellationToken cancellationToken = default);

    /// <summary>Saves a document to another file, which it belongs to from then on.</summary>
    Task SaveAsAsync(IDocument document, string filePath, CancellationToken cancellationToken = default);

    /// <summary>Reads the document's file again, replacing the text; the change can be undone.</summary>
    Task ReloadAsync(IDocument document, CancellationToken cancellationToken = default);

    /// <summary>Closes a document without asking; unsaved changes are lost.</summary>
    void Close(IDocument document);

    /// <summary>Raised after a document opens.</summary>
    event EventHandler<DocumentEventArgs>? Opened;

    /// <summary>Raised after a document is saved.</summary>
    event EventHandler<DocumentEventArgs>? Saved;

    /// <summary>Raised after a document closes.</summary>
    event EventHandler<DocumentEventArgs>? Closed;

    /// <summary>Raised when the file of an open document changes on disk; unmodified documents have been reloaded already.</summary>
    event EventHandler<DocumentEventArgs>? ChangedOnDisk;
}

/// <summary>The document an event is about.</summary>
public sealed class DocumentEventArgs(IDocument document) : EventArgs
{
    public IDocument Document { get; } = document;
}
