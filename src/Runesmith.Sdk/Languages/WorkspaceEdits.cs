using Runesmith.Text;

namespace Runesmith.Sdk.Languages;

/// <summary>Changes to one file's text.</summary>
/// <param name="FilePath">The file's full path. In an edit with <see cref="WorkspaceEdit.FileOperations"/>, a file that is renamed may be
/// named by its old or its new path, and a file that is created by its path.</param>
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

    /// <summary>Gets the files and folders to create, rename or delete, in order, before the text changes in <see cref="Documents"/> are
    /// made.</summary>
    /// <remarks>Added in plugin API 0.1.2. Operations on paths outside the open folder need the <c>filesystem</c> capability.</remarks>
    public IReadOnlyList<FileOperation> FileOperations { get; init; } = [];

    /// <summary>Gets whether the edit changes nothing.</summary>
    public bool IsEmpty => FileOperations.Count == 0 && Documents.All(document => document.Changes.Count == 0);
}

/// <summary>A file or folder that a <see cref="WorkspaceEdit"/> creates, renames or deletes: a <see cref="CreateFileOperation"/>,
/// <see cref="RenameFileOperation"/> or <see cref="DeleteFileOperation"/>.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
public abstract record FileOperation
{
    private protected FileOperation()
    {
    }
}

/// <summary>Creates a file, and the folders it needs.</summary>
/// <param name="FilePath">The new file's full path.</param>
/// <remarks>Added in plugin API 0.1.2. By default the edit is not applied when the file exists.</remarks>
public sealed record CreateFileOperation(string FilePath) : FileOperation
{
    /// <summary>Gets the file's text, written as UTF-8 without a byte order mark; empty by default.</summary>
    public string Content { get; init; } = "";

    /// <summary>Gets whether to replace a file that exists.</summary>
    public bool Overwrite { get; init; }

    /// <summary>Gets whether to leave a file that exists as it is and go on; <see cref="Overwrite"/> wins when both are set.</summary>
    public bool IgnoreIfExists { get; init; }
}

/// <summary>Renames or moves a file or folder, and the open editors of the files in it follow.</summary>
/// <param name="OldPath">The full path of the file or folder.</param>
/// <param name="NewPath">Its new full path; missing folders are created.</param>
/// <remarks>Added in plugin API 0.1.2. By default the edit is not applied when something exists at <paramref name="NewPath"/>.</remarks>
public sealed record RenameFileOperation(string OldPath, string NewPath) : FileOperation
{
    /// <summary>Gets whether to replace a file that exists at the new path.</summary>
    public bool Overwrite { get; init; }

    /// <summary>Gets whether to skip the rename and go on when something exists at the new path; <see cref="Overwrite"/> wins when both are
    /// set.</summary>
    public bool IgnoreIfExists { get; init; }
}

/// <summary>Deletes a file or folder, and closes the editors of the files in it.</summary>
/// <param name="Path">The full path of the file or folder.</param>
/// <remarks>Added in plugin API 0.1.2. By default the edit is not applied when the path does not exist, or is a folder that is not
/// empty.</remarks>
public sealed record DeleteFileOperation(string Path) : FileOperation
{
    /// <summary>Gets whether to delete a folder with everything in it.</summary>
    public bool Recursive { get; init; }

    /// <summary>Gets whether to go on when the path does not exist.</summary>
    public bool IgnoreIfMissing { get; init; }

    /// <summary>Gets whether the deleted files go to the system's trash: on Linux the desktop trash, for files on its drive, and on macOS
    /// <c>~/.Trash</c>. Otherwise, or when false, Runesmith keeps them while the edit can be undone and then deletes them for good.</summary>
    public bool UseTrash { get; init; } = true;
}

/// <summary>When <see cref="IWorkspaceEditService"/> asks the user before applying an edit.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
public enum WorkspaceEditConfirmation
{
    /// <summary>Applies the edit without asking.</summary>
    Never,

    /// <summary>Asks when the edit touches more than <see cref="WorkspaceEditOptions.LargeEditThreshold"/> files, counting the files inside
    /// the folders it renames or deletes.</summary>
    WhenLarge,

    /// <summary>Always asks.</summary>
    Always,
}

/// <summary>How <see cref="IWorkspaceEditService.ApplyAsync(WorkspaceEdit, WorkspaceEditOptions, CancellationToken)"/> applies an edit.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
public sealed record WorkspaceEditOptions
{
    /// <summary>Gets what the edit does in a few words, such as "Move Order.cs to Models", shown when the user is asked and in the
    /// <b>Undo Workspace Edit</b> command; null describes it from its changes.</summary>
    public string? Label { get; init; }

