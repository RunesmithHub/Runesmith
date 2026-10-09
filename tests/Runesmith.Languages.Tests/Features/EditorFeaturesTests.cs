using Runesmith.Languages.Features;
using Runesmith.Languages.Tests.Analyzers;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Shell;
using Runesmith.Text;

namespace Runesmith.Languages.Tests.Features;

public sealed class EditorFeaturesTests
{
    private static readonly TestDocument Document = new("/work/A.fake", "A.fake", "text");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FindsOnlyTheProvidersOfTheDocumentsLanguageAndMergesTheirActions()
    {
        var features = Create(
            codeActions:
            [
                Export<ICodeActionProvider>(new Actions(new CodeAction("Refactor", CodeActionKind.Refactor), new CodeAction("Fix", CodeActionKind.QuickFix)), "fake"),
                Export<ICodeActionProvider>(new Actions(new CodeAction("Other language", CodeActionKind.QuickFix)), "csharp"),
                Export<ICodeActionProvider>(new Actions(new CodeAction("Preferred", CodeActionKind.Refactor) { IsPreferred = true }), LanguagesAttribute.Any),
            ]);

        var actions = await features.GetCodeActionsAsync(Request(), Token);

        Assert.Equal(["Preferred", "Fix", "Refactor"], actions.Select(a => a.Title));
        Assert.All(actions, action => Assert.NotNull(action.Provider));
        Assert.All(actions, action => Assert.Null(action.Source));
    }

