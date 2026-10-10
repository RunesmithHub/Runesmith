using Avalonia.Controls;
using Runesmith.Editor.Navigation;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Text;

namespace Runesmith.Editor.Tests;

public sealed class NavigationTests
{
    private const string Code = "int count = 0;\ncount = count + 1;\nPrint(count);";

    private static readonly string FilePath = Path.Combine(Path.GetTempPath(), "runesmith-tests", "Navigation.cs");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheOccurrencesOfTheSymbolUnderTheCaretAreHighlightedAMomentAfterItStops() => Run(async () =>
    {
        var fakes = new FakeEditorServices();
        fakes.Navigation.Highlights = _ =>
        [
            new DocumentHighlight(new TextSpan(4, 5), DocumentHighlightKind.Write),
            new DocumentHighlight(new TextSpan(15, 5), DocumentHighlightKind.Write),
            new DocumentHighlight(new TextSpan(23, 5), DocumentHighlightKind.Read),
        ];
        using var editor = new TextEditor(new FileTestDocument(FilePath, Code), fakes.Create());
        var window = Show(editor);

        editor.CaretOffset = 6;
        Assert.Equal(0, fakes.Navigation.HighlightRequests);
        await WaitAsync(() => editor.Area.Decorations.Count == 3);
        var highlights = editor.Area.Decorations.All<TextHighlight>().Select(p => (TextHighlight)p.Decoration).ToList();
        Assert.All(highlights, h => Assert.True(h.Background && h.Underline == UnderlineStyle.None));
        Assert.Equal([DecorationTone.Information, DecorationTone.Information, DecorationTone.Neutral], highlights.Select(h => h.Tone));

        editor.CaretOffset = 24;
        Assert.Equal(3, editor.Area.Decorations.Count);
        editor.Area.Select(new EditorSelection(0, 3));
        Assert.Equal(0, editor.Area.Decorations.Count);
        editor.CaretOffset = 14;
        await Task.Delay(OccurrenceController.Delay * 3, Token);
        Assert.Equal(0, editor.Area.Decorations.Count);
        window.Close();
    });

    [Fact]
    public Task ExpandSelectionGrowsThroughTheProvidersSpansAndShrinkGoesBack() => Run(async () =>
    {
        var fakes = new FakeEditorServices();
        fakes.Syntax.SelectionRanges = (snapshot, offset) => [new TextSpan(15, 5), new TextSpan(15, 17), new TextSpan(15, 18), new TextSpan(0, snapshot.Length)];
        using var editor = new TextEditor(new FileTestDocument(FilePath, Code), fakes.Create());
        editor.CaretOffset = 17;

        await editor.ExpandSelectionAsync();
        Assert.Equal(new TextSpan(15, 5), editor.Selection);
        await editor.ExpandSelectionAsync();
        Assert.Equal(new TextSpan(15, 17), editor.Selection);
        await editor.ExpandSelectionAsync();
        await editor.ExpandSelectionAsync();
        Assert.Equal(new TextSpan(0, Code.Length), editor.Selection);
        await editor.ExpandSelectionAsync();
        Assert.Equal(new TextSpan(0, Code.Length), editor.Selection);

        editor.ShrinkSelection();
        Assert.Equal(new TextSpan(15, 18), editor.Selection);
        editor.ShrinkSelection();
        editor.ShrinkSelection();
        editor.ShrinkSelection();
        Assert.Equal(new TextSpan(17, 0), editor.Selection);
        editor.ShrinkSelection();
        Assert.Equal(new TextSpan(17, 0), editor.Selection);
    });

    [Fact]
    public void WithoutAProviderTheSelectionGrowsThroughTheWordBracketsAndLines()
    {
        var snapshot = TextSnapshot.Create("run(a, f(b + c));\nnext();");

        var spans = SelectionExpander.Fallback(snapshot, new TextSpan(9, 0)).OrderBy(s => s.Length).ToList();

        Assert.Equal(
            [new TextSpan(9, 1), new TextSpan(9, 5), new TextSpan(8, 7), new TextSpan(4, 11), new TextSpan(3, 13), new TextSpan(0, 17), new TextSpan(0, 18), new TextSpan(0, 25)],
            spans);
    }

    [Fact]
    public Task ReferencesAndImplementationsComeFromTheProvidersAndANewSearchCancelsTheLast() => Run(async () =>
    {
        var fakes = new FakeEditorServices();
        fakes.Navigation.References.Add(new DocumentLocation(FilePath, new TextPosition(1, 0), new TextPosition(1, 5)));
        fakes.Navigation.Implementations.Add(new DocumentLocation("/other.cs", new TextPosition(3, 2)));
        using var editor = new TextEditor(new FileTestDocument(FilePath, Code), fakes.Create());
        editor.CaretOffset = 6;

        var references = editor.FindReferencesAsync();
        var implementations = await editor.FindImplementationsAsync();

        Assert.Equal("count", editor.WordAtCaret());
        Assert.Null(await references);
        Assert.Equal("/other.cs", Assert.Single(implementations!).FilePath);
        Assert.Single((await editor.FindReferencesAsync())!);
    });

    private static Window Show(Control content)
    {
        var window = new Window { Width = 600, Height = 400, Content = content };
        window.Show();
        return window;
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition was not met in time.");
            await Task.Delay(10, Token);
        }
    }

    private static Task<bool> Run(Func<Task> test) =>
        HeadlessSession.Value.Dispatch(async () =>
        {
            await test();
            return true;
        }, Token);
}
