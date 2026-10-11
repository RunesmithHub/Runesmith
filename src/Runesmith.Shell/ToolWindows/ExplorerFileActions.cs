using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Shell;
using Runesmith.Shell.Editors;

namespace Runesmith.Shell.ToolWindows;

/// <summary>The Explorer's file commands. Each makes its change as a workspace edit, so it is one step of the workspace's undo history, and
/// file operation participants and language servers take part in it.</summary>
internal sealed class ExplorerFileActions(WorkspaceEditService edits, IEditorService editors, INotificationService notifications)
{
    /// <summary>Creates a file in a folder and opens it, or creates a folder.</summary>
    public async Task CreateAsync(string parent, string name, bool folder)
    {
        var path = Path.Combine(parent, name);
        FileOperation operation = folder ? new CreateFolderOperation(path) : new CreateFileOperation(path);
        if (await ApplyAsync(operation, Strings.ToolsExplorerCreateFailed(name)) && !folder)
            await editors.OpenAsync(path);
    }

    /// <summary>Gives a file or folder a new name in the same folder.</summary>
    public Task RenameAsync(string path, string name) =>
        ApplyAsync(new RenameFileOperation(path, Path.Combine(Path.GetDirectoryName(path)!, name)), Strings.ToolsExplorerRenameFailed(Path.GetFileName(path)));

    /// <summary>Deletes a file, or a folder with everything in it, once the user agrees.</summary>
    public async Task DeleteAsync(string path, bool isFolder)
    {
        var name = Path.GetFileName(path);
        var question = isFolder ? Strings.ToolsExplorerDeleteFolder(name) : Strings.ToolsExplorerDeleteFile(name);
        if (await notifications.ConfirmAsync(Strings.ToolsExplorerDeleteTitle, question, Strings.ToolsExplorerDelete, isDestructive: true))
            await ApplyAsync(new DeleteFileOperation(path) { Recursive = isFolder }, Strings.ToolsExplorerDeleteFailed(name));
    }

    private async Task<bool> ApplyAsync(FileOperation operation, string failure)
    {
        var (applied, problem) = await edits.ApplyFileCommandAsync(new WorkspaceEdit([]) { FileOperations = [operation] });
        if (!applied && problem is not null)
            notifications.Notify(NotificationKind.Error, failure, problem);
        return applied;
    }
}
