using System.Text;
using Runesmith.Sdk.Languages;
using Runesmith.Workspace.Files;

namespace Runesmith.Shell.Editors.FileOperations;

/// <summary>What file actions need from the service that runs them.</summary>
internal interface IFileActionHost
{
    FileStash Stash { get; }

    /// <summary>Points the open documents of a file or folder that moved at the new path.</summary>
    void MoveDocuments(string from, string to);
}

/// <summary>One file operation of a workspace edit, made so it can be undone and redone.</summary>
internal abstract class FileAction(IFileActionHost host)
{
    protected IFileActionHost Host { get; } = host;

    public bool IsDone { get; private set; }

    /// <summary>Gets the operation as made, for participants and summaries.</summary>
    public abstract FileOperation Operation { get; }

    /// <summary>Gets the operation that undoing makes.</summary>
    public abstract FileOperation Reverse { get; }

    /// <summary>Gets the paths it changes, for <c>FilesChangedMessage</c>.</summary>
    public abstract IReadOnlyList<string> Paths { get; }

    /// <exception cref="IOException">The files could not be changed.</exception>
    /// <exception cref="UnauthorizedAccessException">The files could not be changed.</exception>
    public void Do()
    {
        DoCore();
        IsDone = true;
    }

    /// <exception cref="IOException">The files could not be changed.</exception>
    /// <exception cref="UnauthorizedAccessException">The files could not be changed.</exception>
    public void Undo()
    {
        UndoCore();
        IsDone = false;
    }

    /// <summary>Says what keeps the action from being undone now, or null.</summary>
    public abstract string? CannotUndo();

    /// <summary>Says what keeps the action from being made again now, or null.</summary>
    public abstract string? CannotRedo();

    /// <summary>Lets go of the files kept for undo and redo, once the edit leaves the history.</summary>
    public abstract void Discard();

    protected abstract void DoCore();

    protected abstract void UndoCore();

    protected static string Name(string path) => Path.GetFileName(path);

    protected static bool IsCaseOnlyRename(string from, string to) =>
        !string.Equals(from, to, StringComparison.Ordinal) && PathComparison.Comparer.Equals(from, to);
}

internal sealed class CreateFileAction(IFileActionHost host, CreateFileOperation operation) : FileAction(host)
{
    private readonly byte[] _content = new UTF8Encoding(false).GetBytes(operation.Content);
    private string? _replaced;
    private string? _undone;
    private List<string> _folders = [];

    public override FileOperation Operation => operation;

    public override FileOperation Reverse => new DeleteFileOperation(operation.FilePath);

    public override IReadOnlyList<string> Paths => [operation.FilePath];

    public override string? CannotUndo() => File.Exists(operation.FilePath) ? null : $"{Name(operation.FilePath)} no longer exists.";

    public override string? CannotRedo() =>
        FileMoves.Exists(operation.FilePath) && !operation.Overwrite ? $"{Name(operation.FilePath)} exists again." : null;

    public override void Discard()
    {
        if (IsDone && _replaced is not null)
            Host.Stash.Discard(_replaced, operation.FilePath, useTrash: true);
        else if (!IsDone && _undone is not null)
            Host.Stash.Discard(_undone, operation.FilePath, useTrash: false);
        (_replaced, _undone) = (null, null);
    }

    protected override void DoCore()
    {
        var path = operation.FilePath;
        if (FileMoves.Exists(path))
            _replaced = Host.Stash.Stash(path);

        try
        {
            if (_undone is not null)
            {
                _folders = FileStash.Restore(_undone, path);
                _undone = null;
                return;
            }

            _folders = FileMoves.CreateParents(path);
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            stream.Write(_content);
        }
        catch
        {
            FileMoves.DeleteIfEmpty(_folders);
            if (_replaced is not null)
            {
                FileStash.Restore(_replaced, path);
                _replaced = null;
            }

            throw;
        }
    }

