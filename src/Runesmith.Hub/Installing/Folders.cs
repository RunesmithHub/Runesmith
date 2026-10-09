namespace Runesmith.Hub.Installing;

/// <summary>Moves and deletes plugin folders.</summary>
internal static class Folders
{
    /// <summary>Moves a folder, copying it when the target is on another drive.</summary>
    public static void Move(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        try
        {
            Directory.Move(source, target);
        }
        catch (IOException) when (Directory.Exists(source) && !Directory.Exists(target))
        {
            Copy(source, target);
            Directory.Delete(source, recursive: true);
        }
    }

    /// <summary>Deletes a folder if it exists; returns whether it is gone.</summary>
    public static bool TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void Copy(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var folder in Directory.EnumerateDirectories(source))
            Copy(folder, Path.Combine(target, Path.GetFileName(folder)));
    }
}