    /// <summary>Gets when the user is asked first, with a summary of the changes.</summary>
    public WorkspaceEditConfirmation Confirmation { get; init; } = WorkspaceEditConfirmation.WhenLarge;

    /// <summary>Gets how many files an edit may create, rename or change before <see cref="WorkspaceEditConfirmation.WhenLarge"/>
    /// asks.</summary>
    public int LargeEditThreshold { get; init; } = 20;
}

/// <summary>Applies workspace edits: each file's changes become one step of that file's undo history, and files that are not open open in
/// editor tabs, so every change can be seen, undone and saved.</summary>
public interface IWorkspaceEditService
{
    /// <summary>Applies an edit on the UI thread. Nothing is changed, and the user is told why, when a file cannot be opened, is read-only, has
    /// changed since the edit was computed, or has overlapping changes.</summary>
    /// <returns>Whether the edit was applied.</returns>
    Task<bool> ApplyAsync(WorkspaceEdit edit, CancellationToken cancellationToken = default);

    /// <summary>Applies an edit, with its file operations, as one step: either all of it is applied or none of it, and
    /// <see cref="UndoAsync"/> takes back the files and the text together.</summary>
    /// <remarks>Added in plugin API 0.1.2. Before the files change, every <see cref="IFileOperationParticipant"/> is asked for text changes
    /// to make with them. Open editors follow renamed files, editors of deleted files close, and <c>FilesChangedMessage</c> is published.
    /// Nothing is changed when an operation's path exists or is missing against its options, a file to delete or replace has unsaved changes,
    /// the user declines, or the calling plugin did not declare the <c>filesystem</c> capability for a path outside the open folder.</remarks>
    /// <returns>Whether the edit was applied.</returns>
    Task<bool> ApplyAsync(WorkspaceEdit edit, WorkspaceEditOptions options, CancellationToken cancellationToken = default) =>
        ApplyAsync(edit, cancellationToken);

    /// <summary>Gets whether there is a workspace edit to undo.</summary>
    /// <remarks>Added in plugin API 0.1.2.</remarks>
    bool CanUndo => false;

    /// <summary>Gets whether there is an undone workspace edit to redo.</summary>
    /// <remarks>Added in plugin API 0.1.2.</remarks>
    bool CanRedo => false;

    /// <summary>Undoes the last workspace edit: its text changes, then its file operations, in reverse order. Nothing is changed, and the
    /// user is told why, when a file has been changed since in a way that conflicts.</summary>
    /// <remarks>Added in plugin API 0.1.2.</remarks>
    /// <returns>Whether the edit was undone.</returns>
    Task<bool> UndoAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    /// <summary>Applies the last undone workspace edit again.</summary>
    /// <remarks>Added in plugin API 0.1.2.</remarks>
    /// <returns>Whether the edit was applied again.</returns>
    Task<bool> RedoAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
}

/// <summary>Takes part in file operations that workspace edits make, such as updating a namespace when a file moves to another folder.
/// Export it with <c>[Export(typeof(IFileOperationParticipant))]</c>.</summary>
/// <remarks>Added in plugin API 0.1.2. Runesmith calls the participants at the same time, on a background thread, and waits for them at most
/// <see cref="TimeBudget"/> in all, after which it cancels the token and goes on without the late ones.</remarks>
public interface IFileOperationParticipant
{
    /// <summary>The time participants get, together, before and again after the files change.</summary>
    static TimeSpan TimeBudget { get; } = TimeSpan.FromSeconds(3);

    /// <summary>Called before the operations are made; returns text changes to make in the same step, such as to the moved file, or null.
    /// The changes may name a renamed file by its old or new path. File operations in the returned edit are ignored.</summary>
    Task<WorkspaceEdit?> WillApplyAsync(IReadOnlyList<FileOperation> operations, CancellationToken cancellationToken) =>
        Task.FromResult<WorkspaceEdit?>(null);

    /// <summary>Called after the operations were made, and after the edit is undone or redone, with the operations that were made: for an
    /// undo, the reverse ones, such as the rename back, or a delete for a file the edit created.</summary>
    Task DidApplyAsync(IReadOnlyList<FileOperation> operations, CancellationToken cancellationToken) => Task.CompletedTask;
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