    protected override void UndoCore()
    {
        _undone = Host.Stash.Stash(operation.FilePath);
        if (_replaced is not null)
        {
            FileStash.Restore(_replaced, operation.FilePath);
            _replaced = null;
        }

        FileMoves.DeleteIfEmpty(_folders);
    }
}

internal sealed class RenameFileAction(IFileActionHost host, RenameFileOperation operation) : FileAction(host)
{
    private string? _replaced;
    private List<string> _folders = [];

    public override FileOperation Operation => operation;

    public override FileOperation Reverse => new RenameFileOperation(operation.NewPath, operation.OldPath);

    public override IReadOnlyList<string> Paths => [operation.OldPath, operation.NewPath];

    public override string? CannotUndo()
    {
        if (!FileMoves.Exists(operation.NewPath))
            return $"{Name(operation.NewPath)} no longer exists.";
        return !IsCaseOnlyRename(operation.OldPath, operation.NewPath) && FileMoves.Exists(operation.OldPath) ? $"{Name(operation.OldPath)} exists again." : null;
    }

    public override string? CannotRedo()
    {
        if (!FileMoves.Exists(operation.OldPath))
            return $"{Name(operation.OldPath)} no longer exists.";
        return !IsCaseOnlyRename(operation.OldPath, operation.NewPath) && FileMoves.Exists(operation.NewPath) && !operation.Overwrite
            ? $"{Name(operation.NewPath)} exists again."
            : null;
    }

    public override void Discard()
    {
        if (IsDone && _replaced is not null)
            Host.Stash.Discard(_replaced, operation.NewPath, useTrash: true);
        _replaced = null;
    }

    protected override void DoCore()
    {
        if (!IsCaseOnlyRename(operation.OldPath, operation.NewPath) && FileMoves.Exists(operation.NewPath))
            _replaced = Host.Stash.Stash(operation.NewPath);

        try
        {
            _folders = FileMoves.Move(operation.OldPath, operation.NewPath);
        }
        catch when (_replaced is not null)
        {
            FileStash.Restore(_replaced, operation.NewPath);
            _replaced = null;
            throw;
        }

        Host.MoveDocuments(operation.OldPath, operation.NewPath);
    }

    protected override void UndoCore()
    {
        FileMoves.Move(operation.NewPath, operation.OldPath);
        Host.MoveDocuments(operation.NewPath, operation.OldPath);
        if (_replaced is not null)
        {
            FileStash.Restore(_replaced, operation.NewPath);
            _replaced = null;
        }

        FileMoves.DeleteIfEmpty(_folders);
    }
}

internal sealed class DeleteFileAction(IFileActionHost host, DeleteFileOperation operation) : FileAction(host)
{
    private string? _trashed;
    private string? _kept;

    public override FileOperation Operation => operation;

    public override FileOperation Reverse => new CreateFileOperation(operation.Path);

    public override IReadOnlyList<string> Paths => [operation.Path];

    public override string? CannotUndo()
    {
        if (FileMoves.Exists(operation.Path))
            return $"{Name(operation.Path)} exists again.";
        return (_trashed ?? _kept) is { } held && !FileMoves.Exists(held) ? $"{Name(operation.Path)} is no longer in the trash." : null;
    }

    public override string? CannotRedo() => FileMoves.Exists(operation.Path) ? null : $"{Name(operation.Path)} no longer exists.";

    public override void Discard()
    {
        if (IsDone && _kept is not null)
            Host.Stash.Discard(_kept, operation.Path, operation.UseTrash);
        (_trashed, _kept) = (null, null);
    }

    protected override void DoCore()
    {
        _trashed = operation.UseTrash ? Host.Stash.Trash.TryMoveToTrash(operation.Path) : null;
        if (_trashed is null)
            _kept = Host.Stash.Stash(operation.Path);
    }

    protected override void UndoCore()
    {
        if (_trashed is not null)
            Host.Stash.Trash.Restore(_trashed, operation.Path);
        else if (_kept is not null)
            FileStash.Restore(_kept, operation.Path);
        (_trashed, _kept) = (null, null);
    }
}
