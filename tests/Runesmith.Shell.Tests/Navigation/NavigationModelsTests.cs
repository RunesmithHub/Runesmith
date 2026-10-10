using Avalonia.Controls;
using Avalonia.Input;
using Runesmith.Editor.Tests;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Shell.Appearance;
using Runesmith.Shell.Navigation;
using Runesmith.Shell.Symbols;
using Runesmith.Text;

namespace Runesmith.Shell.Tests.Navigation;

public sealed class NavigationModelsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly IReadOnlyList<DocumentSymbol> Symbols =
    [
        new("Program", SymbolKind.Class, new TextSpan(0, 100), new TextSpan(6, 7))
        {
            Children =
            [
                new DocumentSymbol("Main", SymbolKind.Method, new TextSpan(20, 30), new TextSpan(25, 4)),
                new DocumentSymbol("Configure", SymbolKind.Method, new TextSpan(55, 40), new TextSpan(60, 9))
                {
                    Children = [new DocumentSymbol("builder", SymbolKind.Variable, new TextSpan(70, 10), new TextSpan(70, 7))],
                },
            ],
        },
        new("Helpers", SymbolKind.Class, new TextSpan(110, 20), new TextSpan(116, 7)),
    ];

    [Fact]
    public void TheOutlineFiltersKeepingParentsSortsByNameAndFindsTheSymbolAtAnOffset()
    {
        var model = new OutlineModel();
        model.SetSymbols(Symbols);

        Assert.Equal(["Program", "Helpers"], model.Roots.Select(n => n.Name));
        Assert.Equal(5, model.SymbolCount);
        Assert.Equal("Program/Configure/builder", model.NodeAt(72)!.Key);
        Assert.Equal("Program", model.NodeAt(52)!.Key);
        Assert.Null(model.NodeAt(105));

        model.Filter = "bld";
        var program = Assert.Single(model.Roots);
        var configure = Assert.Single(program.Children);
        Assert.Equal("builder", Assert.Single(configure.Children).Name);
        Assert.True(program.IsExpanded && configure.IsExpanded);

        model.Filter = "";
        model.Sort = OutlineSort.Name;
        Assert.Equal(["Helpers", "Program"], model.Roots.Select(n => n.Name));
        Assert.Equal(["Configure", "Main"], model.Roots[1].Children.Select(n => n.Name));
    }

    [Fact]
    public void TheOutlineKeepsRowsOpenOrClosedAcrossUpdatesOfTheSameFile()
    {
        var model = new OutlineModel();
        model.SetSymbols(Symbols);
        var configure = model.Roots[0].Children[1];
        Assert.True(configure.IsExpanded);

        configure.IsExpanded = false;
        model.SetSymbols(Symbols);
        Assert.False(model.Roots[0].Children[1].IsExpanded);

        model.SetSymbols(Symbols, sameFile: false);
        Assert.True(model.Roots[0].Children[1].IsExpanded);

        model.Roots[0].IsExpanded = false;
        OutlineModel.Reveal(model.Roots[0].Children[1].Children[0]);
        Assert.True(model.Roots[0].IsExpanded);
    }

    [Fact]
    public async Task ReferencesAreGroupedByFileWithTheirLinesFromOpenDocumentsOrTheDisk()
    {
        var folder = Directory.CreateTempSubdirectory("runesmith-references-").FullName;
        var onDisk = Path.Combine(folder, "src", "Disk.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(onDisk)!);
        await File.WriteAllTextAsync(onDisk, "class Disk\r\n{\r\n    Run();\r\n}\r\n", Token);
        var open = Path.Combine(folder, "Open.cs");
        var openText = TextSnapshot.Create("void Run()\n{\n}\n// Run again\n");
        var model = new ReferencesModel();

        await model.ShowAsync("References to Run", "reference",
        [
            new DocumentLocation(open, new TextPosition(3, 3), new TextPosition(3, 6)),
            new DocumentLocation(onDisk, new TextPosition(2, 4), new TextPosition(2, 7)),
            new DocumentLocation(open, new TextPosition(0, 5), new TextPosition(0, 8)),
            new DocumentLocation(Path.Combine(folder, "Gone.cs"), new TextPosition(9, 0)),
        ], path => path == open ? openText : null, folder, Token);

        Assert.Equal(("References to Run", "4 references in 3 files"), (model.Title, model.Summary));
        Assert.Equal(["Gone.cs", "Open.cs", "Disk.cs"], model.Groups.Select(g => g.Name));
        Assert.Equal("src", model.Groups[2].Folder);
        var openItems = model.Groups[1].Items;
        Assert.Equal([0, 3], openItems.Select(i => i.Line));
        Assert.Equal(("void Run()", 5, 3), (openItems[0].Preview, openItems[0].MatchStart, openItems[0].MatchLength));
        Assert.Equal(("Run();", 0, 3), (model.Groups[2].Items[0].Preview, model.Groups[2].Items[0].MatchStart, model.Groups[2].Items[0].MatchLength));
        Assert.Equal("", model.Groups[0].Items[0].Preview);

        Assert.Equal(9, model.Step(null, 1)!.Line);
        Assert.Same(openItems[0], model.Step(model.Groups[0].Items[0], 1));
        Assert.Same(model.Groups[0].Items[0], model.Step(model.Groups[2].Items[0], 1));
        Assert.Same(model.Groups[2].Items[0], model.Step(null, -1));
        model.Clear();
        Assert.Empty(model.Groups);
        Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public async Task TheHierarchyLoadsEachLevelWhenItOpensAndCanGrowTheOtherWay()
    {
        var features = new Hierarchies();
        var model = new HierarchyModel(features);
        var changes = 0;
        model.Changed += (_, _) => changes++;

        model.ShowCalls([Item("Run")]);
        var root = Assert.Single(model.Roots);
        await root.LoadChildrenAsync();

        Assert.Equal("Callers of Run", model.Title);
        Assert.Equal(["Main", "Test"], root.Children.Select(c => c.Item!.Name));
        Assert.Equal(2, root.Children[0].CallSites.Count);
        Assert.Equal(new TextPosition(4, 8), root.Children[0].Target!.Start);
        Assert.Equal("Loading...", Assert.Single(root.Children[0].Children).Message);
        Assert.Equal(1, features.Incoming);

        root.Children[1].IsExpanded = true;
        await root.Children[1].LoadChildrenAsync();
        Assert.Equal("No callers", Assert.Single(root.Children[1].Children).Message);

        model.SetDirection(HierarchyDirection.Callees);
        await model.Roots[0].LoadChildrenAsync();
        Assert.Equal(("Calls from Run", "Write"), (model.Title, model.Roots[0].Children[0].Item!.Name));

        model.ShowTypes([Item("Shape")]);
        await model.Roots[0].LoadChildrenAsync();
        Assert.Equal(("Subtypes of Shape", "Circle"), (model.Title, model.Roots[0].Children[0].Item!.Name));
        Assert.Equal(new TextPosition(1, 6), model.Roots[0].Children[0].Target!.Start);
        model.SetDirection(HierarchyDirection.Supertypes);
        await model.Roots[0].LoadChildrenAsync();
        Assert.Equal("No supertypes", model.Roots[0].Children[0].Message);
        Assert.Equal(4, changes);
    }

    [Fact]
    public Task TheReferencesPanelShowsThePlacesAndOpensTheSelectedOneOnEnter() => HeadlessSession.Value.Dispatch(async () =>
    {
        var model = new ReferencesModel();
        var revealed = new List<(DocumentLocation Location, bool Focus)>();
        var view = new ReferencesView(model, _ => new ThemedIcon(HammerUI.Icons.File), (location, focus) =>
        {
            revealed.Add((location, focus));
            return Task.CompletedTask;
        });
        var window = new Window { Width = 800, Height = 400, Content = view };
        window.Show();
        var place = new DocumentLocation("/work/A.cs", new TextPosition(0, 0), new TextPosition(0, 1));

        await model.ShowAsync("References to A", "reference", [place], _ => TextSnapshot.Create("A a;"), "/work", Token);
        view.Tree.SelectedItem = model.Groups[0].Items[0];
        view.Tree.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = view.Tree });

        Assert.Equal([(place, false), (place, true)], revealed);
        window.Close();
        return true;
    }, Token);

    [Fact]
    public Task TheSymbolPathFollowsTheCaretAndTheEditsBeforeTheNextAnswer() => HeadlessSession.Value.Dispatch(async () =>
    {
        var fakes = new FakeEditorServices();
        fakes.Navigation.Symbols = Symbols;
        using var editor = new Editor.TextEditor(new FileTestDocument("/work/Program.cs", new string('x', 140)), fakes.Create());
        var tracker = new DocumentSymbols(fakes.Navigation).For(editor);
        await WaitAsync(() => tracker.Symbols is not null);
        var path = new Shell.Views.SymbolBreadcrumbs(editor, tracker);

        editor.CaretOffset = 72;
        await WaitAsync(() => path.Names.Count == 3);
        Assert.Equal(["Program", "Configure", "builder"], path.Names);

        editor.Document.Buffer.Insert(0, "0123456789");
        Assert.Equal(["Program", "Configure", "builder"], tracker.PathAt(82).Select(s => s.Name));
        Assert.Equal(["Program", "Configure"], tracker.PathAt(72).Select(s => s.Name));
        tracker.Dispose();
        return true;
    }, Token);

    private static async Task WaitAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition was not met in time.");
            await Task.Delay(10, Token);
        }
    }

    private static HierarchyItem Item(string name, int line = 0) =>
        new(name, SymbolKind.Method, new DocumentLocation("/work/A.cs", new TextPosition(line, 0), new TextPosition(line + 2, 1)))
        {
            SelectionLocation = new DocumentLocation("/work/A.cs", new TextPosition(line, 6), new TextPosition(line, 6 + name.Length)),
        };

    private sealed class Hierarchies : FakeNavigationFeatures2
    {
        public int Incoming { get; private set; }

        public override Task<IReadOnlyList<HierarchyCall>> GetIncomingCallsAsync(HierarchyItem item, CancellationToken cancellationToken)
        {
            Incoming++;
            IReadOnlyList<HierarchyCall> calls = item.Name == "Run"
                ? [new HierarchyCall(Item("Main", 3), [At(4, 8), At(5, 8)]), new HierarchyCall(Item("Test", 9), [At(10, 4)])]
                : [];
            return Task.FromResult(calls);
        }

        public override Task<IReadOnlyList<HierarchyCall>> GetOutgoingCallsAsync(HierarchyItem item, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<HierarchyCall>>([new HierarchyCall(Item("Write", 20), [At(1, 4)])]);

        public override Task<IReadOnlyList<HierarchyItem>> GetSubtypesAsync(HierarchyItem item, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<HierarchyItem>>([Item("Circle", 1)]);

        private static DocumentLocation At(int line, int column) => new("/work/A.cs", new TextPosition(line, column));
    }
}

/// <summary>Navigation features whose hierarchy requests a test overrides; everything else answers nothing.</summary>
internal class FakeNavigationFeatures2 : INavigationFeatures
{
    public event EventHandler<LanguageFeatureChangedEventArgs>? Changed
    {
        add { }
        remove { }
    }

    public Task<IReadOnlyList<DocumentLocation>> GetReferencesAsync(IDocument document, TextSnapshot snapshot, int offset, bool includeDeclaration, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DocumentLocation>>([]);

    public Task<IReadOnlyList<DocumentLocation>> GetImplementationsAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DocumentLocation>>([]);

    public Task<IReadOnlyList<DocumentHighlight>> GetDocumentHighlightsAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DocumentHighlight>>([]);

    public Task<IReadOnlyList<DocumentSymbol>?> GetDocumentSymbolsAsync(IDocument document, TextSnapshot snapshot, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DocumentSymbol>?>(null);

    public Task<IReadOnlyList<WorkspaceSymbol>> GetWorkspaceSymbolsAsync(string query, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WorkspaceSymbol>>([]);

    public Task<IReadOnlyList<HierarchyItem>> PrepareCallHierarchyAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<HierarchyItem>>([]);

    public virtual Task<IReadOnlyList<HierarchyCall>> GetIncomingCallsAsync(HierarchyItem item, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<HierarchyCall>>([]);

    public virtual Task<IReadOnlyList<HierarchyCall>> GetOutgoingCallsAsync(HierarchyItem item, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<HierarchyCall>>([]);

    public Task<IReadOnlyList<HierarchyItem>> PrepareTypeHierarchyAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<HierarchyItem>>([]);

    public virtual Task<IReadOnlyList<HierarchyItem>> GetSupertypesAsync(HierarchyItem item, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<HierarchyItem>>([]);

    public virtual Task<IReadOnlyList<HierarchyItem>> GetSubtypesAsync(HierarchyItem item, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<HierarchyItem>>([]);
}
