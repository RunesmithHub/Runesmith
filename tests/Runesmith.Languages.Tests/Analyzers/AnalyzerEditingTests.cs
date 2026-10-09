using Runesmith.Languages.Analyzers;
using Runesmith.LanguageServices;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Settings;
using Runesmith.Text;

namespace Runesmith.Languages.Tests.Analyzers;

public sealed class AnalyzerEditingTests : IDisposable
{
    private const string FilePath = "/work/A.fake";

    private readonly EditingAnalyzer analyzer = new();
    private readonly TestDocuments documents = new();
    private readonly AnalyzerBridge bridge;
    private readonly AnalyzerEdits edits;
    private readonly string otherFile = Path.Combine(Path.GetTempPath(), $"runesmith-{Guid.NewGuid():N}.fake");

    public AnalyzerEditingTests()
    {
        bridge = new AnalyzerBridge([analyzer], documents, new TestWorkspace(), new TestDiagnostics(), new TestOutput());
        edits = new AnalyzerEdits(bridge, documents);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        bridge.Dispose();
        File.Delete(otherFile);
    }

    [Fact]
    public async Task QuickFixesComeAloneUntilTheUserAsksAndResolveToAnEditOfTheOpenDocument()
    {
        var document = new TestDocument(FilePath, "A.fake", "var x = 1;");
        documents.Add(document);
        var provider = new AnalyzerCodeActionProvider(bridge, edits);
        var snapshot = document.Buffer.Current;

        var automatic = await provider.GetCodeActionsAsync(new CodeActionRequest(document, snapshot, new TextSpan(0, 10), [], CodeActionTrigger.Automatic), Token);
        var invoked = await provider.GetCodeActionsAsync(new CodeActionRequest(document, snapshot, new TextSpan(0, 10), [], CodeActionTrigger.Invoked), Token);

        Assert.Equal(["Fix it"], automatic.Select(a => a.Title));
        Assert.Equal([CodeActionKind.QuickFix, CodeActionKind.Refactor], invoked.Select(a => a.Kind));
        Assert.True(invoked[0].IsPreferred);

        var resolved = await provider.ResolveAsync(automatic[0], Token);
        var edit = Assert.Single(resolved.Edit!.Documents);
        Assert.Same(snapshot, edit.Snapshot);
        Assert.Equal(new TextChange(new TextSpan(4, 1), "fixed"), Assert.Single(edit.Changes));

        var refused = await Assert.ThrowsAsync<LanguageFeatureException>(() => provider.ResolveAsync(invoked[1], Token));
        Assert.Equal("This change adds files.", refused.Message);
    }

    [Fact]
    public async Task AnEditOfAnOlderVersionMovesThroughTheTypingSince()
    {
        var document = new TestDocument(FilePath, "A.fake", "var x = 1;");
        documents.Add(document);
        var version = bridge.Host.GetDocument(FilePath)!.Version;
        document.Buffer.Insert(0, "// note\n");

        var edit = edits.ToWorkspaceEdit([new FileEdit(FilePath, version, [new PositionEdit(new TextPosition(0, 4), new TextPosition(0, 5), "y")])]);

        var change = Assert.Single(Assert.Single(edit.Documents).Changes);
        Assert.Equal(new TextSpan(12, 1), change.Span);
        Assert.Same(document.Buffer.Current, edit.Documents[0].Snapshot);
    }

    [Fact]
    public void AnEditOfAFileThatIsNotOpenCountsItsLinesWithoutCarriageReturns()
    {
        File.WriteAllText(otherFile, "first\r\nsecond\r\n");

        var edit = edits.ToWorkspaceEdit([new FileEdit(otherFile, null, [new PositionEdit(new TextPosition(1, 0), new TextPosition(1, 6), "2nd\r\n")])]);

        var document = Assert.Single(edit.Documents);
        Assert.Null(document.Snapshot);
        Assert.Equal(new TextChange(new TextSpan(6, 6), "2nd\n"), Assert.Single(document.Changes));
    }

