using System.Composition;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Shell;
using Runesmith.Text;

namespace Runesmith.Languages.Features;

/// <summary>Asks the reference, implementation, highlight, symbol and hierarchy providers that serve a document, and merges or picks what they
/// answer.</summary>
[Export(typeof(INavigationFeatures))]
[Shared]
public sealed class NavigationFeatures : INavigationFeatures
{
    private readonly FeatureLog log;
    private readonly ProviderList<IReferenceProvider> references;
    private readonly ProviderList<IImplementationProvider> implementations;
    private readonly ProviderList<IDocumentHighlightProvider> highlights;
    private readonly ProviderList<IDocumentSymbolProvider> symbols;
    private readonly ProviderList<ICallHierarchyProvider> callHierarchy;
    private readonly ProviderList<ITypeHierarchyProvider> typeHierarchy;
    private readonly Lazy<IWorkspaceSymbolProvider>[] workspaceSymbols;

    [ImportingConstructor]
    public NavigationFeatures(
        [ImportMany] IEnumerable<Lazy<IReferenceProvider, LanguageMetadata>> referenceProviders,
        [ImportMany] IEnumerable<Lazy<IImplementationProvider, LanguageMetadata>> implementationProviders,
        [ImportMany] IEnumerable<Lazy<IDocumentHighlightProvider, LanguageMetadata>> highlightProviders,
        [ImportMany] IEnumerable<Lazy<IDocumentSymbolProvider, LanguageMetadata>> symbolProviders,
        [ImportMany] IEnumerable<Lazy<IWorkspaceSymbolProvider>> workspaceSymbolProviders,
        [ImportMany] IEnumerable<Lazy<ICallHierarchyProvider, LanguageMetadata>> callHierarchyProviders,
        [ImportMany] IEnumerable<Lazy<ITypeHierarchyProvider, LanguageMetadata>> typeHierarchyProviders,
        Lazy<IOutputService> output)
    {
        log = new FeatureLog(output);
        references = new(referenceProviders, log);
        implementations = new(implementationProviders, log);
        highlights = new(highlightProviders, log);
        symbols = new(symbolProviders, log, provider => provider.Changed += (_, e) => Changed?.Invoke(this, e));
        workspaceSymbols = [.. workspaceSymbolProviders];
        callHierarchy = new(callHierarchyProviders, log);
        typeHierarchy = new(typeHierarchyProviders, log);
    }

    public event EventHandler<LanguageFeatureChangedEventArgs>? Changed;

    public Task<IReadOnlyList<DocumentLocation>> GetReferencesAsync(IDocument document, TextSnapshot snapshot, int offset, bool includeDeclaration,
        CancellationToken cancellationToken) =>
        MergeAsync(references.Serving(document), provider => provider.GetReferencesAsync(document, snapshot, offset, includeDeclaration, cancellationToken));

