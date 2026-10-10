using Runesmith.Lsp.Protocol;

namespace Runesmith.Lsp;

public sealed partial class LanguageClient
{
    private FileOperationFilters _fileOperationFilters = new(null);

    /// <summary>Asks the server for the changes to make before files and folders are created, renamed or deleted, sending the ones its filters
    /// for the operation match; null when they match none or the server has no changes.</summary>
    /// <exception cref="LspException">The server refused.</exception>
    public async Task<WorkspaceEdit?> WillChangeFilesAsync(FileOperationKind kind, IReadOnlyList<FileOperationTarget> files, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);
        var matched = _fileOperationFilters.Match(kind, before: true, files);
        return matched.Count == 0
            ? null
            : await RequestAsync<WorkspaceEdit>(Method(kind, before: true), Parameters(kind, matched), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Tells the server of files and folders that were created, renamed or deleted, the ones its filters for the operation match.</summary>
    public Task DidChangeFilesAsync(FileOperationKind kind, IReadOnlyList<FileOperationTarget> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var matched = _fileOperationFilters.Match(kind, before: false, files);
        return matched.Count == 0 ? Task.CompletedTask : NotifyAsync(Method(kind, before: false), Parameters(kind, matched));
    }

    private static string Method(FileOperationKind kind, bool before) => kind switch
    {
        FileOperationKind.Create => before ? "workspace/willCreateFiles" : "workspace/didCreateFiles",
        FileOperationKind.Rename => before ? "workspace/willRenameFiles" : "workspace/didRenameFiles",
        _ => before ? "workspace/willDeleteFiles" : "workspace/didDeleteFiles",
    };

    private static object Parameters(FileOperationKind kind, IReadOnlyList<FileOperationTarget> files) => kind switch
    {
        FileOperationKind.Create => new CreateFilesParams([.. files.Select(file => new FileCreate(LspUri.FromPath(file.Path)))]),
        FileOperationKind.Rename => new RenameFilesParams([.. files.Select(file => new FileRename(LspUri.FromPath(file.Path), LspUri.FromPath(file.NewPath ?? file.Path)))]),
        _ => new DeleteFilesParams([.. files.Select(file => new FileDelete(LspUri.FromPath(file.Path)))]),
    };
}