    [Fact]
    public async Task RenameRefusalsReachTheUserAndRenamesBecomeWorkspaceEdits()
    {
        var document = new TestDocument(FilePath, "A.fake", "count = count + 1;");
        documents.Add(document);
        var provider = new AnalyzerRenameProvider(bridge, edits);
        var snapshot = document.Buffer.Current;

        var target = await provider.PrepareRenameAsync(document, snapshot, 2, Token);
        var refusal = await Assert.ThrowsAsync<LanguageFeatureException>(() => provider.PrepareRenameAsync(document, snapshot, 6, Token));
        var rename = await provider.RenameAsync(document, snapshot, 2, "total", Token);

        Assert.Equal(new RenameTarget(new TextSpan(0, 5), "count"), target);
        Assert.Equal("Operators cannot be renamed.", refusal.Message);
        Assert.Equal(["total", "total"], Assert.Single(rename!.Documents).Changes.Select(c => c.NewText));
    }

    [Fact]
    public async Task FormattingAnswersOnlyForTheSnapshotItWasAskedAbout()
    {
        var document = new TestDocument(FilePath, "A.fake", "x  =  1;");
        documents.Add(document);
        var provider = new AnalyzerFormattingProvider(bridge);
        var snapshot = document.Buffer.Current;

        var changes = await provider.FormatDocumentAsync(document, snapshot, new FormattingOptions(2, true), Token);
        var stale = await provider.FormatDocumentAsync(document, TextSnapshot.Create("other"), new FormattingOptions(2, true), Token);

        Assert.Equal("2 True", analyzer.LastFormatting);
        Assert.Equal([new TextChange(new TextSpan(1, 2), " "), new TextChange(new TextSpan(4, 2), " ")], changes);
        Assert.Null(stale);
    }

    [Fact]
    public async Task InlayHintsFollowTheSettingAndBecomeTypeHintsAfterTheirText()
    {
        var document = new TestDocument(FilePath, "A.fake", "count = 1;");
        documents.Add(document);
        var settings = new TestSettings();
        var provider = new AnalyzerDecorationProvider(bridge, settings);
        var changes = 0;
        provider.Changed += (_, _) => changes++;
        var request = new DecorationRequest(document, document.Buffer.Current);

        var off = await provider.GetDecorationsAsync(request, Token);
        settings.Set(SettingKeys.InlayHints, true);
        var on = await provider.GetDecorationsAsync(request, Token);

        Assert.Empty(off);
        Assert.Equal(1, changes);
        var hint = Assert.IsType<InlayHint>(Assert.Single(on));
        Assert.Equal((5, ": int", InlayHintSide.After, InlayHintKind.Type), (hint.Offset, hint.Text, hint.Side, hint.Kind));
    }

    /// <summary>An analyzer with one quick fix and one refactoring that refuses to resolve, renames that refuse at <c>=</c>, formatting that
    /// collapses double spaces, and a type hint after the first word.</summary>
    private sealed class EditingAnalyzer : ILanguageAnalyzer, ICodeActionAnalyzer, IRenameAnalyzer, IFormattingAnalyzer, IInlayHintAnalyzer
    {
        public string? LastFormatting { get; private set; }

        public IReadOnlyList<string> LanguageIds => ["fake"];

        public IReadOnlyList<char> CompletionTriggerCharacters => [];

        public IReadOnlyList<char> SignatureHelpTriggerCharacters => [];

        public IReadOnlyList<char> SignatureHelpRetriggerCharacters => [];

        public void Initialize(AnalyzerContext context)
        {
        }

        public Task OpenWorkspaceAsync(string? rootPath, CancellationToken cancellationToken) => Task.CompletedTask;

        public void Open(SourceDocument document)
        {
        }

        public void Change(SourceDocument document, IReadOnlyList<TextChange> changes)
        {
        }

