using System.Composition;
using Runesmith.Editor;
using Runesmith.Sdk.Build;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.Workspace;
using Runesmith.Shell.Editors;
using Runesmith.Shell.Editors.Custom;
using Runesmith.Shell.Pages;
using Runesmith.Shell.Running;
using Runesmith.Shell.Services;
using Runesmith.Shell.ToolWindows;

namespace Runesmith.Shell.Commands;

/// <summary>Runesmith's own commands and where they sit in the menus.</summary>
[Export(typeof(ICommandContributor))]
[method: ImportingConstructor]
public sealed class CoreCommands(
    ShellController shell,
    EditorService editors,
    Workbench workbench,
    IWorkspace workspace,
    IThemeService theme,
    IBuildService build,
    ToolWindowManager toolWindows,
    PageService pages,
    Lazy<NewProjectService> newProject,
    Lazy<RunService> runs,
    Lazy<IRunConfigurationService> runConfigurations,
    CustomEditorService customEditors,
    WorkspaceEditService workspaceEdits) : ICommandContributor
{
    public const string FindNext = "edit.findNext";
    public const string FindPrevious = "edit.findPrevious";
    public const string MoveLinesUp = "selection.moveLinesUp";
    public const string MoveLinesDown = "selection.moveLinesDown";
    public const string DuplicateLines = "selection.duplicateLines";
    public const string DeleteLines = "selection.deleteLines";
    public const string Indent = "selection.indent";
    public const string Outdent = "selection.outdent";
    public const string ShowWelcome = "help.welcome";
    public const string Documentation = "help.documentation";
    public const string NextChange = "editor.nextChange";
    public const string PreviousChange = "editor.previousChange";
    public const string RollbackLines = CommandIds.RollbackLines;
    public const string ShowChanges = "editor.showChanges";

    /// <summary>The address of Runesmith's documentation.</summary>
    public const string DocumentationUrl = "https://docs.runesmith.dev/";

    private TextEditor? Editor => editors.Active;

    public void Contribute(ICommandRegistry registry)
    {
        var order = 0;
        void Add(string id, string title, string category, string? icon, string? keys, Func<Task> run, Func<bool>? canRun = null, string? menu = null, string group = "default", string? description = null)
        {
            registry.Add(new CommandDefinition(id, title, category) { Icon = icon, KeyBinding = keys, Description = description }, _ => run(), canRun is null ? null : _ => canRun());
            if (menu is not null)
                registry.AddMenuItem(new MenuItemDefinition(menu, id, group, order++));
        }

        void Sync(string id, string title, string category, string? icon, string? keys, Action run, Func<bool>? canRun = null, string? menu = null, string group = "default", string? description = null) =>
            Add(id, title, category, icon, keys, () =>
            {
                run();
                return Task.CompletedTask;
            }, canRun, menu, group, description);

        bool HasEditor() => Editor is not null;
        bool HasEditorOrText() => Editor is not null || shell.FocusedTextBox is not null;
        bool HasCustomEditor() => customEditors.Active is not null;
        bool HasChanges() => Editor?.Area.LineChanges.Count > 0;

        Sync(CommandIds.NewFile, "New File", "File", "file-plus", "Ctrl+N", editors.NewFile, menu: Menus.File, group: "1-new");
        Add(CommandIds.NewProject, "New Project...", "File", "folder-plus", "Ctrl+Shift+N", () => newProject.Value.ShowAsync(), menu: Menus.File, group: "1-new");
        Add(CommandIds.OpenFile, "Open File...", "File", "file", "Ctrl+O", OpenFileAsync, menu: Menus.File, group: "1-new");
        Add(CommandIds.OpenFolder, "Open Folder...", "File", "folder-open", "Ctrl+Shift+O", OpenFolderAsync, menu: Menus.File, group: "1-new");
        registry.AddMenuItem(new MenuItemDefinition(Menus.File, CommandIds.CloneRepository, "1-new", order++));
        Add(CommandIds.Save, "Save", "File", "save", "Ctrl+S", () => customEditors.Active is { } custom ? customEditors.SaveAsync(custom) : editors.SaveAsync(Editor!.Document), () => HasCustomEditor() || HasEditor(), Menus.File, "2-save");
        Add(CommandIds.SaveAs, "Save As...", "File", null, "Ctrl+Shift+S", () => editors.SaveAsAsync(Editor!.Document), HasEditor, Menus.File, "2-save");
        Add(CommandIds.SaveAll, "Save All", "File", null, "Ctrl+Alt+S", editors.SaveAllAsync, menu: Menus.File, group: "2-save");
        Add(CommandIds.Close, "Close Editor", "File", "x", "Ctrl+W | Ctrl+F4", () => customEditors.Active is { } custom ? customEditors.CloseAsync(custom.Id) : editors.CloseAsync(Editor!), () => HasCustomEditor() || HasEditor(), Menus.File, "3-close");
        Add(CommandIds.CloseFolder, "Close Folder", "File", null, null, workbench.CloseFolderAsync, () => workspace.RootPath is not null, Menus.File, "3-close");
        Sync(CommandIds.Settings, "Settings", "File", "settings", "Ctrl+,", pages.ShowSettings, menu: Menus.File, group: "4-settings");
        Sync(CommandIds.Exit, "Exit", "File", null, "Ctrl+Q", shell.Close, menu: Menus.File, group: "5-exit");

        Sync(CommandIds.Undo, "Undo", "Edit", "undo", "Ctrl+Z", () => (shell.FocusedTextBox is { } box ? (Action)box.Undo : customEditors.Active is { } custom ? custom.Editor.Undo : Editor!.Undo)(), () => HasEditorOrText() || HasCustomEditor(), Menus.Edit, "1-undo");
        Sync(CommandIds.Redo, "Redo", "Edit", "redo", "Ctrl+Y | Ctrl+Shift+Z", () => (shell.FocusedTextBox is { } box ? (Action)box.Redo : customEditors.Active is { } custom ? custom.Editor.Redo : Editor!.Redo)(), () => HasEditorOrText() || HasCustomEditor(), Menus.Edit, "1-undo");
        Add(CommandIds.UndoWorkspaceEdit, "Undo Workspace Edit", "Edit", "undo", null, () => workspaceEdits.UndoAsync(), () => workspaceEdits.CanUndo, Menus.Edit, "1-undo",
            "Undo the last change a refactoring, code action or plugin made to files, with the files it created, renamed or deleted.");
        Add(CommandIds.RedoWorkspaceEdit, "Redo Workspace Edit", "Edit", "redo", null, () => workspaceEdits.RedoAsync(), () => workspaceEdits.CanRedo, Menus.Edit, "1-undo",
            "Make the last undone workspace edit again.");
        Add(CommandIds.Cut, "Cut", "Edit", "cut", "Ctrl+X", () => shell.FocusedTextBox is { } box ? Run(box.Cut) : Editor!.CutAsync(), HasEditorOrText, Menus.Edit, "2-clipboard");
        Add(CommandIds.Copy, "Copy", "Edit", "copy", "Ctrl+C", () => shell.FocusedTextBox is { } box ? Run(box.Copy) : Editor!.CopyAsync(), HasEditorOrText, Menus.Edit, "2-clipboard");
        Add(CommandIds.Paste, "Paste", "Edit", "paste", "Ctrl+V", () => shell.FocusedTextBox is { } box ? Run(box.Paste) : Editor!.PasteAsync(), HasEditorOrText, Menus.Edit, "2-clipboard");
        Sync(CommandIds.Find, "Find", "Edit", "search", "Ctrl+F", () => Editor!.OpenFind(withReplace: false), HasEditor, Menus.Edit, "3-find");
        Sync(CommandIds.Replace, "Replace", "Edit", null, "Ctrl+H", () => Editor!.OpenFind(withReplace: true), HasEditor, Menus.Edit, "3-find");
        Sync(FindNext, "Find Next", "Edit", null, "F3", () => Editor!.FindNext(), HasEditor, Menus.Edit, "3-find");
        Sync(FindPrevious, "Find Previous", "Edit", null, "Shift+F3", () => Editor!.FindPrevious(), HasEditor, Menus.Edit, "3-find");
        Sync(CommandIds.FindInFiles, "Find in Files", "Edit", "search", "Ctrl+Shift+F", () => pages.ShowSearch(Editor?.Area.Selection is { IsEmpty: false } s && s.Span.Length < 200 ? Editor.Area.Snapshot.GetText(s.Span) : null), () => workspace.RootPath is not null, Menus.Edit, "3-find");
        Sync(CommandIds.ToggleComment, "Toggle Line Comment", "Edit", null, "Ctrl+/", () => Editor!.ToggleComment(), HasEditor, Menus.Edit, "4-lines");
        Add(CommandIds.ShowCodeActions, "Show Code Actions", "Editor", "lightbulb", "Alt+Enter", () => Editor!.ShowCodeActionsAsync(), HasEditor, Menus.Edit, "5-code",
            "Show the quick fixes and refactorings for the caret's line or the selection.");
        Add(CommandIds.Rename, "Rename Symbol", "Editor", "pen-line", "F2", () => Editor!.RenameAsync(), HasEditor, Menus.Edit, "5-code",
            "Rename the symbol at the caret everywhere it is used.");
        Add(CommandIds.FormatDocument, "Format Document", "Editor", null, "Shift+Alt+F", () => Editor!.FormatAsync(), HasEditor, Menus.Edit, "5-code",
            "Format the whole file with the formatter of its language.");
        Add(CommandIds.FormatSelection, "Format Selection", "Editor", null, null, () => Editor!.FormatAsync(selectionOnly: true), () => Editor?.Area.Selection.IsEmpty == false,
            Menus.Edit, "5-code", "Format the selected lines with the formatter of their language.");

        Sync(CommandIds.SelectAll, "Select All", "Selection", null, "Ctrl+A", () => (shell.FocusedTextBox is { } box ? (Action)box.SelectAll : Editor!.SelectAll)(), HasEditorOrText, Menus.Selection, "1-select");
        Sync(MoveLinesUp, "Move Line Up", "Selection", "arrow-up", "Alt+Up", () => Editor!.MoveLines(down: false), HasEditor, Menus.Selection, "2-lines");
        Sync(MoveLinesDown, "Move Line Down", "Selection", "arrow-down", "Alt+Down", () => Editor!.MoveLines(down: true), HasEditor, Menus.Selection, "2-lines");
        Sync(DuplicateLines, "Duplicate Line", "Selection", "copy-plus", "Shift+Alt+Down", () => Editor!.DuplicateLines(), HasEditor, Menus.Selection, "2-lines");
        Sync(DeleteLines, "Delete Line", "Selection", "trash", "Ctrl+Shift+K", () => Editor!.DeleteLines(), HasEditor, Menus.Selection, "2-lines");
        Sync(Indent, "Indent", "Selection", null, null, () => Editor!.Indent(), HasEditor, Menus.Selection, "3-indent");
        Sync(Outdent, "Outdent", "Selection", null, null, () => Editor!.Outdent(), HasEditor, Menus.Selection, "3-indent");

        Add(CommandIds.CommandPalette, "Command Palette", "View", "search", "Ctrl+K | Ctrl+Shift+P | F1", () => shell.ShowQuickPickAsync(">"), menu: Menus.View, group: "1-palette");
        Sync(CommandIds.ToggleTheme, "Toggle Light and Dark Theme", "View", "sun", "Ctrl+Shift+T", theme.Toggle, menu: Menus.View, group: "3-appearance");
        Sync(CommandIds.ZoomIn, "Zoom In", "View", "zoom-in", "Ctrl+= | Ctrl+Shift+=", () => editors.Zoom(1), menu: Menus.View, group: "3-appearance");
        Sync(CommandIds.ZoomOut, "Zoom Out", "View", "zoom-out", "Ctrl+-", () => editors.Zoom(-1), menu: Menus.View, group: "3-appearance");
        Sync(CommandIds.ResetZoom, "Reset Zoom", "View", null, "Ctrl+0", editors.ResetZoom, menu: Menus.View, group: "3-appearance");
        Sync(CommandIds.ResetLayout, "Reset Layout", "View", "layout", null, toolWindows.Reset, menu: Menus.View, group: "4-layout");

        Add(CommandIds.SearchEverywhere, "Search Everywhere", "Go", "search", "Ctrl+Shift+A", shell.ShowSearchEverywhereAsync, menu: Menus.Go, group: "1-go",
            description: "Search files, commands and settings in one list; pressing Shift twice opens it too.");
        Add(CommandIds.QuickOpen, "Go to File...", "Go", "file-code", "Ctrl+P", () => shell.ShowQuickPickAsync(), menu: Menus.Go, group: "1-go");
        Add(CommandIds.GoToLine, "Go to Line...", "Go", null, "Ctrl+G", () => shell.ShowQuickPickAsync(":"), HasEditor, Menus.Go, "1-go");
        Add(CommandIds.GoToDefinition, "Go to Definition", "Go", null, "F12", () => Editor!.GoToDefinitionAsync(), HasEditor, Menus.Go, "2-symbol");
        Add(CommandIds.Back, "Back", "Go", "arrow-left", "Alt+Left", editors.GoBackAsync, () => editors.CanGoBack, Menus.Go, "3-history");
        Add(CommandIds.Forward, "Forward", "Go", "arrow-right", "Alt+Right", editors.GoForwardAsync, () => editors.CanGoForward, Menus.Go, "3-history");
        Sync(CommandIds.TriggerCompletion, "Trigger Suggestions", "Editor", "sparkles", "Ctrl+Space", () => Editor!.TriggerCompletion(), HasEditor);
        Sync(CommandIds.TriggerSignatureHelp, "Show Parameter Hints", "Editor", null, "Ctrl+Shift+Space", () => Editor!.TriggerSignatureHelp(), HasEditor);
        Sync(NextChange, "Next Change", "Editor", "arrow-down", "F7", () => Editor!.GoToChange(next: true), HasChanges, Menus.Go, "4-changes",
            "Move to the next lines that differ from the base, such as the last commit.");
        Sync(PreviousChange, "Previous Change", "Editor", "arrow-up", "Shift+F7", () => Editor!.GoToChange(next: false), HasChanges, Menus.Go, "4-changes");
        Sync(RollbackLines, "Rollback Lines", "Editor", "undo", "Ctrl+Alt+Z", () => Editor!.RollbackChanges(), () => Editor?.Area.ChangesInSelection().Count > 0, Menus.Edit, "4-lines",
            "Put the changed lines at the caret or in the selection back as they are in the base.");
        Sync(ShowChanges, "Show Changes", "Editor", "git-compare", null, () => editors.ShowChanges(Editor!), () => Editor?.Area.BaseText is not null, Menus.Go, "4-changes",
            "Show the file beside its base, such as the last commit.");

        Add(CommandIds.Build, "Build", "Build", "hammer", "Ctrl+Shift+B", () => runs.Value.BuildAsync(runConfigurations.Value.Selected),
            () => !runs.Value.IsBuilding && (build.CanBuild || runConfigurations.Value.Selected is not null), Menus.Build, "1-build",
            "Build the selected run configuration's project, or the open folder, such as its solution with dotnet build.");
        Sync(CommandIds.CancelBuild, "Cancel Build", "Build", "x", "Ctrl+Break", () => runs.Value.CancelBuild(), () => runs.Value.IsBuilding, Menus.Build, "1-build");

        Sync(ShowWelcome, "Welcome", "Help", "sparkles", null, pages.ShowWelcome, menu: Menus.Help, group: "1-start");
        Sync(CommandIds.KeyboardShortcuts, "Keyboard Shortcuts", "Help", "keyboard", null, pages.ShowKeyboardShortcuts, menu: Menus.Help, group: "1-start");
        Sync(Documentation, "Documentation", "Help", "external-link", null, () => Launcher.OpenUrl(DocumentationUrl), menu: Menus.Help, group: "2-docs");
        Add(CommandIds.About, "About Runesmith", "Help", "info", null, pages.ShowAboutAsync, menu: Menus.Help, group: "3-about");
    }

    private static Task Run(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    private async Task OpenFileAsync()
    {
        if (await shell.PickFileToOpenAsync("Open File", workspace.RootPath) is { } path)
            await editors.OpenAsync(path);
    }

    private async Task OpenFolderAsync()
    {
        if (await shell.PickFolderAsync("Open Folder", workspace.RootPath) is { } folder)
            await workbench.OpenFolderAsync(folder);
    }
}
