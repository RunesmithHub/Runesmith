namespace Runesmith.Sdk.Commands;

/// <summary>The ids of Runesmith's own commands, for plugins that run them or place them in menus.</summary>
public static class CommandIds
{
    public const string NewProject = "file.newProject";
    public const string OpenFolder = "file.openFolder";
    public const string OpenFile = "file.openFile";
    public const string NewFile = "file.new";
    public const string Save = "file.save";
    public const string SaveAs = "file.saveAs";
    public const string SaveAll = "file.saveAll";
    public const string Close = "file.close";
    public const string CloseFolder = "file.closeFolder";
    public const string Settings = "file.settings";
    public const string Exit = "file.exit";

    public const string Undo = "edit.undo";
    public const string Redo = "edit.redo";
    public const string Cut = "edit.cut";
    public const string Copy = "edit.copy";
    public const string Paste = "edit.paste";
    public const string Find = "edit.find";
    public const string Replace = "edit.replace";
    public const string FindInFiles = "edit.findInFiles";
    public const string ToggleComment = "edit.toggleComment";

    public const string SelectAll = "selection.selectAll";

    public const string CommandPalette = "view.commandPalette";
    public const string ToggleTheme = "view.toggleTheme";
    public const string ZoomIn = "view.zoomIn";
    public const string ZoomOut = "view.zoomOut";
    public const string ResetZoom = "view.resetZoom";
    public const string ResetLayout = "view.resetLayout";

    public const string SearchEverywhere = "go.searchEverywhere";
    public const string QuickOpen = "go.quickOpen";
    public const string GoToLine = "go.goToLine";
    public const string GoToDefinition = "go.goToDefinition";
    public const string Back = "go.back";
    public const string Forward = "go.forward";

    public const string TriggerCompletion = "editor.triggerCompletion";
    public const string TriggerSignatureHelp = "editor.triggerSignatureHelp";
    public const string RollbackLines = "editor.rollbackLines";
    public const string ShowCodeActions = "editor.showCodeActions";
    public const string Rename = "editor.rename";
    public const string FormatDocument = "editor.formatDocument";
    public const string FormatSelection = "editor.formatSelection";

    public const string Build = "build.build";
    public const string CancelBuild = "build.cancel";

    public const string Run = "run.run";
    public const string Debug = "run.debug";
    public const string Stop = "run.stop";
    public const string EditConfigurations = "run.editConfigurations";

    public const string ToggleBreakpoint = "debug.toggleBreakpoint";
    public const string Continue = "debug.continue";
    public const string Pause = "debug.pause";
    public const string StepOver = "debug.stepOver";
    public const string StepInto = "debug.stepInto";
    public const string StepOut = "debug.stepOut";
    public const string RestartDebugging = "debug.restart";
    public const string StopDebugging = "debug.stop";

    public const string Sdks = "tools.sdks";

    public const string CloneRepository = "vcs.clone";

    public const string KeyboardShortcuts = "help.keyboardShortcuts";
    public const string About = "help.about";
}
