using System.Composition;
using Runesmith.Sdk.Commands;
using Runesmith.Shell.Editors;

namespace Runesmith.Shell.Terminal;

/// <summary>The terminal's commands: a new terminal, clearing and ending one, running the editor's selection in it and opening a folder in
/// it from the Explorer.</summary>
[Export(typeof(ICommandContributor))]
[Shared]
[method: ImportingConstructor]
public sealed class TerminalCommands(Lazy<TerminalToolWindow> window, Lazy<EditorService> editors) : ICommandContributor
{
    public const string New = "terminal.new";
    public const string Kill = "terminal.kill";
    public const string Clear = "terminal.clear";
    public const string RunSelection = "terminal.runSelection";
    public const string OpenHere = "terminal.openHere";

    private static string Category => Strings.ToolsCategoryTerminal;

    public void Contribute(ICommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        TerminalIcons.Register();
        registry.Add(
            new CommandDefinition(New, Strings.ToolsTerminalNewTerminal, Category) { Icon = "plus", KeyBinding = "Ctrl+Shift+`", Description = Strings.ToolsTerminalNewTerminalDescription },
            _ =>
            {
                window.Value.NewShell();
                return Task.CompletedTask;
            });
        registry.Add(
            new CommandDefinition(Kill, Strings.ToolsTerminalKillTerminal, Category) { Icon = "trash", Description = Strings.ToolsTerminalKillTerminalDescription },
            _ =>
            {
                window.Value.Active?.Close();
                return Task.CompletedTask;
            },
            _ => window.Value.Active is not null);
        registry.Add(
            new CommandDefinition(Clear, Strings.ToolsTerminalClearTerminal, Category) { Icon = "eraser", Description = Strings.ToolsTerminalClearTerminalDescription },
            _ =>
            {
                window.Value.ClearActive();
                return Task.CompletedTask;
            },
            _ => window.Value.Active is not null);
        registry.Add(
            new CommandDefinition(RunSelection, Strings.ToolsTerminalRunSelectedTextInTerminal, Category) { Icon = TerminalIcons.Terminal, Description = Strings.ToolsTerminalRunSelectedTextInTerminalDescription },
            _ => RunSelectionAsync(),
            _ => editors.Value.Active is not null);
        registry.Add(
            new CommandDefinition(OpenHere, Strings.ToolsTerminalOpenInTerminal, Category) { Icon = TerminalIcons.Terminal, Description = Strings.ToolsTerminalOpenInTerminalDescription, ShowInPalette = false },
            argument =>
            {
                if (argument is ContextMenuTarget target)
                    window.Value.NewShell(target.IsDirectory ? target.Path : Path.GetDirectoryName(target.Path));
                return Task.CompletedTask;
            },
            argument => argument is ContextMenuTarget);

        registry.AddMenuItem(new MenuItemDefinition(Menus.View, New, "3-terminal", 0));
        registry.AddMenuItem(new MenuItemDefinition(ContextMenus.Explorer, OpenHere, "terminal"));
    }

    private Task RunSelectionAsync()
    {
        if (editors.Value.Active is not { } editor)
            return Task.CompletedTask;

        var snapshot = editor.Document.Buffer.Current;
        var selection = editor.Selection;
        var text = selection.IsEmpty ? snapshot.GetLineText(snapshot.GetLineFromPosition(selection.Start).LineNumber) : snapshot.GetText(selection);
        var terminal = window.Value.Active is { HasExited: false } active ? active : window.Value.NewShell();
        if (terminal is null || string.IsNullOrWhiteSpace(text))
            return Task.CompletedTask;

        terminal.Show(takeFocus: false);
        terminal.Paste(text.TrimEnd('\n', '\r'));
        terminal.SendText("", addNewLine: true);
        return Task.CompletedTask;
    }
}
