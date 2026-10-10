using System.Composition;
using Runesmith.Editor;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Workspace;
using Runesmith.Shell.Editors;
using Runesmith.Shell.Symbols;
using Runesmith.Workspace.Search;

namespace Runesmith.Shell.Palette;

/// <summary>Goes to a symbol of the active file: <c>@Main</c>.</summary>
[Export(typeof(IQuickPickSource))]
[method: ImportingConstructor]
public sealed class DocumentSymbolSource(EditorService editors, DocumentSymbols symbols) : IQuickPickSource
{
    public string Prefix => "@";

    public string Name => "Symbols";

    public string Placeholder => editors.Active is null ? "Open a file first to go to its symbols" : "Type the name of a symbol in this file";

    public async Task<IReadOnlyList<QuickPickItem>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (editors.Active is not { } editor)
            return [];

        var tracker = symbols.For(editor);
        if (tracker.Symbols is null)
            await tracker.RefreshAsync();
        if (tracker.Symbols is not { } tree)
            return [new QuickPickItem($"Nothing lists the symbols of {editor.Document.Name}", null) { IsEnabled = false, Icon = HammerUI.Icons.Info }];

        var rows = new List<(QuickPickItem Item, double Score, int Start)>();
        foreach (var (symbol, container) in Flatten(tree, null))
        {
            int[] matches = [];
            var score = query.Length == 0 ? 1 : FuzzyMatcher.Score(symbol.Name, query, out matches);
            if (score <= 0)
                continue;

            rows.Add((new QuickPickItem(symbol.Name, symbol)
            {
                Subtitle = container,
                Detail = SymbolKinds.Name(symbol.Kind),
                Icon = SymbolKinds.Icon(symbol.Kind),
                Matches = matches,
            }, score, symbol.Range.Start));
        }

        return query.Length == 0
            ? [.. rows.OrderBy(r => r.Start).Select(r => r.Item)]
            : [.. rows.OrderByDescending(r => r.Score).ThenBy(r => r.Start).Select(r => r.Item)];
    }

    public Task AcceptAsync(QuickPickItem item)
    {
        if (editors.Active is { } editor && item.Value is DocumentSymbol symbol)
        {
            var span = symbol.SelectionRange;
            editor.Area.Select(new EditorSelection(span.Start, span.End), scrollIntoView: false);
            editor.Area.ScrollIntoView(span.Start, center: true);
            editor.Area.Focus();
        }

        return Task.CompletedTask;
    }

    private static IEnumerable<(DocumentSymbol Symbol, string? Container)> Flatten(IReadOnlyList<DocumentSymbol> level, string? container)
    {
        foreach (var symbol in level)
        {
            yield return (symbol, container);
            foreach (var child in Flatten(symbol.Children, container is null ? symbol.Name : $"{container} › {symbol.Name}"))
                yield return child;
        }
    }
}

/// <summary>Finds symbols anywhere in the open folder by name: <c>#User</c>. Search Everywhere lists a few of them too.</summary>
[Export(typeof(IQuickPickSource))]
[Export]
[method: ImportingConstructor]
public sealed class WorkspaceSymbolSource(INavigationFeatures features, EditorService editors, IWorkspace workspace) : IQuickPickSource
{
    private const int MaxRows = 100;

    public string Prefix => "#";

    public string Name => "Workspace";

    public string Placeholder => "Type the name of a symbol";

    public async Task<IReadOnlyList<QuickPickItem>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (query.Length == 0)
            return [];

        await Task.Delay(120, cancellationToken);
        var found = await Task.Run(() => features.GetWorkspaceSymbolsAsync(query, cancellationToken), cancellationToken);
        var rows = new List<(QuickPickItem Item, double Score)>();
        foreach (var symbol in found)
        {
            var score = FuzzyMatcher.Score(symbol.Name, query, out var matches);
            if (score <= 0)
                continue;

            var path = workspace.RootPath is { } root ? Path.GetRelativePath(root, symbol.Location.FilePath) : symbol.Location.FilePath;
            rows.Add((new QuickPickItem(symbol.Name, symbol)
            {
                Subtitle = $"{path}:{symbol.Location.Start.Line + 1}",
                Detail = symbol.ContainerName ?? SymbolKinds.Name(symbol.Kind),
                Icon = SymbolKinds.Icon(symbol.Kind),
                Matches = matches,
            }, score));
        }

        return [.. rows.OrderByDescending(r => r.Score).ThenBy(r => r.Item.Title.Length).Take(MaxRows).Select(r => r.Item)];
    }

    public Task AcceptAsync(QuickPickItem item) =>
        item.Value is WorkspaceSymbol symbol ? editors.RevealAsync(symbol.Location.FilePath, symbol.Location.Start, null, focus: true) : Task.CompletedTask;
}
