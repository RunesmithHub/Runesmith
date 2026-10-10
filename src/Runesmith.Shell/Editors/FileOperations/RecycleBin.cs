using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using Runesmith.Workspace.Files;

namespace Runesmith.Shell.Editors.FileOperations;

/// <summary>The Windows Recycle Bin, which File Explorer restores from. Only files on local fixed drives go there; for others the edit keeps
/// them until it can no longer be undone.</summary>
/// <param name="canRecycle">Gets whether a path is on a drive with a Recycle Bin.</param>
/// <param name="folderOf">Gets the user's Recycle Bin folder on a path's drive, which keeps each item as <c>$R</c> and an id beside its
/// record, <c>$I</c> and the same id.</param>
/// <param name="recycle">Moves a path to the Recycle Bin, or returns false when the system refused.</param>
internal sealed class RecycleBin(Func<string, bool> canRecycle, Func<string, string?> folderOf, Func<string, bool> recycle) : ITrash
{
    private const int RecordHeader = 24;

    [SupportedOSPlatform("windows")]
    public static RecycleBin Create() =>
        new(path => CanRecycle(path, root => new DriveInfo(root).DriveType, Environment.Is64BitProcess), FolderOf, Shell.Recycle);

    public string? TryMoveToTrash(string path, string? originalPath = null)
    {
        // The Recycle Bin puts an item back where it was deleted from, so a file Runesmith kept elsewhere would not go home.
        if ((originalPath is not null && !PathComparison.Comparer.Equals(originalPath, path)) || !canRecycle(path) || folderOf(path) is not { } folder)
            return null;

        var before = Records(folder).ToHashSet(PathComparison.Comparer);
        if (!recycle(path) && FileMoves.Exists(path))
            return null;

        return Find(folder, before, path) ?? Path.Combine(folder, "$R" + Path.GetFileName(path));
    }

    public void Restore(string trashedPath, string path)
    {
        FileMoves.Move(trashedPath, path);
        var name = Path.GetFileName(trashedPath);
        if (!name.StartsWith("$R", StringComparison.Ordinal))
            return;

        try
        {
            File.Delete(Path.Combine(Path.GetDirectoryName(trashedPath)!, "$I" + name[2..]));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The Recycle Bin then lists an item it cannot restore, which emptying it removes.
        }
    }

    /// <summary>Gets whether a path can go to the Recycle Bin: a full path on a local fixed drive, from a 64-bit process.</summary>
    internal static bool CanRecycle(string path, Func<string, DriveType> driveType, bool is64BitProcess)
    {
        // Shell.FileOperationRequest has the layout of 64-bit Windows; 32-bit Windows packs it differently.
        if (!is64BitProcess || path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] is not ('\\' or '/'))
            return false;

        try
        {
            return driveType(path[..3]) == DriveType.Fixed;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Reads the path a recycled item came from out of its <c>$I</c> record: version 1, from Windows Vista, holds it in 520 bytes
    /// after the header, and version 2, from Windows 10, after its length in characters.</summary>
    internal static string? OriginalPath(ReadOnlySpan<byte> record)
    {
        if (record.Length < RecordHeader)
            return null;

        ReadOnlySpan<byte> name;
        switch (BinaryPrimitives.ReadInt64LittleEndian(record))
        {
            case 1:
                name = record[RecordHeader..Math.Min(record.Length, RecordHeader + 520)];
                break;
            case 2 when record.Length >= RecordHeader + 4:
                var length = BinaryPrimitives.ReadInt32LittleEndian(record[RecordHeader..]);
                if (length <= 0 || RecordHeader + 4 + ((long)length * 2) > record.Length)
                    return null;
                name = record.Slice(RecordHeader + 4, length * 2);
                break;
            default:
                return null;
        }

        var text = Encoding.Unicode.GetString(name);
        var end = text.IndexOf('\0', StringComparison.Ordinal);
        return end >= 0 ? text[..end] : text;
    }

    // Finds the item a delete just put in the Recycle Bin by the new record that names the path it came from.
    private static string? Find(string folder, HashSet<string> before, string path)
    {
        foreach (var record in Records(folder).Where(record => !before.Contains(record)))
        {
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(record);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (OriginalPath(bytes) is { } original && PathComparison.Comparer.Equals(original, path))
                return Path.Combine(folder, "$R" + Path.GetFileName(record)[2..]);
        }

        return null;
    }

    private static List<string> Records(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? [.. Directory.EnumerateFiles(folder, "$I*")] : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? FolderOf(string path)
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value is { } user ? Path.Combine(path[..3], "$Recycle.Bin", user) : null;
    }

    [SupportedOSPlatform("windows")]
    private static class Shell
    {
        private const uint Delete = 3;
        private const ushort Silent = 0x0004;
        private const ushort NoConfirmation = 0x0010;
        private const ushort AllowUndo = 0x0040;
        private const ushort NoErrorUi = 0x0400;
        private const ushort WantNukeWarning = 0x4000;

        public static bool Recycle(string path)
        {
            var request = new FileOperationRequest
            {
                Function = Delete,
                From = path + '\0',
                // The warning makes Windows ask before it deletes for good what it cannot recycle, such as a file larger than the Recycle Bin.
                Flags = AllowUndo | NoConfirmation | Silent | NoErrorUi | WantNukeWarning,
            };
            return SHFileOperationW(ref request) == 0 && !request.AnyOperationsAborted;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int SHFileOperationW(ref FileOperationRequest request);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct FileOperationRequest
        {
            public IntPtr Window;
            public uint Function;
            public string From;
            public string? To;
            public ushort Flags;
            [MarshalAs(UnmanagedType.Bool)]
            public bool AnyOperationsAborted;
            public IntPtr NameMappings;
            public string? ProgressTitle;
        }
    }
}
