using System.Globalization;
using System.Text;

namespace Runesmith.Shell.Editors.FileOperations;

/// <summary>The system's trash, where deleted files can be restored from.</summary>
internal interface ITrash
{
    /// <summary>Moves a file or folder to the trash and returns where it went, or null when this system or drive has no trash it can use.</summary>
    /// <param name="originalPath">The path the trash shows it was deleted from, when that differs from <paramref name="path"/>.</param>
    string? TryMoveToTrash(string path, string? originalPath = null);

    /// <summary>Moves a file or folder back out of the trash.</summary>
    void Restore(string trashedPath, string path);
}

/// <summary>Creates the trash of this system.</summary>
internal static class SystemTrash
{
    public static ITrash Create()
    {
        if (OperatingSystem.IsWindows())
            return NoTrash.Instance;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS())
            return new FolderTrash(Path.Combine(home, ".Trash"));

        var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdg ? xdg : Path.Combine(home, ".local", "share");
        return new FreedesktopTrash(Path.Combine(data, "Trash"));
    }
}

/// <summary>No trash: deleted files are kept by Runesmith until the edit can no longer be undone.</summary>
internal sealed class NoTrash : ITrash
{
    public static NoTrash Instance { get; } = new();

    public string? TryMoveToTrash(string path, string? originalPath = null) => null;

    public void Restore(string trashedPath, string path) => FileMoves.Move(trashedPath, path);
}

/// <summary>A trash that is a plain folder, such as <c>~/.Trash</c> on macOS.</summary>
internal sealed class FolderTrash(string folder) : ITrash
{
    public string? TryMoveToTrash(string path, string? originalPath = null)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var target = UniquePath(folder, Path.GetFileName(originalPath ?? path), name => FileMoves.Exists(Path.Combine(folder, name)));
            FileMoves.Move(path, target);
            return target;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Restore(string trashedPath, string path) => FileMoves.Move(trashedPath, path);

    internal static string UniquePath(string folder, string name, Func<string, bool> isTaken)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (var i = 1; ; i++)
        {
            var candidate = i == 1 ? name : $"{stem}.{i}{extension}";
            if (!isTaken(candidate))
                return Path.Combine(folder, candidate);
        }
    }
}

/// <summary>The trash of Linux desktops, which file managers can restore from: the file goes to <c>files</c> and a note of where it was to
/// <c>info</c>. Only files on the trash's own drive go there; for others the edit keeps them until it can no longer be undone.</summary>
internal sealed class FreedesktopTrash(string folder) : ITrash
{
    public string? TryMoveToTrash(string path, string? originalPath = null)
    {
        var files = Path.Combine(folder, "files");
        var info = Path.Combine(folder, "info");
        string? infoPath = null;
        try
        {
            Directory.CreateDirectory(files);
            Directory.CreateDirectory(info);
            if (!SameDrive(path, folder))
                return null;

            var name = Path.GetFileName(originalPath ?? path);
            var note = $"[Trash Info]\nPath={Escape(originalPath ?? path)}\nDeletionDate={DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)}\n";
            for (var i = 1; infoPath is null; i++)
            {
                var candidate = i == 1 ? name : $"{Path.GetFileNameWithoutExtension(name)}.{i}{Path.GetExtension(name)}";
                var reserved = Path.Combine(info, candidate + ".trashinfo");
                if (FileMoves.Exists(Path.Combine(files, candidate)))
                    continue;
                try
                {
                    using var stream = new FileStream(reserved, FileMode.CreateNew, FileAccess.Write);
                    stream.Write(Encoding.UTF8.GetBytes(note));
                    infoPath = reserved;
                }
                catch (IOException) when (File.Exists(reserved))
                {
                    // Another program trashed a file with this name at the same moment.
                }
            }

            var target = Path.Combine(files, Path.GetFileNameWithoutExtension(infoPath));
            FileMoves.Move(path, target);
            return target;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (infoPath is not null)
                File.Delete(infoPath);
            return null;
        }
    }

    public void Restore(string trashedPath, string path)
    {
        FileMoves.Move(trashedPath, path);
        File.Delete(Path.Combine(folder, "info", Path.GetFileName(trashedPath) + ".trashinfo"));
    }

    private static string Escape(string path) => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    // A rename keeps the file on its drive; Unix tells drives apart by their mount points, which DriveInfo finds by the longest prefix.
    private static bool SameDrive(string path, string folder) =>
        string.Equals(MountOf(path), MountOf(folder), StringComparison.Ordinal);

    private static string MountOf(string path)
    {
        var full = Path.GetFullPath(path);
        return DriveInfo.GetDrives()
            .Select(drive => drive.RootDirectory.FullName)
            .Where(root => full.StartsWith(root, StringComparison.Ordinal) && (full.Length == root.Length || root.EndsWith('/') || full[root.Length] == '/'))
            .MaxBy(root => root.Length) ?? "/";
    }
}
