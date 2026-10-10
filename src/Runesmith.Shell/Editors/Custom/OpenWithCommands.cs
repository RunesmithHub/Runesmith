using System.Composition;
using HammerUI;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Editors;
using Runesmith.Shell.Palette;
using Runesmith.Shell.Services;

namespace Runesmith.Shell.Editors.Custom;

/// <summary>The commands that choose the editor a file opens in: Open With, the default editor for a kind of file, and Revert File.</summary>
[Export(typeof(ICommandContributor))]
[method: ImportingConstructor]
public sealed class OpenWithCommands(CustomEditorService customEditors, EditorService editors, IDocumentService documents, ShellController shell) : ICommandContributor
{
    public const string OpenWith = "file.openWith";
    public const string SetDefaultEditor = "file.setDefaultEditor";
    public const string Revert = "file.revert";

    public void Contribute(ICommandRegistry registry)
    {
        registry.Add(new CommandDefinition(OpenWith, "Open With...", "File") { Description = "Open the file in another editor, such as the text editor or an image viewer." },
            argument => FileOf(argument) is { } path ? OpenWithAsync(path) : Task.CompletedTask, argument => FileOf(argument) is not null);
        registry.Add(new CommandDefinition(SetDefaultEditor, "Set Default Editor...", "File") { Description = "Choose the editor files of this kind open in." },
            argument => FileOf(argument) is { } path ? SetDefaultAsync(path) : Task.CompletedTask, argument => FileOf(argument) is not null);
        registry.Add(new CommandDefinition(Revert, "Revert File", "File") { Icon = "undo", Description = "Read the file from disk again, dropping unsaved changes." },
            _ => RevertAsync(), _ => customEditors.Active is not null || editors.Active?.Document.FilePath is not null);
        registry.AddMenuItem(new MenuItemDefinition(Menus.File, OpenWith, "1-new", 100));
        registry.AddMenuItem(new MenuItemDefinition(Menus.File, Revert, "2-save", 100));
        registry.AddMenuItem(new MenuItemDefinition(ContextMenus.Explorer, OpenWith, "open"));
        registry.AddMenuItem(new MenuItemDefinition(ContextMenus.Editor, OpenWith, "open"));
    }

    /// <summary>Gets the file a command works on: the context menu's file, else the file of the active tab.</summary>
    private string? FileOf(object? argument) => argument switch
    {
        ContextMenuTarget { IsDirectory: false } target => target.Path,
        ContextMenuTarget => null,
        _ => customEditors.Active?.FilePath ?? editors.Active?.Document.FilePath,
    };

    private async Task OpenWithAsync(string path)
    {
        var open = customEditors.OpenEditorOf(path);
        var key = FilePatterns.KeyOf(path);
        var rows = customEditors.GetChoices(path).Select(choice => new QuickPickItem(choice.Name, choice.Id)
        {
            Icon = choice.Id == EditorProviderDefinition.TextEditorId ? Icons.FileText : Icons.Find(customEditors.Catalog.Find(choice.Id)?.Definition.Icon) ?? Icons.File,
            Subtitle = choice.IsDefault ? $"Default for {key}" : null,
            Detail = choice.Id == open ? "Open" : null,
        }).ToList();
        rows.Add(new QuickPickItem($"Set Default Editor for {key}...", SetDefaultEditor) { Icon = Icons.Settings });

        switch ((await shell.PickAsync($"Open {Path.GetFileName(path)} with", rows))?.Value)
        {
            case SetDefaultEditor:
                await SetDefaultAsync(path);
                break;
            case string id:
                await customEditors.OpenWithAsync(path, id);
                break;
        }
    }

    private async Task SetDefaultAsync(string path)
    {
        var key = FilePatterns.KeyOf(path);
        var rows = customEditors.GetChoices(path).Select(choice => new QuickPickItem(choice.Name, choice.Id) { Detail = choice.IsDefault ? "Current" : null }).ToList();
        if ((await shell.PickAsync($"Default editor for {key}", rows))?.Value is string id)
            customEditors.SetDefault(path, id);
    }

    private async Task RevertAsync()
    {
        if (customEditors.Active is { } custom)
            await customEditors.RevertAsync(custom);
        else if (editors.Active?.Document is { FilePath: not null } document)
            await documents.ReloadAsync(document);
    }
}
