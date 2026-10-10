using System.Composition;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Shell;
using Runesmith.Text;

namespace Runesmith.Languages.Features;

/// <summary>Asks the folding range, selection range and semantic token providers that serve a document, and takes the first answer.</summary>
[Export(typeof(ISyntaxFeatures))]
[Shared]
public sealed class SyntaxFeatures : ISyntaxFeatures
{
    private readonly ProviderList<IFoldingRangeProvider> folding;
    private readonly ProviderList<ISelectionRangeProvider> selection;
    private readonly ProviderList<ISemanticTokensProvider> semanticTokens;

    [ImportingConstructor]
    public SyntaxFeatures(
        [ImportMany] IEnumerable<Lazy<IFoldingRangeProvider, LanguageMetadata>> foldingProviders,
        [ImportMany] IEnumerable<Lazy<ISelectionRangeProvider, LanguageMetadata>> selectionProviders,
        [ImportMany] IEnumerable<Lazy<ISemanticTokensProvider, LanguageMetadata>> semanticTokenProviders,
        Lazy<IOutputService> output)
    {
        var log = new FeatureLog(output);
        folding = new(foldingProviders, log, provider => provider.Changed += (_, e) => Changed?.Invoke(this, e));
        selection = new(selectionProviders, log);
        semanticTokens = new(semanticTokenProviders, log, provider => provider.Changed += (_, e) => Changed?.Invoke(this, e));
    }

    public event EventHandler<LanguageFeatureChangedEventArgs>? Changed;

    public Task<IReadOnlyList<FoldingRange>?> GetFoldingRangesAsync(IDocument document, TextSnapshot snapshot, CancellationToken cancellationToken) =>
        folding.FirstAsync(document, provider => provider.GetFoldingRangesAsync(document, snapshot, cancellationToken));

    public Task<IReadOnlyList<TextSpan>?> GetSelectionRangesAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken) =>
        selection.FirstAsync(document, provider => provider.GetSelectionRangesAsync(document, snapshot, offset, cancellationToken));

    public bool HasSemanticTokens(IDocument document) => semanticTokens.Serves(document);

    public Task<IReadOnlyList<SemanticToken>?> GetSemanticTokensAsync(IDocument document, TextSnapshot snapshot, TextSpan? span, CancellationToken cancellationToken) =>
        semanticTokens.FirstAsync(document, provider => span is { } range
            ? provider.GetSemanticTokensAsync(document, snapshot, range, cancellationToken)
            : provider.GetSemanticTokensAsync(document, snapshot, cancellationToken));
}
