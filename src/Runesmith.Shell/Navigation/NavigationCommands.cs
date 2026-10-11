using System.Composition;
using Runesmith.Editor;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Languages;
using Runesmith.Shell.Editors;
using Runesmith.Shell.Services;

namespace Runesmith.Shell.Navigation;

/// <summary>The commands that find and show symbols: references, implementations, symbols by name and hierarchies; and folding and expanding
/// the selection.</summary>
[Export(typeof(ICommandContributor))]
[method: ImportingConstructor]
public sealed class NavigationCommands(
    EditorService editors,
    ShellController shell,
    INavigationFeatures features,
    Lazy<ReferencesToolWindow> references,
    Lazy<HierarchyToolWindow> hierarchy) : ICommandContributor
{
    private TextEditor? Editor => editors.Active;

    public void Contribute(ICommandRegistry registry)
    {
        var order = 100;
        void Add(string id, string title, string category, string? icon, string? keys, Func<Task> run, Func<bool>? canRun, string? menu, string group, string? description = null)
        {
            registry.Add(new CommandDefinition(id, title, category) { Icon = icon, KeyBinding = keys, Description = description }, _ => run(), canRun is null ? null : _ => canRun());
            if (menu is not null)
                registry.AddMenuItem(new MenuItemDefinition(menu, id, group, order++));
        }

        void Sync(string id, string title, string category, string? icon, string? keys, Action run, Func<bool>? canRun, string? menu, string group, string? description = null) =>
            Add(id, title, category, icon, keys, () =>
            {
                run();
                return Task.CompletedTask;
            }, canRun, menu, group, description);

        bool HasEditor() => Editor is not null;

        Add(CommandIds.GoToImplementation, Strings.ShellGoToImplementation, Strings.ShellCategoryGo, null, "Ctrl+F12", GoToImplementationAsync, HasEditor, Menus.Go, "2-symbol",
            Strings.ShellGoToImplementationDescription);
        Add(CommandIds.FindReferences, Strings.ShellFindReferences, Strings.ShellCategoryGo, "link", "Shift+F12", FindReferencesAsync, HasEditor, Menus.Go, "2-symbol",
            Strings.ShellFindReferencesDescription);
        Add(CommandIds.GoToSymbolInFile, Strings.ShellGoToSymbolInFile, Strings.ShellCategoryGo, "list-tree", "Ctrl+R", () => shell.ShowQuickPickAsync("@"), HasEditor, Menus.Go, "2-symbol",
            Strings.ShellGoToSymbolInFileDescription);
        Add(CommandIds.GoToSymbolInWorkspace, Strings.ShellGoToSymbolInWorkspace, Strings.ShellCategoryGo, null, "Ctrl+Shift+R", () => shell.ShowQuickPickAsync("#"), null, Menus.Go, "2-symbol",
            Strings.ShellGoToSymbolInWorkspaceDescription);
        Add(CommandIds.ShowCallHierarchy, Strings.ShellShowCallHierarchy, Strings.ShellCategoryGo, HierarchyToolWindow.Icon, "Ctrl+Alt+H", ShowCallHierarchyAsync, HasEditor, Menus.Go, "2-symbol",
            Strings.ShellShowCallHierarchyDescription);
        Add(CommandIds.ShowTypeHierarchy, Strings.ShellShowTypeHierarchy, Strings.ShellCategoryGo, null, "Ctrl+Alt+Shift+H", ShowTypeHierarchyAsync, HasEditor, Menus.Go, "2-symbol",
            Strings.ShellShowTypeHierarchyDescription);

        Add(CommandIds.ExpandSelection, Strings.ShellExpandSelection, Strings.ShellCategorySelection, null, "Shift+Alt+Right", () => Editor!.ExpandSelectionAsync(), HasEditor, Menus.Selection, "1-select",
            Strings.ShellExpandSelectionDescription);
        Sync(CommandIds.ShrinkSelection, Strings.ShellShrinkSelection, Strings.ShellCategorySelection, null, "Shift+Alt+Left", () => Editor!.ShrinkSelection(), HasEditor, Menus.Selection, "1-select",
            Strings.ShellShrinkSelectionDescription);

        Sync(CommandIds.Fold, Strings.ShellFold, Strings.ShellCategoryView, "chevron-right", "Ctrl+Shift+[", () => Editor!.Fold(), HasEditor, Menus.View, "5-folding",
            Strings.ShellFoldDescription);
        Sync(CommandIds.Unfold, Strings.ShellUnfold, Strings.ShellCategoryView, "chevron-down", "Ctrl+Shift+]", () => Editor!.Unfold(), HasEditor, Menus.View, "5-folding",
            Strings.ShellUnfoldDescription);
        Sync(CommandIds.FoldAll, Strings.ShellFoldAll, Strings.ShellCategoryView, null, "Ctrl+Alt+[", () => Editor!.FoldAll(), HasEditor, Menus.View, "5-folding");
        Sync(CommandIds.UnfoldAll, Strings.ShellUnfoldAll, Strings.ShellCategoryView, null, "Ctrl+Alt+]", () => Editor!.UnfoldAll(), HasEditor, Menus.View, "5-folding");
    }

    private async Task FindReferencesAsync()
    {
        if (Editor is not { } editor)
            return;

        var name = editor.WordAtCaret();
        if (await editor.FindReferencesAsync() is not { } found)
            return;

        await ShowAsync(editor, found, Strings.ShellReferencesTo(name ?? ""), "reference", Strings.ShellNoReferencesFound);
    }

    private async Task GoToImplementationAsync()
    {
        if (Editor is not { } editor)
            return;

        var name = editor.WordAtCaret();
        if (await editor.FindImplementationsAsync() is not { } found)
            return;

        await ShowAsync(editor, found, Strings.ShellImplementationsOf(name ?? ""), "implementation", Strings.ShellNoImplementationsFound);
    }

    // One place is opened at once; several are listed in the References panel.
    private async Task ShowAsync(TextEditor editor, IReadOnlyList<DocumentLocation> found, string title, string noun, string none)
    {
        switch (found.Count)
        {
            case 0:
                editor.ShowMessage(none);
                break;
            case 1:
                await editors.RevealAsync(found[0].FilePath, found[0].Start, found[0].End, focus: true);
                break;
            default:
                await references.Value.ShowAsync(title, noun, found);
                break;
        }
    }

    private async Task ShowCallHierarchyAsync()
    {
        var items = await PrepareAsync(features.PrepareCallHierarchyAsync);
        if (items is null)
            Editor?.ShowMessage(Strings.ShellNoCallTarget);
        else if (items.Count > 0)
            hierarchy.Value.ShowCalls(items);
    }

    private async Task ShowTypeHierarchyAsync()
    {
        var items = await PrepareAsync(features.PrepareTypeHierarchyAsync);
        if (items is null)
            Editor?.ShowMessage(Strings.ShellNoTypeTarget);
        else if (items.Count > 0)
            hierarchy.Value.ShowTypes(items);
    }

    // Null when there is nothing to show; empty when the provider refused and the user has been told why.
    private async Task<IReadOnlyList<HierarchyItem>?> PrepareAsync(
        Func<Sdk.Documents.IDocument, Text.TextSnapshot, int, CancellationToken, Task<IReadOnlyList<HierarchyItem>>> prepare)
    {
        if (Editor is not { } editor)
            return null;

        var snapshot = editor.Area.Snapshot;
        var offset = editor.CaretOffset;
        try
        {
            var items = await Task.Run(() => prepare(editor.Document, snapshot, offset, CancellationToken.None));
            return items.Count > 0 ? items : null;
        }
        catch (LanguageFeatureException exception)
        {
            editor.ShowMessage(exception.Message);
            return [];
        }
    }
}
