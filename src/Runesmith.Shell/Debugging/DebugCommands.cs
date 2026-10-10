using System.Composition;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Running;
using Runesmith.Sdk.ToolWindows;
using Runesmith.Shell.Editors;
using Runesmith.Shell.Running;
using Runesmith.Shell.Services;
using Runesmith.Shell.Views;

namespace Runesmith.Shell.Debugging;

/// <summary>The debugger's commands: breakpoints, stepping, continuing, pausing, restarting and stopping, and watches; in the Run menu and the
/// editor's context menu.</summary>
[Export(typeof(ICommandContributor))]
[Export]
[Shared]
[method: ImportingConstructor]
public sealed class DebugCommands(
    BreakpointService breakpoints,
    DebugService debug,
    Lazy<RunCommands> runCommands,
    Lazy<RunService> runs,
    Lazy<IRunConfigurationService> configurations,
    Lazy<EditorService> editors,
    NotificationService notifications,
    Lazy<IToolWindowManager> toolWindows) : ICommandContributor
{
    public const string EditBreakpoint = "debug.editBreakpoint";
    public const string AddLogPoint = "debug.addLogPoint";
    public const string ToggleBreakpointEnabled = "debug.toggleBreakpointEnabled";
    public const string RemoveAllBreakpoints = "debug.removeAllBreakpoints";
    public const string AddWatch = "debug.addWatch";

    private const string Category = "Debug";
    private const string Menu = Menus.Run;

    public void Contribute(ICommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        DebugIcons.Register();
        registry.Add(
            new CommandDefinition(CommandIds.ToggleBreakpoint, "Toggle Breakpoint", Category) { Icon = DebugIcons.Breakpoint, KeyBinding = "F9", Description = "Add a breakpoint to the line, or remove its breakpoint." },
            argument => Run(() => Toggle(argument)),
            argument => TargetOf(argument) is not null);
        registry.Add(
            new CommandDefinition(EditBreakpoint, "Edit Breakpoint...", Category) { Icon = "settings", Description = "Set the breakpoint's condition, hit count or log message." },
            argument => EditAsync(argument, logPoint: false),
            argument => TargetOf(argument) is not null);
        registry.Add(
            new CommandDefinition(AddLogPoint, "Add Log Point...", Category) { Icon = DebugIcons.LogPoint, Description = "Write a message to the debug console when the line runs, without stopping." },
            argument => EditAsync(argument, logPoint: true),
            argument => TargetOf(argument) is { } target && breakpoints.Find(target.Path, target.Line) is null);
        registry.Add(
            new CommandDefinition(ToggleBreakpointEnabled, "Enable or Disable Breakpoint", Category) { Description = "Turn the line's breakpoint off without removing it, or on again." },
            argument => Run(() => ToggleEnabled(argument)),
            argument => TargetOf(argument) is { } target && breakpoints.Find(target.Path, target.Line) is not null);
        registry.Add(
            new CommandDefinition(RemoveAllBreakpoints, "Remove All Breakpoints", Category) { Icon = "trash", Description = "Remove every breakpoint in the folder." },
            _ => RemoveAllAsync(),
            _ => breakpoints.All.Count > 0);
        registry.Add(
            new CommandDefinition(AddWatch, "Add Watch...", Category) { Icon = "eye", Description = "Watch an expression's value while debugging." },
            _ => AddWatchAsync(),
            _ => true);

        registry.Add(
            new CommandDefinition(CommandIds.Continue, "Continue", Category) { Icon = DebugIcons.Continue, KeyBinding = "F5", Description = "Resume the paused program, or start debugging the selected configuration." },
            _ => ContinueAsync(),
            _ => debug.Current is { State: DebugState.Paused } || debug.Current is null && runCommands.Value.WhyCannotDebug(configurations.Value.Selected) is null);
        registry.Add(
            new CommandDefinition(CommandIds.Pause, "Pause", Category) { Icon = "pause", Description = "Pause the running program." },
            _ => debug.Current?.PauseAsync() ?? Task.CompletedTask,
            _ => debug.Current is { State: DebugState.Running });
        registry.Add(
            new CommandDefinition(CommandIds.StepOver, "Step Over", Category) { Icon = DebugIcons.StepOver, KeyBinding = "F10", Description = "Run the current line and pause at the next one." },
            _ => debug.Current?.StepOverAsync() ?? Task.CompletedTask,
            _ => IsPaused);
        registry.Add(
            new CommandDefinition(CommandIds.StepInto, "Step Into", Category) { Icon = DebugIcons.StepInto, KeyBinding = "F11", Description = "Step into the call on the current line." },
            _ => debug.Current?.StepIntoAsync() ?? Task.CompletedTask,
            _ => IsPaused);
        registry.Add(
            new CommandDefinition(CommandIds.StepOut, "Step Out", Category) { Icon = DebugIcons.StepOut, KeyBinding = "Shift+F11", Description = "Run until the current function returns." },
            _ => debug.Current?.StepOutAsync() ?? Task.CompletedTask,
            _ => IsPaused);
        registry.Add(
            new CommandDefinition(CommandIds.RestartDebugging, "Restart Debugging", Category) { Icon = "rotate-cw", KeyBinding = "Ctrl+Shift+F5", Description = "Stop the program and debug it again." },
            _ => RestartAsync(),
            _ => debug.Current is { } session && debug.RunOf(session) is not null);
        registry.Add(
            new CommandDefinition(CommandIds.StopDebugging, "Stop Debugging", Category) { Icon = "stop", KeyBinding = "Shift+F5", Description = "End the program and the debugger." },
            _ => StopAsync(),
            _ => debug.Current is not null);

        registry.AddMenuItem(new MenuItemDefinition(Menu, CommandIds.Continue, "3-debug", 0));
        registry.AddMenuItem(new MenuItemDefinition(Menu, CommandIds.Pause, "3-debug", 1));
        registry.AddMenuItem(new MenuItemDefinition(Menu, CommandIds.StepOver, "3-debug", 2));
        registry.AddMenuItem(new MenuItemDefinition(Menu, CommandIds.StepInto, "3-debug", 3));
        registry.AddMenuItem(new MenuItemDefinition(Menu, CommandIds.StepOut, "3-debug", 4));
        registry.AddMenuItem(new MenuItemDefinition(Menu, CommandIds.RestartDebugging, "3-debug", 5));
        registry.AddMenuItem(new MenuItemDefinition(Menu, CommandIds.StopDebugging, "3-debug", 6));
        registry.AddMenuItem(new MenuItemDefinition(Menu, CommandIds.ToggleBreakpoint, "4-breakpoints", 0));
        registry.AddMenuItem(new MenuItemDefinition(Menu, EditBreakpoint, "4-breakpoints", 1));
        registry.AddMenuItem(new MenuItemDefinition(Menu, AddLogPoint, "4-breakpoints", 2));
        registry.AddMenuItem(new MenuItemDefinition(Menu, RemoveAllBreakpoints, "4-breakpoints", 3));
        registry.AddMenuItem(new MenuItemDefinition(Menu, AddWatch, "4-breakpoints", 4));
        registry.AddMenuItem(new MenuItemDefinition(ContextMenus.Editor, CommandIds.ToggleBreakpoint, "8-debug", 0));
        registry.AddMenuItem(new MenuItemDefinition(ContextMenus.Editor, EditBreakpoint, "8-debug", 1));
        registry.AddMenuItem(new MenuItemDefinition(ContextMenus.Editor, AddLogPoint, "8-debug", 2));
        registry.AddMenuItem(new MenuItemDefinition(ContextMenus.Editor, ToggleBreakpointEnabled, "8-debug", 3));
        registry.AddMenuItem(new MenuItemDefinition(ContextMenus.Editor, AddWatch, "8-debug", 4));
    }

    private bool IsPaused => debug.Current is { State: DebugState.Paused };

    /// <summary>Gets the file and line, from 0, a breakpoint command acts on: the context menu's or gutter's line, or the active editor's caret
    /// line.</summary>
    internal (string Path, int Line)? TargetOf(object? argument)
    {
        if (argument is ContextMenuTarget target)
            return !target.IsDirectory && target.Lines is { } lines ? (target.Path, lines.First - 1) : null;

        if (editors.Value.ActiveEditor is { Document.FilePath: { } path } editor)
            return (path, editor.Document.Buffer.Current.GetLineFromPosition(Math.Min(editor.CaretOffset, editor.Document.Buffer.Current.Length)).LineNumber);
        return null;
    }

    private static Task Run(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    private void Toggle(object? argument)
    {
        if (TargetOf(argument) is { } target)
            breakpoints.Toggle(target.Path, target.Line);
    }

    private void ToggleEnabled(object? argument)
    {
        if (TargetOf(argument) is { } target && breakpoints.Find(target.Path, target.Line) is { } breakpoint)
            breakpoints.Set(breakpoint with { IsEnabled = !breakpoint.IsEnabled });
    }

    private async Task EditAsync(object? argument, bool logPoint)
    {
        if (TargetOf(argument) is not { } target || notifications.Host.Dialogs?.IsOpen == true)
            return;

        var existing = breakpoints.Find(target.Path, target.Line);
        var breakpoint = existing ?? new LineBreakpoint(Path.GetFullPath(target.Path), target.Line) { LogMessage = logPoint ? "" : null };
        if (await BreakpointDialog.ShowAsync(notifications.Dialogs, breakpoint, existing is not null, logPoint) is not { } edit)
            return;

        if (edit.Remove)
        {
            if (existing is not null)
                breakpoints.Remove(existing);
            return;
        }

        breakpoints.Set(edit.Breakpoint!);
    }

    private async Task RemoveAllAsync()
    {
        var count = breakpoints.All.Count;
        if (count > 1 && !await notifications.ConfirmAsync("Remove all breakpoints", $"Remove all {count} breakpoints in the folder?", "Remove All", isDestructive: true))
            return;

        breakpoints.RemoveAll();
    }

    private async Task AddWatchAsync()
    {
        if (notifications.Host.Dialogs?.IsOpen == true)
            return;

        var selected = editors.Value.ActiveEditor is { } editor && editor.Selection.Length is > 0 and < 200
            ? editor.Document.Buffer.Current.GetText(editor.Selection).Trim()
            : "";
        var expression = selected.Contains('\n', StringComparison.Ordinal) ? "" : selected;
        if (expression.Length == 0)
            expression = await Prompt.AskAsync(notifications.Dialogs, "Add Watch", "Expression", validate: text => text.Length == 0 ? "Type an expression." : null) ?? "";
        if (expression.Length == 0)
            return;

        breakpoints.AddWatch(expression);
        toolWindows.Value.Show(DebugToolWindow.Id);
    }

    private async Task ContinueAsync()
    {
        if (debug.Current is { } session)
            await session.ContinueAsync();
        else
            await runCommands.Value.DebugSelectedAsync();
    }

    private async Task StopAsync()
    {
        if (debug.Current is not { } session)
            return;

        if (debug.RunOf(session) is { } run)
            run.Stop();
        await session.StopAsync();
    }

    private async Task RestartAsync()
    {
        if (debug.Current is not { } session || debug.RunOf(session) is not { } run)
            return;

        run.Stop();
        await session.StopAsync();
        await run.Completion;
        await runs.Value.RunAsync(run.Configuration, RunMode.Debug);
    }
}
