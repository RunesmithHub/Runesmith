namespace Runesmith.Shell.Editors.FileOperations;

/// <summary>Keeps files that workspace edits delete or replace, so the edits can be undone, until they can no longer be.</summary>
internal sealed class FileStash(string folder, ITrash trash)
{
    private static readonly TimeSpan Abandoned = TimeSpan.FromDays(7);

    public ITrash Trash { get; } = trash;

    /// <summary>Moves a file or folder into the stash and returns where it is kept.</summary>
    public string Stash(string path)
    {
        var slot = Path.Combine(folder, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(slot);
        var kept = Path.Combine(slot, Path.GetFileName(path));
        try
        {
            FileMoves.Move(path, kept);
        }
        catch
        {
            TryDelete(slot);
            throw;
        }

        return kept;
    }

    /// <summary>Moves a kept file or folder back to a path and returns the folders it created for it.</summary>
    public static List<string> Restore(string kept, string path)
    {
        var created = FileMoves.Move(kept, path);
        TryDelete(Path.GetDirectoryName(kept)!);
        return created;
    }

    /// <summary>Lets go of a kept file or folder: to the trash, or deleted for good.</summary>
    public void Discard(string kept, string originalPath, bool useTrash)
    {
        try
        {
            if (!useTrash || Trash.TryMoveToTrash(kept, originalPath) is null)
                FileMoves.Delete(kept);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // What cannot be deleted now goes with the stashes of earlier sessions.
            return;
        }

        TryDelete(Path.GetDirectoryName(kept)!);
    }

    /// <summary>Deletes what earlier sessions kept and did not let go of, such as after a crash, once it is a week old.</summary>
    public static void DeleteAbandoned(string root)
    {
        try
        {
            foreach (var session in new DirectoryInfo(root).EnumerateDirectories())
            {
                if (DateTime.UtcNow - session.LastWriteTimeUtc > Abandoned)
                    session.Delete(recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Tried again next time.
        }
    }

    private static void TryDelete(string slot)
    {
        try
        {
            if (Directory.Exists(slot) && !Directory.EnumerateFileSystemEntries(slot).Any())
                Directory.Delete(slot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An empty folder left behind costs nothing.
        }
    }
}
