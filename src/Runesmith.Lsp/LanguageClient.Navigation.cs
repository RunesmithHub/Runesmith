using System.Text.Json;
using Runesmith.Lsp.Protocol;
using Range = Runesmith.Lsp.Protocol.Range;

namespace Runesmith.Lsp;

public sealed partial class LanguageClient
{
    /// <summary>Raised when the server asks the client to request its semantic tokens or folding ranges again.</summary>
    public event EventHandler? SyntaxRefreshRequested;

    /// <summary>Gets the implementations of the symbol at a position; links arrive as the location of their target's selection.</summary>
    public async Task<IReadOnlyList<Location>> ImplementationAsync(string uri, Position position, CancellationToken cancellationToken)
    {
        if (!Capabilities.ImplementationProvider)
            return [];
        var result = await RequestAsync<JsonElement>("textDocument/implementation", new TextDocumentPositionParams(new TextDocumentIdentifier(uri), position),
            cancellationToken).ConfigureAwait(false);
        return result.ValueKind switch
        {
            JsonValueKind.Object => [ToLocation(result)],
            JsonValueKind.Array => [.. result.EnumerateArray().Select(ToLocation)],
            _ => [],
        };
    }

    /// <summary>Gets the occurrences of the symbol at a position in its document; null when the server does not highlight them.</summary>
    public async Task<IReadOnlyList<DocumentHighlight>?> DocumentHighlightAsync(string uri, Position position, CancellationToken cancellationToken)
    {
        if (!Capabilities.DocumentHighlightProvider)
            return null;
        return await RequestAsync<DocumentHighlight[]>("textDocument/documentHighlight", new TextDocumentPositionParams(new TextDocumentIdentifier(uri), position),
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Gets the symbols of a document as a tree; a server that answers with a flat list has it nested by range. Null when the server
    /// does not list symbols.</summary>
    public async Task<IReadOnlyList<DocumentSymbol>?> DocumentSymbolAsync(string uri, CancellationToken cancellationToken)
    {
        if (!Capabilities.DocumentSymbolProvider)
            return null;
        var result = await RequestAsync<JsonElement>("textDocument/documentSymbol", new DocumentParams(new TextDocumentIdentifier(uri)), cancellationToken)
            .ConfigureAwait(false);
        if (result.ValueKind != JsonValueKind.Array)
            return [];

        var items = result.EnumerateArray().ToList();
        if (items.Count == 0 || !items[0].TryGetProperty("location", out _))
            return [.. items.Select(item => item.Deserialize(LspJsonContext.Default.DocumentSymbol)).OfType<DocumentSymbol>()];

        var flat = items.Select(item => item.Deserialize(LspJsonContext.Default.SymbolInformation)).OfType<SymbolInformation>()
            .Where(symbol => symbol.Location.Range is not null)
            .Select(symbol => new DocumentSymbol(symbol.Name, symbol.Kind, symbol.Location.Range!, symbol.Location.Range!) { Detail = symbol.ContainerName, Tags = symbol.Tags, Deprecated = symbol.Deprecated });
        return Nest(flat);
    }

    /// <summary>Finds the symbols of the workspace that match a query; empty when the server does not search symbols.</summary>
    public async Task<IReadOnlyList<SymbolInformation>> WorkspaceSymbolAsync(string query, CancellationToken cancellationToken)
    {
        if (!Capabilities.WorkspaceSymbolProvider)
            return [];
        return await RequestAsync<SymbolInformation[]>("workspace/symbol", new WorkspaceSymbolParams(query), cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Gets the folding ranges of a document; null when the server does not fold.</summary>
    public async Task<IReadOnlyList<FoldingRange>?> FoldingRangeAsync(string uri, CancellationToken cancellationToken)
    {
        if (!Capabilities.FoldingRangeProvider)
            return null;
        return await RequestAsync<FoldingRange[]>("textDocument/foldingRange", new DocumentParams(new TextDocumentIdentifier(uri)), cancellationToken)
            .ConfigureAwait(false) ?? [];
    }

    /// <summary>Gets the semantic tokens of a whole document, or of a range when <paramref name="range"/> is given and the server answers for
    /// ranges; null when the server does not classify tokens.</summary>
    public async Task<SemanticTokens?> SemanticTokensAsync(string uri, Range? range, CancellationToken cancellationToken)
    {
        if (Capabilities.SemanticTokensProvider is not { } provider)
            return null;
        if (range is not null && provider.Range)
        {
            return await RequestAsync<SemanticTokens>("textDocument/semanticTokens/range", new SemanticTokensRangeParams(new TextDocumentIdentifier(uri), range),
                cancellationToken).ConfigureAwait(false);
        }

        if (!provider.Full)
            return null;
        return await RequestAsync<SemanticTokens>("textDocument/semanticTokens/full", new DocumentParams(new TextDocumentIdentifier(uri)), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Gets the ranges around a position, from the smallest; null when the server does not answer.</summary>
    public async Task<IReadOnlyList<Range>?> SelectionRangeAsync(string uri, Position position, CancellationToken cancellationToken)
    {
        if (!Capabilities.SelectionRangeProvider)
            return null;
        var result = await RequestAsync<SelectionRange[]>("textDocument/selectionRange", new SelectionRangeParams(new TextDocumentIdentifier(uri), [position]),
            cancellationToken).ConfigureAwait(false);
        if (result is not [{ } first, ..])
            return null;

        var ranges = new List<Range>();
        for (var current = first; current is not null && ranges.Count < 200; current = current.Parent)
            ranges.Add(current.Range);
        return ranges;
    }

    /// <summary>Gets the function at a position for a call hierarchy; empty when there is none or the server has no call hierarchy.</summary>
    public Task<IReadOnlyList<HierarchyItem>> PrepareCallHierarchyAsync(string uri, Position position, CancellationToken cancellationToken) =>
        Capabilities.CallHierarchyProvider ? PrepareAsync("textDocument/prepareCallHierarchy", uri, position, cancellationToken) : Task.FromResult<IReadOnlyList<HierarchyItem>>([]);

    public async Task<IReadOnlyList<CallHierarchyIncomingCall>> IncomingCallsAsync(HierarchyItem item, CancellationToken cancellationToken) =>
        await RequestAsync<CallHierarchyIncomingCall[]>("callHierarchy/incomingCalls", new HierarchyItemParams(item), cancellationToken).ConfigureAwait(false) ?? [];

    public async Task<IReadOnlyList<CallHierarchyOutgoingCall>> OutgoingCallsAsync(HierarchyItem item, CancellationToken cancellationToken) =>
        await RequestAsync<CallHierarchyOutgoingCall[]>("callHierarchy/outgoingCalls", new HierarchyItemParams(item), cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Gets the type at a position for a type hierarchy; empty when there is none or the server has no type hierarchy.</summary>
    public Task<IReadOnlyList<HierarchyItem>> PrepareTypeHierarchyAsync(string uri, Position position, CancellationToken cancellationToken) =>
        Capabilities.TypeHierarchyProvider ? PrepareAsync("textDocument/prepareTypeHierarchy", uri, position, cancellationToken) : Task.FromResult<IReadOnlyList<HierarchyItem>>([]);

    public async Task<IReadOnlyList<HierarchyItem>> SupertypesAsync(HierarchyItem item, CancellationToken cancellationToken) =>
        await RequestAsync<HierarchyItem[]>("typeHierarchy/supertypes", new HierarchyItemParams(item), cancellationToken).ConfigureAwait(false) ?? [];

    public async Task<IReadOnlyList<HierarchyItem>> SubtypesAsync(HierarchyItem item, CancellationToken cancellationToken) =>
        await RequestAsync<HierarchyItem[]>("typeHierarchy/subtypes", new HierarchyItemParams(item), cancellationToken).ConfigureAwait(false) ?? [];

    private async Task<IReadOnlyList<HierarchyItem>> PrepareAsync(string method, string uri, Position position, CancellationToken cancellationToken) =>
        await RequestAsync<HierarchyItem[]>(method, new TextDocumentPositionParams(new TextDocumentIdentifier(uri), position), cancellationToken).ConfigureAwait(false) ?? [];

    private void RegisterNavigation(JsonRpcConnection connection)
    {
        connection.OnRequest("workspace/semanticTokens/refresh", (_, _) => RefreshSyntax());
        connection.OnRequest("workspace/foldingRange/refresh", (_, _) => RefreshSyntax());
    }

    private Task<object?> RefreshSyntax()
    {
        SyntaxRefreshRequested?.Invoke(this, EventArgs.Empty);
        return Task.FromResult<object?>(null);
    }

    // Each symbol goes inside the last symbol before it whose range contains it.
    private static List<DocumentSymbol> Nest(IEnumerable<DocumentSymbol> flat)
    {
        var roots = new List<SymbolNode>();
        var open = new Stack<SymbolNode>();
        foreach (var symbol in flat.OrderBy(s => s.Range.Start.Line).ThenBy(s => s.Range.Start.Character))
        {
            while (open.Count > 0 && !Contains(open.Peek().Symbol.Range, symbol.Range))
                open.Pop();

            var node = new SymbolNode(symbol);
            (open.Count == 0 ? roots : open.Peek().Children).Add(node);
            open.Push(node);
        }

        return [.. roots.Select(node => node.Build())];
    }

    private static bool Contains(Range outer, Range inner) => Compare(outer.Start, inner.Start) <= 0 && Compare(inner.End, outer.End) <= 0;

    private static int Compare(Position a, Position b) => a.Line != b.Line ? a.Line.CompareTo(b.Line) : a.Character.CompareTo(b.Character);

    private sealed class SymbolNode(DocumentSymbol symbol)
    {
        public DocumentSymbol Symbol { get; } = symbol;

        public List<SymbolNode> Children { get; } = [];

        public DocumentSymbol Build() => Children.Count == 0 ? Symbol : Symbol with { Children = [.. Children.Select(child => child.Build())] };
    }
}
