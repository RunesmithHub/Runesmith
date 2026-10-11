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

        Sync(CommandIds.NewFile, Strings.ShellNewFile, Strings.ShellCategoryFile, "file-plus", "Ctrl+N", editors.NewFile, menu: Menus.File, group: "1-new");
        Add(CommandIds.NewProject, Strings.ShellNewProject, Strings.ShellCategoryFile, "folder-plus", "Ctrl+Shift+N", () => newProject.Value.ShowAsync(), menu: Menus.File, group: "1-new");
        Add(CommandIds.OpenFile, Strings.ShellOpenFile, Strings.ShellCategoryFile, "file", "Ctrl+O", OpenFileAsync, menu: Menus.File, group: "1-new");
        Add(CommandIds.OpenFolder, Strings.ShellOpenFolder, Strings.ShellCategoryFile, "folder-open", "Ctrl+Shift+O", OpenFolderAsync, menu: Menus.File, group: "1-new");
        registry.AddMenuItem(new MenuItemDefinition(Menus.File, CommandIds.CloneRepository, "1-new", order++));
        Add(CommandIds.Save, Strings.ShellSave, Strings.ShellCategoryFile, "save", "Ctrl+S", () => customEditors.Active is { } custom ? customEditors.SaveAsync(custom) : editors.SaveAsync(Editor!.Document), () => HasCustomEditor() || HasEditor(), Menus.File, "2-save");
        Add(CommandIds.SaveAs, Strings.ShellSaveAs, Strings.ShellCategoryFile, null, "Ctrl+Shift+S", () => editors.SaveAsAsync(Editor!.Document), HasEditor, Menus.File, "2-save");
        Add(CommandIds.SaveAll, Strings.ShellSaveAll, Strings.ShellCategoryFile, null, "Ctrl+Alt+S", editors.SaveAllAsync, menu: Menus.File, group: "2-save");
        Add(CommandIds.Close, Strings.ShellCloseEditor, Strings.ShellCategoryFile, "x", "Ctrl+W | Ctrl+F4", () => customEditors.Active is { } custom ? customEditors.CloseAsync(custom.Id) : editors.CloseAsync(Editor!), () => HasCustomEditor() || HasEditor(), Menus.File, "3-close");
        Add(CommandIds.CloseFolder, Strings.ShellCloseFolder, Strings.ShellCategoryFile, null, null, workbench.CloseFolderAsync, () => workspace.RootPath is not null, Menus.File, "3-close");
        Sync(CommandIds.Settings, Strings.ShellSettings, Strings.ShellCategoryFile, "settings", "Ctrl+,", pages.ShowSettings, menu: Menus.File, group: "4-settings");
        Sync(CommandIds.Exit, Strings.ShellExit, Strings.ShellCategoryFile, null, "Ctrl+Q", shell.Close, menu: Menus.File, group: "5-exit");

        Sync(CommandIds.Undo, Strings.ShellUndo, Strings.ShellCategoryEdit, "undo", "Ctrl+Z", () => (shell.FocusedTextBox is { } box ? (Action)box.Undo : customEditors.Active is { } custom ? custom.Editor.Undo : Editor!.Undo)(), () => HasEditorOrText() || HasCustomEditor(), Menus.Edit, "1-undo");
        Sync(CommandIds.Redo, Strings.ShellRedo, Strings.ShellCategoryEdit, "redo", "Ctrl+Y | Ctrl+Shift+Z", () => (shell.FocusedTextBox is { } box ? (Action)box.Redo : customEditors.Active is { } custom ? custom.Editor.Redo : Editor!.Redo)(), () => HasEditorOrText() || HasCustomEditor(), Menus.Edit, "1-undo");
        Add(CommandIds.UndoWorkspaceEdit, Strings.ShellUndoWorkspaceEdit, Strings.ShellCategoryEdit, "undo", null, () => workspaceEdits.UndoAsync(), () => workspaceEdits.CanUndo, Menus.Edit, "1-undo",
            "Undo the last change a refactoring, code action, plugin or the Explorer made to files, with the files it created, renamed or deleted.");
        Add(CommandIds.RedoWorkspaceEdit, Strings.ShellRedoWorkspaceEdit, Strings.ShellCategoryEdit, "redo", null, () => workspaceEdits.RedoAsync(), () => workspaceEdits.CanRedo, Menus.Edit, "1-undo",
            "Make the last undone workspace edit again.");
        Add(CommandIds.Cut, Strings.ShellCut, Strings.ShellCategoryEdit, "cut", "Ctrl+X", () => shell.FocusedTextBox is { } box ? Run(box.Cut) : Editor!.CutAsync(), HasEditorOrText, Menus.Edit, "2-clipboard");
        Add(CommandIds.Copy, Strings.ShellCopy, Strings.ShellCategoryEdit, "copy", "Ctrl+C", () => shell.FocusedTextBox is { } box ? Run(box.Copy) : Editor!.CopyAsync(), HasEditorOrText, Menus.Edit, "2-clipboard");
        Add(CommandIds.Paste, Strings.ShellPaste, Strings.ShellCategoryEdit, "paste", "Ctrl+V", () => shell.FocusedTextBox is { } box ? Run(box.Paste) : Editor!.PasteAsync(), HasEditorOrText, Menus.Edit, "2-clipboard");
        Sync(CommandIds.Find, Strings.ShellFind, Strings.ShellCategoryEdit, "search", "Ctrl+F", () => Editor!.OpenFind(withReplace: false), HasEditor, Menus.Edit, "3-find");
        Sync(CommandIds.Replace, Strings.ShellReplace, Strings.ShellCategoryEdit, null, "Ctrl+H", () => Editor!.OpenFind(withReplace: true), HasEditor, Menus.Edit, "3-find");
        Sync(FindNext, Strings.ShellFindNext, Strings.ShellCategoryEdit, null, "F3", () => Editor!.FindNext(), HasEditor, Menus.Edit, "3-find");
        Sync(FindPrevious, Strings.ShellFindPrevious, Strings.ShellCategoryEdit, null, "Shift+F3", () => Editor!.FindPrevious(), HasEditor, Menus.Edit, "3-find");
        Sync(CommandIds.FindInFiles, Strings.ShellFindInFiles, Strings.ShellCategoryEdit, "search", "Ctrl+Shift+F", () => pages.ShowSearch(Editor?.Area.Selection is { IsEmpty: false } s && s.Span.Length < 200 ? Editor.Area.Snapshot.GetText(s.Span) : null), () => workspace.RootPath is not null, Menus.Edit, "3-find");
        Sync(CommandIds.ToggleComment, Strings.ShellToggleLineComment, Strings.ShellCategoryEdit, null, "Ctrl+/", () => Editor!.ToggleComment(), HasEditor, Menus.Edit, "4-lines");
        Add(CommandIds.ShowCodeActions, Strings.ShellShowCodeActions, Strings.ShellCategoryEditor, "lightbulb", "Alt+Enter", () => Editor!.ShowCodeActionsAsync(), HasEditor, Menus.Edit, "5-code",
            "Show the quick fixes and refactorings for the caret's line or the selection.");
        Add(CommandIds.Rename, Strings.ShellRenameSymbol, Strings.ShellCategoryEditor, "pen-line", "F2", () => Editor!.RenameAsync(), HasEditor, Menus.Edit, "5-code",
            "Rename the symbol at the caret everywhere it is used.");
        Add(CommandIds.FormatDocument, Strings.ShellFormatDocument, Strings.ShellCategoryEditor, null, "Shift+Alt+F", () => Editor!.FormatAsync(), HasEditor, Menus.Edit, "5-code",
            "Format the whole file with the formatter of its language.");
        Add(CommandIds.FormatSelection, Strings.ShellFormatSelection, Strings.ShellCategoryEditor, null, null, () => Editor!.FormatAsync(selectionOnly: true), () => Editor?.Area.Selection.IsEmpty == false,
            Menus.Edit, "5-code", "Format the selected lines with the formatter of their language.");

        Sync(CommandIds.SelectAll, Strings.ShellSelectAll, Strings.ShellCategorySelection, null, "Ctrl+A", () => (shell.FocusedTextBox is { } box ? (Action)box.SelectAll : Editor!.SelectAll)(), HasEditorOrText, Menus.Selection, "1-select");
        Sync(MoveLinesUp, Strings.ShellMoveLineUp, Strings.ShellCategorySelection, "arrow-up", "Alt+Up", () => Editor!.MoveLines(down: false), HasEditor, Menus.Selection, "2-lines");
        Sync(MoveLinesDown, Strings.ShellMoveLineDown, Strings.ShellCategorySelection, "arrow-down", "Alt+Down", () => Editor!.MoveLines(down: true), HasEditor, Menus.Selection, "2-lines");
        Sync(DuplicateLines, Strings.ShellDuplicateLine, Strings.ShellCategorySelection, "copy-plus", "Shift+Alt+Down", () => Editor!.DuplicateLines(), HasEditor, Menus.Selection, "2-lines");
        Sync(DeleteLines, Strings.ShellDeleteLine, Strings.ShellCategorySelection, "trash", "Ctrl+Shift+K", () => Editor!.DeleteLines(), HasEditor, Menus.Selection, "2-lines");
        Sync(Indent, Strings.ShellIndent, Strings.ShellCategorySelection, null, null, () => Editor!.Indent(), HasEditor, Menus.Selection, "3-indent");
        Sync(Outdent, Strings.ShellOutdent, Strings.ShellCategorySelection, null, null, () => Editor!.Outdent(), HasEditor, Menus.Selection, "3-indent");

        Add(CommandIds.CommandPalette, Strings.ShellCommandPalette, Strings.ShellCategoryView, "search", "Ctrl+K | Ctrl+Shift+P | F1", () => shell.ShowQuickPickAsync(">"), menu: Menus.View, group: "1-palette");
        Sync(CommandIds.ToggleTheme, Strings.ShellToggleLightAndDarkTheme, Strings.ShellCategoryView, "sun", "Ctrl+Shift+T", theme.Toggle, menu: Menus.View, group: "3-appearance");
        Sync(CommandIds.ZoomIn, Strings.ShellZoomIn, Strings.ShellCategoryView, "zoom-in", "Ctrl+= | Ctrl+Shift+=", () => editors.Zoom(1), menu: Menus.View, group: "3-appearance");
        Sync(CommandIds.ZoomOut, Strings.ShellZoomOut, Strings.ShellCategoryView, "zoom-out", "Ctrl+-", () => editors.Zoom(-1), menu: Menus.View, group: "3-appearance");
        Sync(CommandIds.ResetZoom, Strings.ShellResetZoom, Strings.ShellCategoryView, null, "Ctrl+0", editors.ResetZoom, menu: Menus.View, group: "3-appearance");
        Sync(CommandIds.ResetLayout, Strings.ShellResetLayout, Strings.ShellCategoryView, "layout", null, toolWindows.Reset, menu: Menus.View, group: "4-layout");

        Add(CommandIds.SearchEverywhere, Strings.ShellSearchEverywhere, Strings.ShellCategoryGo, "search", "Ctrl+Shift+A", shell.ShowSearchEverywhereAsync, menu: Menus.Go, group: "1-go",
            description: Strings.ShellSearchEverywhereDescription);
        Add(CommandIds.QuickOpen, Strings.ShellGoToFile, Strings.ShellCategoryGo, "file-code", "Ctrl+P", () => shell.ShowQuickPickAsync(), menu: Menus.Go, group: "1-go");
        Add(CommandIds.GoToLine, Strings.ShellGoToLine, Strings.ShellCategoryGo, null, "Ctrl+G", () => shell.ShowQuickPickAsync(":"), HasEditor, Menus.Go, "1-go");
        Add(CommandIds.GoToDefinition, Strings.ShellGoToDefinition, Strings.ShellCategoryGo, null, "F12", () => Editor!.GoToDefinitionAsync(), HasEditor, Menus.Go, "2-symbol");
        Add(CommandIds.Back, Strings.ShellBack, Strings.ShellCategoryGo, "arrow-left", "Alt+Left", editors.GoBackAsync, () => editors.CanGoBack, Menus.Go, "3-history");
        Add(CommandIds.Forward, Strings.ShellForward, Strings.ShellCategoryGo, "arrow-right", "Alt+Right", editors.GoForwardAsync, () => editors.CanGoForward, Menus.Go, "3-history");
        Sync(CommandIds.TriggerCompletion, Strings.ShellTriggerSuggestions, Strings.ShellCategoryEditor, "sparkles", "Ctrl+Space", () => Editor!.TriggerCompletion(), HasEditor);
        Sync(CommandIds.TriggerSignatureHelp, Strings.ShellShowParameterHints, Strings.ShellCategoryEditor, null, "Ctrl+Shift+Space", () => Editor!.TriggerSignatureHelp(), HasEditor);
        Sync(NextChange, Strings.ShellNextChange, Strings.ShellCategoryEditor, "arrow-down", "F7", () => Editor!.GoToChange(next: true), HasChanges, Menus.Go, "4-changes",
            "Move to the next lines that differ from the base, such as the last commit.");
        Sync(PreviousChange, Strings.ShellPreviousChange, Strings.ShellCategoryEditor, "arrow-up", "Shift+F7", () => Editor!.GoToChange(next: false), HasChanges, Menus.Go, "4-changes");
        Sync(RollbackLines, Strings.ShellRollbackLines, Strings.ShellCategoryEditor, "undo", "Ctrl+Alt+Z", () => Editor!.RollbackChanges(), () => Editor?.Area.ChangesInSelection().Count > 0, Menus.Edit, "4-lines",
            "Put the changed lines at the caret or in the selection back as they are in the base.");
        Sync(ShowChanges, Strings.ShellShowChanges, Strings.ShellCategoryEditor, "git-compare", null, () => editors.ShowChanges(Editor!), () => Editor?.Area.BaseText is not null, Menus.Go, "4-changes",
            "Show the file beside its base, such as the last commit.");

        Add(CommandIds.Build, Strings.ShellBuild, Strings.ShellCategoryBuild, "hammer", "Ctrl+Shift+B", () => runs.Value.BuildAsync(runConfigurations.Value.Selected),
            () => !runs.Value.IsBuilding && (build.CanBuild || runConfigurations.Value.Selected is not null), Menus.Build, "1-build",
            "Build the selected run configuration's project, or the open folder, such as its solution with dotnet build.");
        Sync(CommandIds.CancelBuild, Strings.ShellCancelBuild, Strings.ShellCategoryBuild, "x", "Ctrl+Break", () => runs.Value.CancelBuild(), () => runs.Value.IsBuilding, Menus.Build, "1-build");

        Sync(ShowWelcome, Strings.ShellWelcome, Strings.ShellCategoryHelp, "sparkles", null, pages.ShowWelcome, menu: Menus.Help, group: "1-start");
        Sync(CommandIds.KeyboardShortcuts, Strings.ShellKeyboardShortcuts, Strings.ShellCategoryHelp, "keyboard", null, pages.ShowKeyboardShortcuts, menu: Menus.Help, group: "1-start");
        Sync(Documentation, Strings.ShellDocumentation, Strings.ShellCategoryHelp, "external-link", null, () => Launcher.OpenUrl(DocumentationUrl), menu: Menus.Help, group: "2-docs");
        Add(CommandIds.About, Strings.ShellAboutRunesmith, Strings.ShellCategoryHelp, "info", null, pages.ShowAboutAsync, menu: Menus.Help, group: "3-about");
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
