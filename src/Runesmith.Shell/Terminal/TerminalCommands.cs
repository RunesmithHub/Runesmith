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

    private const string Category = "Terminal";

    public void Contribute(ICommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        TerminalIcons.Register();
        registry.Add(
            new CommandDefinition(New, "New Terminal", Category) { Icon = "plus", KeyBinding = "Ctrl+Shift+`", Description = "Start your shell in a new terminal tab." },
            _ =>
            {
                window.Value.NewShell();
                return Task.CompletedTask;
            });
        registry.Add(
            new CommandDefinition(Kill, "Kill Terminal", Category) { Icon = "trash", Description = "End the selected terminal's program and close its tab." },
            _ =>
            {
                window.Value.Active?.Close();
                return Task.CompletedTask;
            },
            _ => window.Value.Active is not null);
        registry.Add(
            new CommandDefinition(Clear, "Clear Terminal", Category) { Icon = "eraser", Description = "Clear the selected terminal's screen and scrollback." },
            _ =>
            {
                window.Value.ClearActive();
                return Task.CompletedTask;
            },
            _ => window.Value.Active is not null);
        registry.Add(
            new CommandDefinition(RunSelection, "Run Selected Text in Terminal", Category) { Icon = TerminalIcons.Terminal, Description = "Type the editor's selection, or its line, into the terminal and press Enter." },
            _ => RunSelectionAsync(),
            _ => editors.Value.Active is not null);
        registry.Add(
            new CommandDefinition(OpenHere, "Open in Terminal", Category) { Icon = TerminalIcons.Terminal, Description = "Start your shell in this folder.", ShowInPalette = false },
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
