using Runesmith.Sdk.Languages;
using Runesmith.Workspace.Files;

namespace Runesmith.Shell.Editors.FileOperations;

/// <summary>Where the text changes of a workspace edit go: a file that exists now, at <paramref name="CurrentPath"/>, or one the edit
/// creates; either way at <paramref name="FinalPath"/> once the file operations are made.</summary>
internal sealed record EditTarget(string? CurrentPath, string FinalPath, CreateFileOperation? Created);

/// <summary>The file operations of a workspace edit, checked against the disk as each one would find it, with full paths and without the
/// ones their options skip.</summary>
internal sealed class FileOperationPlan
{
    private readonly List<FileOperation> _operations;

    private FileOperationPlan(List<FileOperation> operations) => _operations = operations;

    public IReadOnlyList<FileOperation> Operations => _operations;

    /// <summary>Checks the operations; returns the plan, or why they cannot be made.</summary>
    public static (FileOperationPlan? Plan, string? Problem) Create(IReadOnlyList<FileOperation> operations)
    {
        var plan = new FileOperationPlan([]);
        foreach (var operation in operations)
        {
            var (kept, problem) = plan.Check(Normalize(operation));
            if (problem is not null)
                return (null, problem);
            if (kept is not null)
                plan._operations.Add(kept);
        }

        return (plan, null);
    }

    /// <summary>Gets every path an operation names, before and after it.</summary>
    public IEnumerable<string> Paths => _operations.SelectMany(operation => operation switch
    {
        CreateFileOperation create => [create.FilePath],
        RenameFileOperation rename => new[] { rename.OldPath, rename.NewPath },
        DeleteFileOperation delete => [delete.Path],
        _ => [],
    });

    /// <summary>Finds the file a text change names, by its path before or after the operations; null with the reason when it is gone.</summary>
    public (EditTarget? Target, string? Problem) Resolve(string path)
    {
        var full = PathComparison.Normalize(path);
        if (Exists(full, _operations.Count))
        {
            var (current, created) = Origin(full);
            return (new EditTarget(current, full, created), null);
        }

        if (!FileMoves.Exists(full))
            return (null, $"{Path.GetFileName(full)} does not exist.");

        var final = full;
        foreach (var operation in _operations)
        {
            switch (operation)
            {
                case RenameFileOperation rename when PathComparison.IsInside(final, rename.OldPath):
                    final = rename.NewPath + final[rename.OldPath.Length..];
                    break;
                case DeleteFileOperation delete when PathComparison.IsInside(final, delete.Path):
                    return (null, $"{Path.GetFileName(full)} is deleted by the same edit.");
                case CreateFileOperation create when PathComparison.Comparer.Equals(create.FilePath, final):
                    return (null, $"{Path.GetFileName(full)} is replaced by the same edit.");
            }
        }

        return (new EditTarget(full, final, null), null);
    }

    private (FileOperation? Kept, string? Problem) Check(FileOperation operation)
    {
        var now = _operations.Count;
        switch (operation)
        {
            case CreateFileOperation create:
                if (!Exists(create.FilePath, now))
                    return (create, null);
                if (IsFolder(create.FilePath, now))
                    return (null, $"{Path.GetFileName(create.FilePath)} is a folder.");
                if (create.Overwrite)
                    return (create, null);
                return create.IgnoreIfExists ? (null, null) : (null, $"{Path.GetFileName(create.FilePath)} already exists.");
            case RenameFileOperation rename:
                if (!Exists(rename.OldPath, now))
                    return (null, $"{Path.GetFileName(rename.OldPath)} does not exist.");
                if (string.Equals(rename.OldPath, rename.NewPath, StringComparison.Ordinal))
                    return (null, null);
                if (PathComparison.IsInside(rename.NewPath, rename.OldPath) && !PathComparison.Comparer.Equals(rename.NewPath, rename.OldPath))
                    return (null, $"{Path.GetFileName(rename.OldPath)} cannot move into itself.");
                if (PathComparison.Comparer.Equals(rename.NewPath, rename.OldPath) || !Exists(rename.NewPath, now))
                    return (rename, null);
                if (IsFolder(rename.NewPath, now))
                    return (null, $"{Path.GetFileName(rename.NewPath)} is a folder.");
                if (rename.Overwrite)
                    return (rename, null);
                return rename.IgnoreIfExists ? (null, null) : (null, $"{Path.GetFileName(rename.NewPath)} already exists.");
            case DeleteFileOperation delete:
                if (!Exists(delete.Path, now))
                    return delete.IgnoreIfMissing ? (null, null) : (null, $"{Path.GetFileName(delete.Path)} does not exist.");
                if (!delete.Recursive && IsFolder(delete.Path, now) && Directory.Exists(delete.Path) && Directory.EnumerateFileSystemEntries(delete.Path).Any())
                    return (null, $"The folder {Path.GetFileName(delete.Path)} is not empty.");
                return (delete, null);
            default:
                return (null, "The edit has an operation Runesmith does not know.");
        }
    }

    // Whether a path exists after the first `count` operations: it is traced back through them to the disk.
    private bool Exists(string path, int count) => Trace(path, count) is { } traced && (traced.Created || FileMoves.Exists(traced.Path));

    private bool IsFolder(string path, int count) =>
        Trace(path, count) is { } traced && (traced.Folder || (!traced.Created && Directory.Exists(traced.Path)));

    // Where a path after `count` operations was before them: on disk, created by an operation, or a folder an operation made for its file.
    private (string Path, bool Created, bool Folder)? Trace(string path, int count)
    {
        for (var i = count - 1; i >= 0; i--)
        {
            switch (_operations[i])
            {
                case CreateFileOperation create when PathComparison.Comparer.Equals(create.FilePath, path):
                    return (path, true, false);
                case CreateFileOperation create when PathComparison.IsInside(create.FilePath, path):
                    return (path, true, true);
                case RenameFileOperation rename when PathComparison.IsInside(path, rename.NewPath):
                    path = rename.OldPath + path[rename.NewPath.Length..];
                    break;
                case RenameFileOperation rename when PathComparison.IsInside(rename.NewPath, path):
                    return (path, true, true);
                case RenameFileOperation rename when PathComparison.IsInside(path, rename.OldPath):
                    return null;
                case DeleteFileOperation delete when PathComparison.IsInside(path, delete.Path):
                    return null;
            }
        }

        return (path, false, false);
    }

    private (string? Current, CreateFileOperation? Created) Origin(string path)
    {
        for (var i = _operations.Count - 1; i >= 0; i--)
        {
            switch (_operations[i])
            {
                case CreateFileOperation create when PathComparison.Comparer.Equals(create.FilePath, path):
                    return (null, create);
                case RenameFileOperation rename when PathComparison.IsInside(path, rename.NewPath):
                    path = rename.OldPath + path[rename.NewPath.Length..];
                    break;
            }
        }

        return (path, null);
    }

    private static FileOperation Normalize(FileOperation operation) => operation switch
    {
        CreateFileOperation create => create with { FilePath = PathComparison.Normalize(create.FilePath) },
        RenameFileOperation rename => rename with { OldPath = PathComparison.Normalize(rename.OldPath), NewPath = PathComparison.Normalize(rename.NewPath) },
        DeleteFileOperation delete => delete with { Path = PathComparison.Normalize(delete.Path) },
        _ => operation,
    };
}