    public Task<IReadOnlyList<DocumentLocation>> GetImplementationsAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken) =>
        MergeAsync(implementations.Serving(document), provider => provider.GetImplementationsAsync(document, snapshot, offset, cancellationToken));

    public async Task<IReadOnlyList<DocumentHighlight>> GetDocumentHighlightsAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken) =>
        await highlights.FirstAsync(document, provider => provider.GetDocumentHighlightsAsync(document, snapshot, offset, cancellationToken)).ConfigureAwait(false) ?? [];

    public Task<IReadOnlyList<DocumentSymbol>?> GetDocumentSymbolsAsync(IDocument document, TextSnapshot snapshot, CancellationToken cancellationToken) =>
        symbols.FirstAsync(document, provider => provider.GetDocumentSymbolsAsync(document, snapshot, cancellationToken));

    public async Task<IReadOnlyList<WorkspaceSymbol>> GetWorkspaceSymbolsAsync(string query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var answers = await Task.WhenAll(workspaceSymbols.Select(async export =>
        {
            IWorkspaceSymbolProvider provider;
            try
            {
                provider = export.Value;
            }
            catch (Exception exception)
            {
                log.Write(null, exception);
                return [];
            }

            return await log.SafeAsync(provider, () => provider.GetWorkspaceSymbolsAsync(query, cancellationToken), []).ConfigureAwait(false);
        })).ConfigureAwait(false);
        return [.. answers.SelectMany(found => found).Distinct()];
    }

    public Task<IReadOnlyList<HierarchyItem>> PrepareCallHierarchyAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken) =>
        PrepareAsync(callHierarchy, document, provider => provider.PrepareCallHierarchyAsync(document, snapshot, offset, cancellationToken));

    public Task<IReadOnlyList<HierarchyCall>> GetIncomingCallsAsync(HierarchyItem item, CancellationToken cancellationToken) =>
        CallsAsync(item, provider => provider.GetIncomingCallsAsync(item, cancellationToken));

    public Task<IReadOnlyList<HierarchyCall>> GetOutgoingCallsAsync(HierarchyItem item, CancellationToken cancellationToken) =>
        CallsAsync(item, provider => provider.GetOutgoingCallsAsync(item, cancellationToken));

    public Task<IReadOnlyList<HierarchyItem>> PrepareTypeHierarchyAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken) =>
        PrepareAsync(typeHierarchy, document, provider => provider.PrepareTypeHierarchyAsync(document, snapshot, offset, cancellationToken));

    public Task<IReadOnlyList<HierarchyItem>> GetSupertypesAsync(HierarchyItem item, CancellationToken cancellationToken) =>
        TypesAsync(item, provider => provider.GetSupertypesAsync(item, cancellationToken));

    public Task<IReadOnlyList<HierarchyItem>> GetSubtypesAsync(HierarchyItem item, CancellationToken cancellationToken) =>
        TypesAsync(item, provider => provider.GetSubtypesAsync(item, cancellationToken));

    private async Task<IReadOnlyList<DocumentLocation>> MergeAsync<T>(IReadOnlyList<T> providers, Func<T, Task<IReadOnlyList<DocumentLocation>>> ask)
        where T : class
    {
        var answers = await Task.WhenAll(providers.Select(provider => log.SafeAsync(provider, () => ask(provider), []))).ConfigureAwait(false);
        return
        [
            .. answers.SelectMany(locations => locations)
                .DistinctBy(location => (location.FilePath, location.Start))
                .OrderBy(location => location.FilePath, StringComparer.Ordinal)
                .ThenBy(location => location.Start),
        ];
    }

    private async Task<IReadOnlyList<HierarchyItem>> PrepareAsync<T>(ProviderList<T> providers, IDocument document, Func<T, Task<IReadOnlyList<HierarchyItem>>> ask)
        where T : class
    {
        foreach (var provider in providers.Serving(document))
        {
            var items = await log.SafeAsync(provider, () => ask(provider), []).ConfigureAwait(false);
            if (items.Count > 0)
                return [.. items.Select(item => item.Provider is null ? item with { Provider = provider } : item)];
        }

        return [];
    }

    private async Task<IReadOnlyList<HierarchyCall>> CallsAsync(HierarchyItem item, Func<ICallHierarchyProvider, Task<IReadOnlyList<HierarchyCall>>> ask)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Provider is not ICallHierarchyProvider provider)
            return [];

        var calls = await log.SafeAsync(provider, () => ask(provider), []).ConfigureAwait(false);
        return [.. calls.Select(call => call.Item.Provider is null ? call with { Item = call.Item with { Provider = provider } } : call)];
    }

    private async Task<IReadOnlyList<HierarchyItem>> TypesAsync(HierarchyItem item, Func<ITypeHierarchyProvider, Task<IReadOnlyList<HierarchyItem>>> ask)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Provider is not ITypeHierarchyProvider provider)
            return [];

        var types = await log.SafeAsync(provider, () => ask(provider), []).ConfigureAwait(false);
        return [.. types.Select(type => type.Provider is null ? type with { Provider = provider } : type)];
    }
}
