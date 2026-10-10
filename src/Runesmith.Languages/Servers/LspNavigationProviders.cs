using System.Composition;
using Runesmith.Lsp;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Text;
using Protocol = Runesmith.Lsp.Protocol;

namespace Runesmith.Languages.Servers;

/// <summary>Find References through the document's language server.</summary>
[Export(typeof(IReferenceProvider))]
[Languages(LanguagesAttribute.Any)]
[Shared]
[method: ImportingConstructor]
public sealed class LspReferenceProvider(LanguageServerManager manager) : IReferenceProvider
{
    public async Task<IReadOnlyList<DocumentLocation>> GetReferencesAsync(IDocument document, TextSnapshot snapshot, int offset, bool includeDeclaration,
        CancellationToken cancellationToken)
    {
        if (await ServerLookup.FindAsync(manager, document, snapshot).ConfigureAwait(false) is not var (_, client, uri))
            return [];

        var locations = await client.ReferencesAsync(uri, LspConvert.ToPosition(snapshot, offset), includeDeclaration, cancellationToken).ConfigureAwait(false);
        return [.. locations.Select(LspConvert.ToLocation).OfType<DocumentLocation>()];
    }
}

/// <summary>Go to Implementation through the document's language server.</summary>
[Export(typeof(IImplementationProvider))]
[Languages(LanguagesAttribute.Any)]
[Shared]
[method: ImportingConstructor]
public sealed class LspImplementationProvider(LanguageServerManager manager) : IImplementationProvider
{
    public async Task<IReadOnlyList<DocumentLocation>> GetImplementationsAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken)
    {
        if (await ServerLookup.FindAsync(manager, document, snapshot).ConfigureAwait(false) is not var (_, client, uri))
            return [];

        var locations = await client.ImplementationAsync(uri, LspConvert.ToPosition(snapshot, offset), cancellationToken).ConfigureAwait(false);
        return [.. locations.Select(LspConvert.ToLocation).OfType<DocumentLocation>()];
    }
}

/// <summary>The occurrences of the symbol at the caret, from the document's language server.</summary>
[Export(typeof(IDocumentHighlightProvider))]
[Languages(LanguagesAttribute.Any)]
[Shared]
[method: ImportingConstructor]
public sealed class LspDocumentHighlightProvider(LanguageServerManager manager) : IDocumentHighlightProvider
{
    public async Task<IReadOnlyList<DocumentHighlight>?> GetDocumentHighlightsAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken)
    {
        if (await ServerLookup.FindAsync(manager, document, snapshot).ConfigureAwait(false) is not var (_, client, uri))
            return null;

        var highlights = await client.DocumentHighlightAsync(uri, LspConvert.ToPosition(snapshot, offset), cancellationToken).ConfigureAwait(false);
        return highlights?.Select(highlight => new DocumentHighlight(LspConvert.ToSpan(snapshot, highlight.Range), LspConvert.ToHighlightKind(highlight.Kind))).ToList();
    }
}

/// <summary>The symbols of a document, from its language server.</summary>
[Export(typeof(IDocumentSymbolProvider))]
[Languages(LanguagesAttribute.Any)]
[Shared]
public sealed class LspDocumentSymbolProvider : IDocumentSymbolProvider
{
    private readonly LanguageServerManager manager;

    [ImportingConstructor]
    public LspDocumentSymbolProvider(LanguageServerManager manager)
    {
        this.manager = manager;
        manager.SyntaxChanged += (_, _) => Changed?.Invoke(this, new LanguageFeatureChangedEventArgs(null));
    }

    public event EventHandler<LanguageFeatureChangedEventArgs>? Changed;

    public async Task<IReadOnlyList<DocumentSymbol>?> GetDocumentSymbolsAsync(IDocument document, TextSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (await ServerLookup.FindAsync(manager, document, snapshot).ConfigureAwait(false) is not var (_, client, uri))
            return null;

        var symbols = await client.DocumentSymbolAsync(uri, cancellationToken).ConfigureAwait(false);
        return symbols?.Select(symbol => LspConvert.ToDocumentSymbol(snapshot, symbol)).ToList();
    }
}

