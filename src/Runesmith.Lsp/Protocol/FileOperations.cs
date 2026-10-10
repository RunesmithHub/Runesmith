namespace Runesmith.Lsp.Protocol;

/// <summary>What a server offers for the workspace as a whole.</summary>
public sealed record WorkspaceServerCapabilities(FileOperationOptions? FileOperations);

/// <summary>The file operations a server wants to hear of: a request before each, whose answer may be an edit to make first, and a
/// notification after it. Each names the files and folders it is for.</summary>
public sealed record FileOperationOptions(
    FileOperationRegistrationOptions? DidCreate,
    FileOperationRegistrationOptions? WillCreate,
    FileOperationRegistrationOptions? DidRename,
    FileOperationRegistrationOptions? WillRename,
    FileOperationRegistrationOptions? DidDelete,
    FileOperationRegistrationOptions? WillDelete);

/// <summary>The files and folders a server wants to hear of for one file operation: those any of the filters match.</summary>
public sealed record FileOperationRegistrationOptions(IReadOnlyList<FileOperationFilter>? Filters);

/// <summary>Files and folders by the scheme of their URI, such as <c>file</c>, and a glob.</summary>
public sealed record FileOperationFilter(string? Scheme, FileOperationPattern? Pattern);

/// <summary>A glob over full paths with <c>/</c> separators: <c>*</c> and <c>?</c> match within a name, <c>**</c> any number of folders,
/// <c>{a,b}</c> either part and <c>[0-9]</c> or <c>[!0-9]</c> a character.</summary>
/// <param name="Matches">A <see cref="FileOperationPatternKind"/>; null matches files and folders.</param>
public sealed record FileOperationPattern(string? Glob, string? Matches, FileOperationPatternOptions? Options);

public sealed record FileOperationPatternOptions(bool? IgnoreCase);

/// <summary>What a <see cref="FileOperationPattern"/> matches.</summary>
public static class FileOperationPatternKind
{
    public const string File = "file";
    public const string Folder = "folder";
}

/// <summary>The files and folders that are created, for <c>workspace/willCreateFiles</c> and <c>workspace/didCreateFiles</c>.</summary>
public sealed record CreateFilesParams(IReadOnlyList<FileCreate> Files);

public sealed record FileCreate(string Uri);

/// <summary>The files and folders that are renamed, for <c>workspace/willRenameFiles</c> and <c>workspace/didRenameFiles</c>; a folder
/// stands for the files in it.</summary>
public sealed record RenameFilesParams(IReadOnlyList<FileRename> Files);

public sealed record FileRename(string OldUri, string NewUri);

/// <summary>The files and folders that are deleted, for <c>workspace/willDeleteFiles</c> and <c>workspace/didDeleteFiles</c>.</summary>
public sealed record DeleteFilesParams(IReadOnlyList<FileDelete> Files);

public sealed record FileDelete(string Uri);