    [Fact]
    public async Task AProviderThatFailsIsLoggedAndLeftOut()
    {
        var output = new RecordingOutput();
        var features = Create(
            codeActions:
            [
                Export<ICodeActionProvider>(new Actions { Throws = true }, "fake"),
                new Lazy<ICodeActionProvider, LanguageMetadata>(() => throw new InvalidOperationException("cannot create"), Metadata("fake")),
                Export<ICodeActionProvider>(new Actions(new CodeAction("Works", CodeActionKind.QuickFix)), "fake"),
            ],
            output: output);

        var actions = await features.GetCodeActionsAsync(Request(), Token);

        Assert.Equal(["Works"], actions.Select(a => a.Title));
        Assert.Contains(output.Lines, line => line.Contains("broken provider", StringComparison.Ordinal));
        Assert.Contains(output.Lines, line => line.Contains("cannot create", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheFirstProviderThatAnswersRenamesAndFormatsAndNamedLanguagesComeFirst()
    {
        var generic = new Refactorer("generic");
        var specific = new Refactorer("specific");
        var silent = new Refactorer(null);
        var features = Create(
            rename: [Export<IRenameProvider>(generic, LanguagesAttribute.Any), Export<IRenameProvider>(silent, "fake"), Export<IRenameProvider>(specific, "fake")],
            formatting: [Export<IDocumentFormattingProvider>(generic, LanguagesAttribute.Any), Export<IDocumentFormattingProvider>(specific, "fake")]);
        var snapshot = Document.Buffer.Current;

        var target = await features.PrepareRenameAsync(Document, snapshot, 0, Token);
        var changes = await features.FormatDocumentAsync(Document, snapshot, new FormattingOptions(4, true), Token);

        Assert.Equal("specific", target?.Name);
        Assert.Equal("specific", Assert.Single(changes!).NewText);
        Assert.Equal(1, silent.Asked);
        Assert.Equal(0, generic.Asked);
    }

    [Fact]
    public async Task RefusalsPassThroughToTheEditor()
    {
        var features = Create(rename: [Export<IRenameProvider>(new Refactorer("x") { Refuses = true }, "fake")]);

        var refusal = await Assert.ThrowsAsync<LanguageFeatureException>(() => features.RenameAsync(Document, Document.Buffer.Current, 0, "y", Token));

        Assert.Equal("Cannot rename this.", refusal.Message);
    }

    [Fact]
    public async Task DecorationProvidersAreGuardedAndKeepTheirIdentity()
    {
        var output = new RecordingOutput();
        var broken = new BrokenDecorations();
        var features = Create(decorations: [Export<IDecorationProvider>(broken, "fake"), Export<IDecorationProvider>(new BrokenDecorations(), "csharp")], output: output);

        var providers = features.GetDecorationProviders(Document);
        var decorations = await providers[0].GetDecorationsAsync(new DecorationRequest(Document, Document.Buffer.Current), Token);
        var raised = 0;
        providers[0].Changed += (_, _) => raised++;
        broken.Raise();

        Assert.Single(providers);
        Assert.Same(providers[0], features.GetDecorationProviders(Document)[0]);
        Assert.Empty(decorations);
        Assert.Equal(1, raised);
        Assert.Contains(output.Lines, line => line.Contains("no decorations today", StringComparison.Ordinal));
    }

    private static CodeActionRequest Request() => new(Document, Document.Buffer.Current, new TextSpan(0, 4), [], CodeActionTrigger.Invoked);

    private static EditorFeatures Create(
        IEnumerable<Lazy<ICodeActionProvider, LanguageMetadata>>? codeActions = null,
        IEnumerable<Lazy<IRenameProvider, LanguageMetadata>>? rename = null,
        IEnumerable<Lazy<IDocumentFormattingProvider, LanguageMetadata>>? formatting = null,
        IEnumerable<Lazy<IDecorationProvider, LanguageMetadata>>? decorations = null,
        IOutputService? output = null) =>
        new(codeActions ?? [], rename ?? [], formatting ?? [], [], decorations ?? [], new Lazy<IOutputService>(() => output ?? new RecordingOutput()));

    private static Lazy<T, LanguageMetadata> Export<T>(T value, params string[] languages) => new(() => value, Metadata(languages));

    private static LanguageMetadata Metadata(params string[] languages) => new() { LanguageIds = languages };

    private sealed class Actions(params CodeAction[] actions) : ICodeActionProvider
    {
        public bool Throws { get; init; }

        public Task<IReadOnlyList<CodeAction>> GetCodeActionsAsync(CodeActionRequest request, CancellationToken cancellationToken) =>
            Throws ? throw new InvalidOperationException("broken provider") : Task.FromResult<IReadOnlyList<CodeAction>>(actions);
    }

    private sealed class Refactorer(string? name) : IRenameProvider, IDocumentFormattingProvider
    {
        public int Asked { get; private set; }

        public bool Refuses { get; init; }

        public Task<RenameTarget?> PrepareRenameAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken)
        {
            Asked++;
            return Task.FromResult(name is null ? null : new RenameTarget(new TextSpan(0, 1), name));
        }

        public Task<WorkspaceEdit?> RenameAsync(IDocument document, TextSnapshot snapshot, int offset, string newName, CancellationToken cancellationToken) =>
            Refuses ? throw new LanguageFeatureException("Cannot rename this.") : Task.FromResult<WorkspaceEdit?>(WorkspaceEdit.Empty);

        public Task<IReadOnlyList<TextChange>?> FormatDocumentAsync(IDocument document, TextSnapshot snapshot, FormattingOptions options, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TextChange>?>(name is null ? null : [new TextChange(new TextSpan(0, 0), name)]);
    }

    private sealed class BrokenDecorations : IDecorationProvider
    {
        public event EventHandler<DecorationsChangedEventArgs>? Changed;

        public Task<IReadOnlyList<Decoration>> GetDecorationsAsync(DecorationRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("no decorations today");

        public void Raise() => Changed?.Invoke(this, new DecorationsChangedEventArgs(null));
    }

    private sealed class RecordingOutput : IOutputService
    {
        public List<string> Lines { get; } = [];

        public IOutputChannel GetChannel(string name) => new Channel(this, name);

        private sealed class Channel(RecordingOutput owner, string name) : IOutputChannel
        {
            public string Name => name;

            public void Append(string text) => owner.Lines.Add(text);

            public void AppendLine(string line) => owner.Lines.Add(line);

            public void Clear()
            {
            }

            public void Show()
            {
            }
        }
    }
}