/// <summary>The workspace symbols of every running language server.</summary>
[Export(typeof(IWorkspaceSymbolProvider))]
[Shared]
[method: ImportingConstructor]
public sealed class LspWorkspaceSymbolProvider(LanguageServerManager manager) : IWorkspaceSymbolProvider
{
    public async Task<IReadOnlyList<WorkspaceSymbol>> GetWorkspaceSymbolsAsync(string query, CancellationToken cancellationToken)
    {
        var answers = await Task.WhenAll(manager.RunningSessions()
            .Select(session => session.Client)
            .OfType<LanguageClient>()
            .Select(client => client.WorkspaceSymbolAsync(query, cancellationToken))).ConfigureAwait(false);
        return [.. answers.SelectMany(symbols => symbols).Select(LspConvert.ToWorkspaceSymbol).OfType<WorkspaceSymbol>()];
    }
}

/// <summary>Folding ranges from the document's language server.</summary>
[Export(typeof(IFoldingRangeProvider))]
[Languages(LanguagesAttribute.Any)]
[Shared]
public sealed class LspFoldingRangeProvider : IFoldingRangeProvider
{
    private readonly LanguageServerManager manager;

    [ImportingConstructor]
    public LspFoldingRangeProvider(LanguageServerManager manager)
    {
        this.manager = manager;
        manager.SyntaxChanged += (_, _) => Changed?.Invoke(this, new LanguageFeatureChangedEventArgs(null));
    }

    public event EventHandler<LanguageFeatureChangedEventArgs>? Changed;

    public async Task<IReadOnlyList<FoldingRange>?> GetFoldingRangesAsync(IDocument document, TextSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (await ServerLookup.FindAsync(manager, document, snapshot).ConfigureAwait(false) is not var (_, client, uri))
            return null;

        var ranges = await client.FoldingRangeAsync(uri, cancellationToken).ConfigureAwait(false);
        return ranges?.Select(LspConvert.ToFoldingRange).OfType<FoldingRange>().ToList();
    }
}

/// <summary>Semantic tokens from the document's language server, decoded with the server's legend.</summary>
[Export(typeof(ISemanticTokensProvider))]
[Languages(LanguagesAttribute.Any)]
[Shared]
public sealed class LspSemanticTokensProvider : ISemanticTokensProvider
{
    private readonly LanguageServerManager manager;

    [ImportingConstructor]
    public LspSemanticTokensProvider(LanguageServerManager manager)
    {
        this.manager = manager;
        manager.SyntaxChanged += (_, _) => Changed?.Invoke(this, new LanguageFeatureChangedEventArgs(null));
    }

    public event EventHandler<LanguageFeatureChangedEventArgs>? Changed;

    public Task<IReadOnlyList<SemanticToken>?> GetSemanticTokensAsync(IDocument document, TextSnapshot snapshot, CancellationToken cancellationToken) =>
        GetAsync(document, snapshot, null, cancellationToken);

    public Task<IReadOnlyList<SemanticToken>?> GetSemanticTokensAsync(IDocument document, TextSnapshot snapshot, TextSpan span, CancellationToken cancellationToken) =>
        GetAsync(document, snapshot, span, cancellationToken);

    private async Task<IReadOnlyList<SemanticToken>?> GetAsync(IDocument document, TextSnapshot snapshot, TextSpan? span, CancellationToken cancellationToken)
    {
        if (await ServerLookup.FindAsync(manager, document, snapshot).ConfigureAwait(false) is not var (session, client, uri)
            || session.Capabilities.SemanticTokensProvider is not { } options)
        {
            return null;
        }

        var range = span is { } value ? LspConvert.ToRange(snapshot, value) : null;
        var tokens = await client.SemanticTokensAsync(uri, range, cancellationToken).ConfigureAwait(false);
        return tokens is null ? null : LspConvert.ToSemanticTokens(snapshot, options.Legend, tokens.Data);
    }
}

/// <summary>Selection ranges from the document's language server.</summary>
[Export(typeof(ISelectionRangeProvider))]
[Languages(LanguagesAttribute.Any)]
[Shared]
[method: ImportingConstructor]
public sealed class LspSelectionRangeProvider(LanguageServerManager manager) : ISelectionRangeProvider
{
    public async Task<IReadOnlyList<TextSpan>?> GetSelectionRangesAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken)
    {
        if (await ServerLookup.FindAsync(manager, document, snapshot).ConfigureAwait(false) is not var (_, client, uri))
            return null;

        var ranges = await client.SelectionRangeAsync(uri, LspConvert.ToPosition(snapshot, offset), cancellationToken).ConfigureAwait(false);
        return ranges?.Select(range => LspConvert.ToSpan(snapshot, range)).ToList();
    }
}

