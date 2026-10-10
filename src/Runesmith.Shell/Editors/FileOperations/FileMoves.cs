namespace Runesmith.Shell.Editors.FileOperations;

/// <summary>Moves files and folders, also between drives, where the system cannot rename them.</summary>
internal static class FileMoves
{
    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>Creates the folders a path needs and returns the ones it created, outermost first.</summary>
    public static List<string> CreateParents(string path)
    {
        var created = new List<string>();
        for (var folder = Path.GetDirectoryName(path); !string.IsNullOrEmpty(folder) && !Directory.Exists(folder); folder = Path.GetDirectoryName(folder))
            created.Insert(0, folder);
        foreach (var folder in created)
            Directory.CreateDirectory(folder);
        return created;
    }

    /// <summary>Deletes folders that <see cref="CreateParents"/> created, innermost first, as long as they are empty.</summary>
    public static void DeleteIfEmpty(IReadOnlyList<string> folders)
    {
        foreach (var folder in folders.Reverse())
        {
            try
            {
                if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                    Directory.Delete(folder);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return;
            }
        }
    }

    /// <summary>Moves a file or folder to a path where nothing is, creating the folders it needs, and returns the folders it created.</summary>
    /// <exception cref="IOException">The move failed.</exception>
    /// <exception cref="UnauthorizedAccessException">The move is not allowed.</exception>
    public static List<string> Move(string from, string to)
    {
        var created = CreateParents(to);
        try
        {
            if (!Directory.Exists(from))
                File.Move(from, to);
            else
                MoveFolder(from, to);
        }
        catch
        {
            DeleteIfEmpty(created);
            throw;
        }

        return created;
    }

    private static void MoveFolder(string from, string to)
    {
        try
        {
            Directory.Move(from, to);
        }
        catch (IOException) when (Directory.Exists(from) && !Exists(to))
        {
            // Folders cannot be renamed across drives, so they are copied.
            Copy(from, to);
            Directory.Delete(from, recursive: true);
        }
    }

    /// <summary>Deletes a file or folder for good.</summary>
    public static void Delete(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        else if (File.Exists(path))
            File.Delete(path);
    }

    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var folder in Directory.EnumerateDirectories(from))
            Copy(folder, Path.Combine(to, Path.GetFileName(folder)));
    }
}
