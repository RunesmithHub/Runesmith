using Runesmith.Languages.Features;
using Runesmith.Languages.Tests.Analyzers;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Shell;
using Runesmith.Text;

namespace Runesmith.Languages.Tests.Features;

public sealed class NavigationFeaturesTests
{
    private static readonly TestDocument Document = new("/work/A.fake", "A.fake", "class A { void Run() { Run(); } }");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static TextSnapshot Snapshot => Document.Buffer.Current;

    [Fact]
    public async Task MergesTheReferencesAndImplementationsOfEveryProviderWithoutDuplicates()
    {
        var first = new Navigator { Locations = [At("/work/B.fake", 3), At("/work/A.fake", 1)] };
        var second = new Navigator { Locations = [At("/work/A.fake", 1), At("/work/A.fake", 0)] };
        var other = new Navigator { Locations = [At("/work/C.fake", 0)] };
        var features = Create(references: [Export<IReferenceProvider>(first, "fake"), Export<IReferenceProvider>(second, LanguagesAttribute.Any), Export<IReferenceProvider>(other, "csharp")],
            implementations: [Export<IImplementationProvider>(first, "fake")]);

        var references = await features.GetReferencesAsync(Document, Snapshot, 26, includeDeclaration: false, Token);
        var implementations = await features.GetImplementationsAsync(Document, Snapshot, 26, Token);

        Assert.Equal([("/work/A.fake", 0), ("/work/A.fake", 1), ("/work/B.fake", 3)], references.Select(l => (l.FilePath, l.Start.Line)));
        Assert.False(first.IncludedDeclaration);
        Assert.Equal(2, implementations.Count);
        Assert.Equal(0, other.Asked);
    }

    [Fact]
    public async Task TheFirstProviderThatAnswersGivesTheHighlightsAndTheSymbols()
    {
        var silent = new Navigator();
        var specific = new Navigator { Highlights = [new DocumentHighlight(new TextSpan(6, 1), DocumentHighlightKind.Write)], Symbols = [Symbol("A")] };
        var generic = new Navigator { Highlights = [new DocumentHighlight(new TextSpan(0, 1))], Symbols = [Symbol("generic")] };
        var features = Create(
            highlights: [Export<IDocumentHighlightProvider>(generic, LanguagesAttribute.Any), Export<IDocumentHighlightProvider>(silent, "fake"), Export<IDocumentHighlightProvider>(specific, "fake")],
            symbols: [Export<IDocumentSymbolProvider>(generic, LanguagesAttribute.Any), Export<IDocumentSymbolProvider>(specific, "fake")]);

        var highlights = await features.GetDocumentHighlightsAsync(Document, Snapshot, 6, Token);
        var symbols = await features.GetDocumentSymbolsAsync(Document, Snapshot, Token);

        Assert.Equal(DocumentHighlightKind.Write, Assert.Single(highlights).Kind);
        Assert.Equal("A", Assert.Single(symbols!).Name);
        Assert.Equal(1, silent.Asked);
        Assert.Equal(0, generic.Asked);
        Assert.Null(await Create().GetDocumentSymbolsAsync(Document, Snapshot, Token));
        Assert.Empty(await Create().GetDocumentHighlightsAsync(Document, Snapshot, 6, Token));
    }

