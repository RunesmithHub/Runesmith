using Runesmith.Languages.Features;
using Runesmith.Languages.Tests.Analyzers;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Text;
using static Runesmith.Languages.Tests.Features.NavigationFeaturesTests;

namespace Runesmith.Languages.Tests.Features;

public sealed class SyntaxFeaturesTests
{
    private static readonly TestDocument Document = new("/work/A.fake", "A.fake", "class A {\n    int x;\n}\n");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static TextSnapshot Snapshot => Document.Buffer.Current;

    [Fact]
    public async Task TheFirstProviderThatAnswersGivesTheFoldingAndSelectionRanges()
    {
        var silent = new Structure();
        var specific = new Structure { Folding = [new FoldingRange(0, 1)], Selection = [new TextSpan(10, 3), new TextSpan(0, 20)] };
        var features = Create(
            folding: [Export<IFoldingRangeProvider>(new Structure { Folding = [] }, LanguagesAttribute.Any), Export<IFoldingRangeProvider>(silent, "fake"), Export<IFoldingRangeProvider>(specific, "fake")],
            selection: [Export<ISelectionRangeProvider>(specific, "fake")]);

        Assert.Equal(new FoldingRange(0, 1), Assert.Single((await features.GetFoldingRangesAsync(Document, Snapshot, Token))!));
        Assert.Equal([new TextSpan(10, 3), new TextSpan(0, 20)], await features.GetSelectionRangesAsync(Document, Snapshot, 11, Token));
        Assert.Equal(1, silent.Asked);
        Assert.Null(await Create().GetFoldingRangesAsync(Document, Snapshot, Token));
        Assert.Null(await Create().GetSelectionRangesAsync(Document, Snapshot, 0, Token));
    }

    [Fact]
    public async Task AsksForTheTokensOfASpanAndAProviderWithoutRangesGivesThoseOfTheWholeDocumentThatTouchIt()
    {
        var provider = new Structure
        {
            Tokens = [new SemanticToken(new TextSpan(6, 1), SemanticTokenTypes.Class), new SemanticToken(new TextSpan(18, 1), SemanticTokenTypes.Property, SemanticTokenModifiers.Declaration)],
        };
        var features = Create(tokens: [Export<ISemanticTokensProvider>(provider, "fake")]);

        var all = await features.GetSemanticTokensAsync(Document, Snapshot, null, Token);
        var inRange = await features.GetSemanticTokensAsync(Document, Snapshot, new TextSpan(10, 12), Token);

        Assert.True(features.HasSemanticTokens(Document));
        Assert.False(features.HasSemanticTokens(new TestDocument("/work/B.cs", "B.cs", "", "csharp")));
        Assert.Equal(2, all!.Count);
        Assert.Equal(SemanticTokenTypes.Property, Assert.Single(inRange!).Type);
    }

    [Fact]
    public async Task FoldingAndTokenProvidersThatChangeArePassedOn()
    {
        var folding = new Structure { Folding = [] };
        var tokens = new Structure { Tokens = [] };
        var features = Create(folding: [Export<IFoldingRangeProvider>(folding, "fake")], tokens: [Export<ISemanticTokensProvider>(tokens, "fake")]);
        var raised = 0;
        features.Changed += (_, _) => raised++;

        await features.GetFoldingRangesAsync(Document, Snapshot, Token);
        await features.GetSemanticTokensAsync(Document, Snapshot, null, Token);
        folding.Raise();
        tokens.Raise();

        Assert.Equal(2, raised);
    }

    private static SyntaxFeatures Create(
        IEnumerable<Lazy<IFoldingRangeProvider, LanguageMetadata>>? folding = null,
        IEnumerable<Lazy<ISelectionRangeProvider, LanguageMetadata>>? selection = null,
        IEnumerable<Lazy<ISemanticTokensProvider, LanguageMetadata>>? tokens = null) =>
        new(folding ?? [], selection ?? [], tokens ?? [], new Lazy<Sdk.Shell.IOutputService>(() => new FeatureOutput()));

    private sealed class Structure : IFoldingRangeProvider, ISelectionRangeProvider, ISemanticTokensProvider
    {
        public IReadOnlyList<FoldingRange>? Folding { get; init; }

        public IReadOnlyList<TextSpan>? Selection { get; init; }

        public IReadOnlyList<SemanticToken>? Tokens { get; init; }

        public int Asked { get; private set; }

        public event EventHandler<LanguageFeatureChangedEventArgs>? Changed;

        public void Raise() => Changed?.Invoke(this, new LanguageFeatureChangedEventArgs(null));

        public Task<IReadOnlyList<FoldingRange>?> GetFoldingRangesAsync(IDocument document, TextSnapshot snapshot, CancellationToken cancellationToken)
        {
            Asked++;
            return Task.FromResult(Folding);
        }

        public Task<IReadOnlyList<TextSpan>?> GetSelectionRangesAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken) =>
            Task.FromResult(Selection);

        public Task<IReadOnlyList<SemanticToken>?> GetSemanticTokensAsync(IDocument document, TextSnapshot snapshot, CancellationToken cancellationToken) =>
            Task.FromResult(Tokens);
    }
}