/// <summary>Call and type hierarchies through the document's language server; each item keeps the server that made it.</summary>
[Export(typeof(ICallHierarchyProvider))]
[Export(typeof(ITypeHierarchyProvider))]
[Languages(LanguagesAttribute.Any)]
[Shared]
[method: ImportingConstructor]
public sealed class LspHierarchyProvider(LanguageServerManager manager) : ICallHierarchyProvider, ITypeHierarchyProvider
{
    public async Task<IReadOnlyList<HierarchyItem>> PrepareCallHierarchyAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken)
    {
        if (await ServerLookup.FindAsync(manager, document, snapshot).ConfigureAwait(false) is not var (_, client, uri))
            return [];

        return ToItems(client, await client.PrepareCallHierarchyAsync(uri, LspConvert.ToPosition(snapshot, offset), cancellationToken).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<HierarchyCall>> GetIncomingCallsAsync(HierarchyItem item, CancellationToken cancellationToken)
    {
        if (Resolve(item) is not var (client, protocolItem))
            return [];

        var calls = await client.IncomingCallsAsync(protocolItem, cancellationToken).ConfigureAwait(false);
        return [.. calls.Select(call => ToCall(client, call.From, call.FromRanges, call.From.Uri)).OfType<HierarchyCall>()];
    }

    public async Task<IReadOnlyList<HierarchyCall>> GetOutgoingCallsAsync(HierarchyItem item, CancellationToken cancellationToken)
    {
        if (Resolve(item) is not var (client, protocolItem))
            return [];

        var calls = await client.OutgoingCallsAsync(protocolItem, cancellationToken).ConfigureAwait(false);
        return [.. calls.Select(call => ToCall(client, call.To, call.FromRanges, protocolItem.Uri)).OfType<HierarchyCall>()];
    }

    public async Task<IReadOnlyList<HierarchyItem>> PrepareTypeHierarchyAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken)
    {
        if (await ServerLookup.FindAsync(manager, document, snapshot).ConfigureAwait(false) is not var (_, client, uri))
            return [];

        return ToItems(client, await client.PrepareTypeHierarchyAsync(uri, LspConvert.ToPosition(snapshot, offset), cancellationToken).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<HierarchyItem>> GetSupertypesAsync(HierarchyItem item, CancellationToken cancellationToken) =>
        Resolve(item) is var (client, protocolItem) ? ToItems(client, await client.SupertypesAsync(protocolItem, cancellationToken).ConfigureAwait(false)) : [];

    public async Task<IReadOnlyList<HierarchyItem>> GetSubtypesAsync(HierarchyItem item, CancellationToken cancellationToken) =>
        Resolve(item) is var (client, protocolItem) ? ToItems(client, await client.SubtypesAsync(protocolItem, cancellationToken).ConfigureAwait(false)) : [];

    private static (LanguageClient Client, Protocol.HierarchyItem Item)? Resolve(HierarchyItem item) =>
        item.Data is ItemData data && data.Client.State == LanguageServerState.Running ? (data.Client, data.Item) : null;

    private static List<HierarchyItem> ToItems(LanguageClient client, IEnumerable<Protocol.HierarchyItem> items) =>
        [.. items.Select(item => ToItem(client, item)).OfType<HierarchyItem>()];

    private static HierarchyItem? ToItem(LanguageClient client, Protocol.HierarchyItem item) =>
        LspConvert.ToHierarchyItem(item) is { } converted ? converted with { Data = new ItemData(client, item) } : null;

    private static HierarchyCall? ToCall(LanguageClient client, Protocol.HierarchyItem item, IReadOnlyList<Protocol.Range> ranges, string rangesUri) =>
        ToItem(client, item) is { } converted && LspUri.ToPath(rangesUri) is { } path ? new HierarchyCall(converted, LspConvert.ToLocations(path, ranges)) : null;

    private sealed record ItemData(LanguageClient Client, Protocol.HierarchyItem Item);
}
