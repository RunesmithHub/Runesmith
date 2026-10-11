using Avalonia.Media;
using Avalonia.Threading;
using Runesmith.Languages.Highlighting;
using Runesmith.Languages.Tests.Analyzers;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Settings;
using Runesmith.Text;

namespace Runesmith.Languages.Tests.Highlighting;

public sealed class SemanticHighlightingTests
{
    private static readonly SyntaxStyle Keyword = new(Colors.Purple);
    private static readonly SyntaxStyle Type = new(Colors.Teal);

    private static SemanticTokenStyles DarkStyles()
    {
        var registry = new TextMateGrammars([]).GetRegistry(BuiltInColorSchemes.Dark);
        return new SemanticTokenStyles(registry.Theme, new StyleTable(registry.Theme, registry.DefaultForeground), registry.DefaultForeground);
    }

    [Fact]
    public void SemanticTokensReplaceTheTextMateTokensBeneathThem()
    {
        SyntaxToken[] below = [new(0, 5, Keyword), new(6, 6, Keyword)];
        OverlayToken[] above = [new(2, 2, Type), new(8, 3, Type)];

        var merged = SemanticHighlighter.Merge(below, above, 14, null);

        Assert.Equal(
        [
            new SyntaxToken(0, 2, Keyword), new SyntaxToken(2, 2, Type), new SyntaxToken(4, 1, Keyword), new SyntaxToken(6, 2, Keyword),
            new SyntaxToken(8, 3, Type), new SyntaxToken(11, 1, Keyword),
        ], merged);
    }

    [Fact]
    public void AStrikeOnlyTokenStrikesTheTokensBeneathAndThePlainTextBetweenThem()
    {
        var plain = new SyntaxStyle(Colors.White) { IsStrikethrough = true };

        var merged = SemanticHighlighter.Merge([new SyntaxToken(2, 2, Keyword)], [new OverlayToken(0, 6, null), new OverlayToken(20, 3, Type)], 8, plain);

        Assert.Equal([new SyntaxToken(0, 2, plain), new SyntaxToken(2, 2, Keyword with { IsStrikethrough = true }), new SyntaxToken(4, 2, plain)], merged);
    }

    [Fact]
    public void NeighborsDrawnAlikeBecomeOneTokenAndEmptyTokensAreDropped()
    {
        SyntaxToken[] below = [new(0, 3, Keyword), new(3, 0, Type), new(3, 2, Keyword), new(5, 4, Keyword), new(10, 2, Keyword)];

        var merged = SemanticHighlighter.Merge(below, [new OverlayToken(6, 1, Keyword), new OverlayToken(10, 1, Type)], 12, null);

        Assert.Equal([new SyntaxToken(0, 9, Keyword), new SyntaxToken(10, 1, Type), new SyntaxToken(11, 1, Keyword)], merged);
    }