        public void Close(string path)
        {
        }

        public ValueTask CompleteAsync(CompletionQuery query, CompletionSink sink, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask<CompletionDetails?> ResolveAsync(CompletionEntry entry, CancellationToken cancellationToken) => ValueTask.FromResult<CompletionDetails?>(null);

        public ValueTask<HoverResult?> HoverAsync(DocumentPosition position, CancellationToken cancellationToken) => ValueTask.FromResult<HoverResult?>(null);

        public ValueTask<IReadOnlyList<SourceLocation>> DefinitionAsync(DocumentPosition position, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<SourceLocation>>([]);

        public ValueTask<SignatureHelpResult?> SignatureHelpAsync(DocumentPosition position, CancellationToken cancellationToken) =>
            ValueTask.FromResult<SignatureHelpResult?>(null);

        public ValueTask<IReadOnlyList<Problem>> DiagnosticsAsync(SourceDocument document, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<Problem>>([]);

        public ValueTask<IReadOnlyList<CodeActionEntry>> CodeActionsAsync(SourceDocument document, TextSpan span, bool includeRefactorings, CancellationToken cancellationToken)
        {
            var fix = new CodeActionEntry("Fix it", IsRefactoring: false, document) { IsPreferred = true };
            return ValueTask.FromResult<IReadOnlyList<CodeActionEntry>>(includeRefactorings ? [fix, new CodeActionEntry("Restructure", IsRefactoring: true, document)] : [fix]);
        }

        public ValueTask<IReadOnlyList<FileEdit>> ResolveCodeActionAsync(CodeActionEntry entry, CancellationToken cancellationToken)
        {
            if (entry.IsRefactoring)
                throw new AnalyzerRefusalException("This change adds files.");

            var document = (SourceDocument)entry.Token;
            return ValueTask.FromResult<IReadOnlyList<FileEdit>>(
                [new FileEdit(document.Path, document.Version, [new PositionEdit(new TextPosition(0, 4), new TextPosition(0, 5), "fixed")])]);
        }

        public ValueTask<RenameSite?> PrepareRenameAsync(DocumentPosition position, CancellationToken cancellationToken)
        {
            var text = position.Document.Snapshot.GetText();
            if (text[position.Offset] == '=')
                throw new AnalyzerRefusalException("Operators cannot be renamed.");
            var word = WordBoundaries.GetWordAt(position.Document.Snapshot, position.Offset);
            return ValueTask.FromResult<RenameSite?>(new RenameSite(word, text.Substring(word.Start, word.Length)));
        }

        public ValueTask<IReadOnlyList<FileEdit>> RenameAsync(DocumentPosition position, string newName, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<FileEdit>>(
            [
                new FileEdit(position.Document.Path, position.Document.Version,
                [
                    new PositionEdit(new TextPosition(0, 0), new TextPosition(0, 5), newName),
                    new PositionEdit(new TextPosition(0, 8), new TextPosition(0, 13), newName),
                ]),
            ]);

        public ValueTask<IReadOnlyList<TextChange>> FormatAsync(SourceDocument document, TextSpan? span, int tabSize, bool insertSpaces, CancellationToken cancellationToken)
        {
            LastFormatting = $"{tabSize} {insertSpaces}";
            var text = document.Snapshot.GetText();
            var changes = new List<TextChange>();
            for (var at = text.IndexOf("  ", StringComparison.Ordinal); at >= 0; at = text.IndexOf("  ", at + 2, StringComparison.Ordinal))
                changes.Add(new TextChange(new TextSpan(at, 2), " "));
            return ValueTask.FromResult<IReadOnlyList<TextChange>>(changes);
        }

        public ValueTask<IReadOnlyList<InlayHintEntry>> InlayHintsAsync(SourceDocument document, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<InlayHintEntry>>([new InlayHintEntry(WordBoundaries.GetWordAt(document.Snapshot, 0).End, ": int", IsType: true)]);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
