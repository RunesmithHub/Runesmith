using System.Composition;
using Runesmith.Sdk.Editors;
using Runesmith.Sdk.Settings;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.Workspace;
using Runesmith.Shell.Editors;
using Runesmith.Shell.Editors.Custom;
using Runesmith.Shell.Session;
using Runesmith.Text;
using Runesmith.Workspace.Files;

namespace Runesmith.Shell.Services;

/// <summary>Opens and closes folders, keeping each folder's open files for the next time, and opens what the command line names.</summary>
[Export]
[Shared]
[method: ImportingConstructor]
public sealed class Workbench(
    IWorkspace workspace,
    EditorService editors,
    CustomEditorService customEditors,
    RecentFolders recent,
    ISettingsService settings,
    INotificationService notifications)
{
    /// <summary>Opens a folder, after asking about unsaved changes in the open one; returns whether it opened.</summary>
    public async Task<bool> OpenFolderAsync(string folder)
    {
        folder = Path.GetFullPath(folder);
        if (!Directory.Exists(folder))
        {
            recent.Remove(folder);
            notifications.Notify(NotificationKind.Error, "The folder does not exist", folder);
            return false;
        }

        if (!await CloseFolderAsync())
            return false;

        await workspace.OpenAsync(folder);
        if (settings.Get<bool>(SettingKeys.RestoreSession) && SessionStore.LoadWorkspace(folder) is { } session)
            await RestoreAsync(session);
        return true;
    }

    /// <summary>Closes the open folder and its editors, after asking about unsaved changes; returns whether it closed.</summary>
    public async Task<bool> CloseFolderAsync()
    {
        if (!await editors.ConfirmLosingChangesAsync())
            return false;

        SaveSession();
        await editors.CloseAllAsync();
        if (workspace.RootPath is not null)
            workspace.Close();
        return true;
    }

    /// <summary>Remembers the open files of the open folder.</summary>
    public void SaveSession()
    {
        if (workspace.RootPath is not { } root)
            return;

        var files = editors.Editors
            .Where(e => e.Document.FilePath is not null)
            .Select(e => new SessionFile(e.Document.FilePath!, e.CaretOffset) { Editor = customEditors.Catalog.DefaultFor(e.Document.FilePath!) == EditorProviderDefinition.TextEditorId ? null : EditorProviderDefinition.TextEditorId })
            .Concat(customEditors.Panels.Select(p => new SessionFile(p.FilePath, 0) { Editor = p.Entry.Definition.Id }))
            .ToList();
        SessionStore.SaveWorkspace(root, new WorkspaceSession(files, customEditors.Active?.FilePath ?? editors.ActiveEditor?.Document.FilePath));
    }

    /// <summary>Opens what the command line names: a folder, or files with an optional <c>:line</c> or <c>:line:column</c>.</summary>
    public async Task OpenArgumentsAsync(IReadOnlyList<string> arguments, string workingDirectory)
    {
        foreach (var argument in arguments)
        {
            var (path, position) = ParseLocation(argument, workingDirectory);
            if (Directory.Exists(path))
            {
                await OpenFolderAsync(path);
            }
            else if (File.Exists(path))
            {
                await editors.OpenAsync(path, position);
            }
            else
            {
                notifications.Notify(NotificationKind.Warning, "Nothing to open", $"{path} does not exist.");
            }
        }
    }

    /// <summary>Splits <c>path:line:column</c>, with the line and column counted from one, into a full path and a position.</summary>
    public static (string Path, TextPosition? Position) ParseLocation(string argument, string workingDirectory)
    {
        var parts = argument.Split(':');
        var numbers = new List<int>();
        var end = parts.Length;
        while (end > 1 && numbers.Count < 2 && int.TryParse(parts[end - 1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number))
        {
            numbers.Insert(0, number);
            end--;
        }

        var path = string.Join(':', parts[..end]);
        path = Path.GetFullPath(path, workingDirectory);
        TextPosition? position = numbers.Count switch
        {
            0 => null,
            1 => new TextPosition(Math.Max(0, numbers[0] - 1), 0),
            _ => new TextPosition(Math.Max(0, numbers[0] - 1), Math.Max(0, numbers[1] - 1)),
        };
        return (path, position);
    }

    private async Task RestoreAsync(WorkspaceSession session)
    {
        foreach (var file in session.Files)
        {
            if (!File.Exists(file.Path))
                continue;

            if (file.Editor is { } editor && editor != EditorProviderDefinition.TextEditorId && customEditors.GetChoices(file.Path).Any(c => c.Id == editor))
            {
                await customEditors.OpenWithAsync(file.Path, editor, activate: false);
                continue;
            }

            var opened = file.Editor == EditorProviderDefinition.TextEditorId
                ? await editors.OpenInTextEditorAsync(file.Path, activate: false, anyContent: true)
                : await editors.OpenAsync(file.Path, activate: false);
            if (opened is { } text)
                text.CaretOffset = Math.Clamp(file.Caret, 0, text.Document.Buffer.Current.Length);
        }

        if (session.Active is { } active && File.Exists(active))
            await editors.OpenAsync(active);
    }
}