    [Fact]
    public Task TokensGivenOutOfOrderGoToTheirLinesInOrder() => HeadlessSession.Value.Dispatch(async () =>
    {
        var document = new TestDocument("/work/A.fake", "A.fake", "let a = b\nlet c = d");
        var features = new Tokens(_ =>
        [
            new SemanticToken(new TextSpan(18, 1), SemanticTokenTypes.Class), new SemanticToken(new TextSpan(8, 1), SemanticTokenTypes.Class),
            new SemanticToken(new TextSpan(4, 1), SemanticTokenTypes.Class), new SemanticToken(new TextSpan(14, 1), SemanticTokenTypes.Class),
        ]);
        using var highlighter = new SemanticHighlighter(new Below(), document, features, DarkStyles);

        await WaitAsync(() => highlighter.OverlayOf(1).Count > 0);
        Assert.Equal([4, 8], highlighter.OverlayOf(0).Select(t => t.Start));
        Assert.Equal([4, 8], highlighter.OverlayOf(1).Select(t => t.Start));
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public void TypesAndModifiersTakeTheColorsOfTheirScopesInTheScheme()
    {
        var styles = DarkStyles();

        Assert.Equal(Color.Parse("#56C8D8"), styles.Get(SemanticTokenTypes.Class, SemanticTokenModifiers.None)!.Value.Style!.Foreground);
        Assert.Equal(Color.Parse("#7AA7FF"), styles.Get(SemanticTokenTypes.Method, SemanticTokenModifiers.Declaration)!.Value.Style!.Foreground);
        Assert.Equal(Color.Parse("#E6D2B5"), styles.Get(SemanticTokenTypes.Parameter, SemanticTokenModifiers.None)!.Value.Style!.Foreground);
        Assert.Equal(Color.Parse("#F2A65A"), styles.Get(SemanticTokenTypes.Variable, SemanticTokenModifiers.Readonly)!.Value.Style!.Foreground);
        Assert.True(styles.Get(SemanticTokenTypes.Interface, SemanticTokenModifiers.None)!.Value.Style!.IsItalic);
        Assert.True(styles.Get(SemanticTokenTypes.Function, SemanticTokenModifiers.Deprecated)!.Value.Style!.IsStrikethrough);
        Assert.Null(styles.Get("lifetime", SemanticTokenModifiers.None));
        Assert.Null(styles.Get("lifetime", SemanticTokenModifiers.Deprecated)!.Value.Style);
        Assert.Equal(["support.class", "support.type", "entity.name.type.class", "entity.name.type"],
            SemanticTokenStyles.ScopesOf(SemanticTokenTypes.Class, SemanticTokenModifiers.DefaultLibrary));
    }

    [Fact]
    public Task TokensArriveOverTheTextMateTokensAndFollowEditsUntilTheNextAnswer() => HeadlessSession.Value.Dispatch(async () =>
    {
        var document = new TestDocument("/work/A.fake", "A.fake", "let shape = Circle()\nshape.area()");
        var features = new Tokens(snapshot => [new SemanticToken(new TextSpan(snapshot.GetText().IndexOf("Circle", StringComparison.Ordinal), 6), SemanticTokenTypes.Class)]);
        var inner = new Below();
        using var highlighter = new SemanticHighlighter(inner, document, features, DarkStyles);
        var changed = new List<(int, int)>();
        highlighter.Changed += (_, e) => changed.Add((e.FirstLine, e.LastLine));

        await WaitAsync(() => highlighter.OverlayOf(0).Count > 0);
        var tokens = highlighter.GetTokens(0);
        Assert.Contains(tokens, t => t.Start == 12 && t.Length == 6 && t.Style.Foreground == Color.Parse("#56C8D8"));
        Assert.Contains(tokens, t => t.Start == 0 && t.Length == 3 && ReferenceEquals(t.Style, Keyword));
        Assert.Equal([(0, 0)], changed);

        document.Buffer.Insert(4, "big");
        Assert.Equal(15, Assert.Single(highlighter.OverlayOf(0)).Start);
        document.Buffer.Insert(14, "\n");
        Assert.Empty(highlighter.OverlayOf(0));

        await WaitAsync(() => features.Requests >= 2 && highlighter.OverlayOf(1).Count > 0);
        Assert.Equal(1, highlighter.OverlayOf(1)[0].Start);
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task TheSettingTurnsTheTokensOfOpenDocumentsOffAndOnAgain() => HeadlessSession.Value.Dispatch(async () =>
    {
        var grammars = new TextMateGrammars([]);
        var settings = new TestSettings();
        settings.Set(SettingKeys.SemanticHighlighting, true);
        var features = new Tokens(snapshot => [new SemanticToken(new TextSpan(snapshot.GetText().IndexOf("shape", StringComparison.Ordinal), 5), SemanticTokenTypes.Variable)]);
        using var provider = new TextMateHighlighterProvider(grammars, new LanguageRegistry([], grammars), new SyntaxColors(grammars), new Lazy<ISyntaxFeatures>(() => features), settings);
        var document = new TestDocument("/work/A.fake", "A.fake", "let shape = 1");
        var highlighter = Assert.IsType<SemanticHighlighter>(provider.Create(document));
        await WaitAsync(() => highlighter.OverlayOf(0).Count > 0);

        settings.Set(SettingKeys.SemanticHighlighting, false);
        Assert.False(highlighter.IsEnabled);
        Assert.Empty(highlighter.OverlayOf(0));
        Assert.Empty(highlighter.GetTokens(0));
        var requests = features.Requests;
        document.Buffer.Insert(0, " ");
        await PumpAsync(SemanticHighlighter.RefreshDelay * 2);
        Assert.Equal(requests, features.Requests);

        settings.Set(SettingKeys.SemanticHighlighting, true);
        await WaitAsync(() => highlighter.OverlayOf(0).Count > 0);
        Assert.Equal(5, highlighter.OverlayOf(0)[0].Start);
        return true;
    }, TestContext.Current.CancellationToken);

    private static async Task PumpAsync(TimeSpan time)
    {
        var until = DateTime.UtcNow + time;
        while (DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private sealed class Below : ISyntaxHighlighter
    {
        public event EventHandler<HighlightingChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<SyntaxToken> GetTokens(int lineNumber) => lineNumber == 0 ? [new SyntaxToken(0, 3, Keyword)] : [];

        public void Prioritize(int firstLine, int lastLine)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class Tokens(Func<TextSnapshot, IReadOnlyList<SemanticToken>> answer) : ISyntaxFeatures
    {
        private int requests;

        public int Requests => Volatile.Read(ref requests);

        public event EventHandler<LanguageFeatureChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public Task<IReadOnlyList<FoldingRange>?> GetFoldingRangesAsync(IDocument document, TextSnapshot snapshot, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FoldingRange>?>(null);

        public Task<IReadOnlyList<TextSpan>?> GetSelectionRangesAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TextSpan>?>(null);

        public bool HasSemanticTokens(IDocument document) => true;

        public Task<IReadOnlyList<SemanticToken>?> GetSemanticTokensAsync(IDocument document, TextSnapshot snapshot, TextSpan? span, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requests);
            return Task.FromResult<IReadOnlyList<SemanticToken>?>(answer(snapshot));
        }
    }
}
