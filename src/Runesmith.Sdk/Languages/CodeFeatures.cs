using Runesmith.Sdk.Documents;
using Runesmith.Text;

namespace Runesmith.Sdk.Languages;

/// <summary>The references, implementations, occurrences, symbols and hierarchies of every provider, merged or picked for one document; the
/// editor and the panels use this rather than the providers.</summary>
/// <remarks>A provider that throws is logged to the "Language features" output channel and left out. <see cref="LanguageFeatureException"/>
/// and cancellation pass through to the caller. Added in plugin API 0.1.2.</remarks>
public interface INavigationFeatures
{
    /// <summary>Gets the references of every provider, without duplicates.</summary>
    Task<IReadOnlyList<DocumentLocation>> GetReferencesAsync(IDocument document, TextSnapshot snapshot, int offset, bool includeDeclaration,
        CancellationToken cancellationToken);

    /// <summary>Gets the implementations of every provider, without duplicates.</summary>
    Task<IReadOnlyList<DocumentLocation>> GetImplementationsAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken);

    /// <summary>Gets the occurrences from the first provider that answers; empty when none does.</summary>
    Task<IReadOnlyList<DocumentHighlight>> GetDocumentHighlightsAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken);

    /// <summary>Gets the symbol tree from the first provider that answers; null when none does.</summary>
    Task<IReadOnlyList<DocumentSymbol>?> GetDocumentSymbolsAsync(IDocument document, TextSnapshot snapshot, CancellationToken cancellationToken);

    /// <summary>Gets the symbols of every workspace symbol provider that match a query.</summary>
    Task<IReadOnlyList<WorkspaceSymbol>> GetWorkspaceSymbolsAsync(string query, CancellationToken cancellationToken);

    /// <summary>Gets the function at an offset from the first provider that knows one, with <see cref="HierarchyItem.Provider"/> set.</summary>
    Task<IReadOnlyList<HierarchyItem>> PrepareCallHierarchyAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken);

    /// <summary>Asks the item's provider for its callers.</summary>
    Task<IReadOnlyList<HierarchyCall>> GetIncomingCallsAsync(HierarchyItem item, CancellationToken cancellationToken);

    /// <summary>Asks the item's provider for the functions it calls.</summary>
    Task<IReadOnlyList<HierarchyCall>> GetOutgoingCallsAsync(HierarchyItem item, CancellationToken cancellationToken);

    /// <summary>Gets the type at an offset from the first provider that knows one, with <see cref="HierarchyItem.Provider"/> set.</summary>
    Task<IReadOnlyList<HierarchyItem>> PrepareTypeHierarchyAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken);

    Task<IReadOnlyList<HierarchyItem>> GetSupertypesAsync(HierarchyItem item, CancellationToken cancellationToken);

    Task<IReadOnlyList<HierarchyItem>> GetSubtypesAsync(HierarchyItem item, CancellationToken cancellationToken);

    /// <summary>Raised, on any thread, when a document symbol provider's answers changed for a reason other than an edit.</summary>
    event EventHandler<LanguageFeatureChangedEventArgs>? Changed;
}

/// <summary>The folding ranges, selection ranges and semantic tokens of the providers that serve a document; the editor uses this rather than
/// the providers.</summary>
/// <remarks>Each asks the providers in turn and takes the first answer. Providers for a named language come before providers for every
/// language, and providers from plugins before Runesmith's own. Added in plugin API 0.1.2.</remarks>
public interface ISyntaxFeatures
{
    /// <summary>Gets the folding ranges from the first provider that answers; null when none does.</summary>
    Task<IReadOnlyList<FoldingRange>?> GetFoldingRangesAsync(IDocument document, TextSnapshot snapshot, CancellationToken cancellationToken);

    /// <summary>Gets the selection ranges around an offset from the first provider that answers; null when none does.</summary>
    Task<IReadOnlyList<TextSpan>?> GetSelectionRangesAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken);

    /// <summary>Gets whether a provider of semantic tokens serves the document's language.</summary>
    bool HasSemanticTokens(IDocument document);

    /// <summary>Gets the semantic tokens of the whole document, or of those that touch <paramref name="span"/>, from the first provider that
    /// answers; null when none does.</summary>
    Task<IReadOnlyList<SemanticToken>?> GetSemanticTokensAsync(IDocument document, TextSnapshot snapshot, TextSpan? span, CancellationToken cancellationToken);

    /// <summary>Raised, on any thread, when a folding range or semantic token provider's answers changed for a reason other than an edit.</summary>
    event EventHandler<LanguageFeatureChangedEventArgs>? Changed;
}
