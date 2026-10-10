namespace Runesmith.Lsp;

/// <summary>The file operations servers can ask to hear of.</summary>
public enum FileOperationKind
{
    Create,
    Rename,
    Delete,
}

/// <summary>A file or folder that a file operation creates, renames or deletes, as the client tells servers of it.</summary>
/// <param name="Path">Its full path; for a rename, the path before it.</param>
/// <param name="NewPath">For a rename, the path after it.</param>
/// <param name="IsFolder">Whether it is a folder, or null when that is not known, such as once it is deleted; a server's filters for files and
/// for folders then both match it.</param>
public sealed record FileOperationTarget(string Path, string? NewPath = null, bool? IsFolder = null);