    [Fact]
    public async Task AsksEveryWorkspaceSymbolProviderAndLeavesOutOneThatFails()
    {
        var output = new FeatureOutput();
        var features = Create(
            workspaceSymbols:
            [
                new Lazy<IWorkspaceSymbolProvider>(() => new Navigator { WorkspaceSymbols = [new WorkspaceSymbol("Run", SymbolKind.Method, At("/work/A.fake", 0))] }),
                new Lazy<IWorkspaceSymbolProvider>(() => new Navigator { Throws = true }),
                new Lazy<IWorkspaceSymbolProvider>(() => new Navigator { WorkspaceSymbols = [new WorkspaceSymbol("Runner", SymbolKind.Class, At("/work/B.fake", 2))] }),
            ],
            output: output);

        var symbols = await features.GetWorkspaceSymbolsAsync("Run", Token);

        Assert.Equal(["Run", "Runner"], symbols.Select(s => s.Name));
        Assert.Contains(output.Lines, line => line.Contains("broken navigator", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HierarchyItemsRememberTheirProviderWhichAnswersForThemAndTheirChildren()
    {
        var empty = new Navigator { PreparesNothing = true };
        var hierarchy = new Navigator();
        var features = Create(calls: [Export<ICallHierarchyProvider>(empty, "fake"), Export<ICallHierarchyProvider>(hierarchy, "fake")],
            types: [Export<ITypeHierarchyProvider>(hierarchy, LanguagesAttribute.Any)]);

        var root = Assert.Single(await features.PrepareCallHierarchyAsync(Document, Snapshot, 16, Token));
        var callers = await features.GetIncomingCallsAsync(root, Token);
        var callees = await features.GetOutgoingCallsAsync(callers[0].Item, Token);
        var type = Assert.Single(await features.PrepareTypeHierarchyAsync(Document, Snapshot, 6, Token));
        var subtypes = await features.GetSubtypesAsync(type, Token);

        Assert.Same(hierarchy, root.Provider);
        Assert.Equal(1, empty.Asked);
        Assert.Equal(("Main", 2), (callers[0].Item.Name, callers[0].CallSites.Count));
        Assert.Same(hierarchy, callers[0].Item.Provider);
        Assert.Equal("Run", Assert.Single(callees).Item.Name);
        Assert.Equal("B", Assert.Single(subtypes).Name);
        Assert.Same(hierarchy, subtypes[0].Provider);
        Assert.Empty(await features.GetSupertypesAsync(new HierarchyItem("X", SymbolKind.Class, At("/x", 0)), Token));
    }

    [Fact]
    public async Task ASymbolProviderThatChangesIsPassedOnOnceItServesADocument()
    {
        var provider = new Navigator { Symbols = [] };
        var features = Create(symbols: [Export<IDocumentSymbolProvider>(provider, "fake")]);
        var raised = new List<string?>();
        features.Changed += (_, e) => raised.Add(e.FilePath);

        provider.Raise("/before");
        await features.GetDocumentSymbolsAsync(Document, Snapshot, Token);
        await features.GetDocumentSymbolsAsync(Document, Snapshot, Token);
        provider.Raise("/work/A.fake");

        Assert.Equal(["/work/A.fake"], raised);
    }

    private static DocumentLocation At(string path, int line) => new(path, new TextPosition(line, 0), new TextPosition(line, 3));

    private static DocumentSymbol Symbol(string name) => new(name, SymbolKind.Class, new TextSpan(0, 10), new TextSpan(6, 1));

    private static NavigationFeatures Create(
        IEnumerable<Lazy<IReferenceProvider, LanguageMetadata>>? references = null,
        IEnumerable<Lazy<IImplementationProvider, LanguageMetadata>>? implementations = null,
        IEnumerable<Lazy<IDocumentHighlightProvider, LanguageMetadata>>? highlights = null,
        IEnumerable<Lazy<IDocumentSymbolProvider, LanguageMetadata>>? symbols = null,
        IEnumerable<Lazy<IWorkspaceSymbolProvider>>? workspaceSymbols = null,
        IEnumerable<Lazy<ICallHierarchyProvider, LanguageMetadata>>? calls = null,
        IEnumerable<Lazy<ITypeHierarchyProvider, LanguageMetadata>>? types = null,
        IOutputService? output = null) =>
        new(references ?? [], implementations ?? [], highlights ?? [], symbols ?? [], workspaceSymbols ?? [], calls ?? [], types ?? [],
            new Lazy<IOutputService>(() => output ?? new FeatureOutput()));

    internal static Lazy<T, LanguageMetadata> Export<T>(T value, params string[] languages) => new(() => value, new LanguageMetadata { LanguageIds = languages });

    private sealed class Navigator : IReferenceProvider, IImplementationProvider, IDocumentHighlightProvider, IDocumentSymbolProvider, IWorkspaceSymbolProvider,
        ICallHierarchyProvider, ITypeHierarchyProvider
    {
        public IReadOnlyList<DocumentLocation> Locations { get; init; } = [];

        public IReadOnlyList<DocumentHighlight>? Highlights { get; init; }

        public IReadOnlyList<DocumentSymbol>? Symbols { get; init; }

        public IReadOnlyList<WorkspaceSymbol> WorkspaceSymbols { get; init; } = [];

        public bool Throws { get; init; }

        public bool PreparesNothing { get; init; }

        public int Asked { get; private set; }

        public bool? IncludedDeclaration { get; private set; }

        public event EventHandler<LanguageFeatureChangedEventArgs>? Changed;

        public void Raise(string? path) => Changed?.Invoke(this, new LanguageFeatureChangedEventArgs(path));

        public Task<IReadOnlyList<DocumentLocation>> GetReferencesAsync(IDocument document, TextSnapshot snapshot, int offset, bool includeDeclaration, CancellationToken cancellationToken)
        {
            Asked++;
            IncludedDeclaration = includeDeclaration;
            return Task.FromResult(Locations);
        }

        public Task<IReadOnlyList<DocumentLocation>> GetImplementationsAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken) =>
            Task.FromResult(Locations);

        public Task<IReadOnlyList<DocumentHighlight>?> GetDocumentHighlightsAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken)
        {
            Asked++;
            return Task.FromResult(Highlights);
        }

        public Task<IReadOnlyList<DocumentSymbol>?> GetDocumentSymbolsAsync(IDocument document, TextSnapshot snapshot, CancellationToken cancellationToken)
        {
            Asked++;
            return Task.FromResult(Symbols);
        }

        public Task<IReadOnlyList<WorkspaceSymbol>> GetWorkspaceSymbolsAsync(string query, CancellationToken cancellationToken) =>
            Throws ? throw new InvalidOperationException("broken navigator") : Task.FromResult(WorkspaceSymbols);

        public Task<IReadOnlyList<HierarchyItem>> PrepareCallHierarchyAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken)
        {
            Asked++;
            return Task.FromResult<IReadOnlyList<HierarchyItem>>(PreparesNothing ? [] : [Item("Run", SymbolKind.Method)]);
        }

        public Task<IReadOnlyList<HierarchyCall>> GetIncomingCallsAsync(HierarchyItem item, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<HierarchyCall>>([new HierarchyCall(Item("Main", SymbolKind.Function), [At("/work/A.fake", 0), At("/work/A.fake", 1)])]);

        public Task<IReadOnlyList<HierarchyCall>> GetOutgoingCallsAsync(HierarchyItem item, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<HierarchyCall>>([new HierarchyCall(Item("Run", SymbolKind.Method), [At("/work/A.fake", 0)])]);

        public Task<IReadOnlyList<HierarchyItem>> PrepareTypeHierarchyAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<HierarchyItem>>([Item("A", SymbolKind.Class)]);

        public Task<IReadOnlyList<HierarchyItem>> GetSupertypesAsync(HierarchyItem item, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<HierarchyItem>>([]);

        public Task<IReadOnlyList<HierarchyItem>> GetSubtypesAsync(HierarchyItem item, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<HierarchyItem>>([Item("B", SymbolKind.Class)]);

        private static HierarchyItem Item(string name, SymbolKind kind) => new(name, kind, At("/work/A.fake", 0));
    }
}

/// <summary>Collects what the features write to the output.</summary>
internal sealed class FeatureOutput : IOutputService
{
    public List<string> Lines { get; } = [];

    public IOutputChannel GetChannel(string name) => new Channel(this, name);

    private sealed class Channel(FeatureOutput owner, string name) : IOutputChannel
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
