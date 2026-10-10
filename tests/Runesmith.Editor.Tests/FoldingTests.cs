using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Runesmith.Editor.Folding;
using Runesmith.Sdk.Languages;
using Runesmith.Text;

namespace Runesmith.Editor.Tests;

public sealed class FoldingTests
{
    private const string Code = """
        class A
        {
            void Run()
            {
                Work();
            }
        }
        // end
        """;

    private static readonly string FilePath = Path.Combine(Path.GetTempPath(), "runesmith-tests", "Folding.cs");

    [Fact]
    public void FoldingARegionHidesTheLinesAfterItsFirstUpToItsLast()
    {
        var model = new FoldingModel();
        model.SetRanges([new FoldingRange(1, 6), new FoldingRange(3, 5), new FoldingRange(3, 4), new FoldingRange(9, 12), new FoldingRange(4, 4)], 8);

        Assert.Equal([(1, 6), (3, 5)], model.Regions.Select(r => (r.StartLine, r.EndLine)));
        model.SetCollapsed(model.RegionAt(3)!, true);
        Assert.Equal([4, 5], Enumerable.Range(0, 8).Where(model.IsHidden));
        Assert.Equal(2, model.HiddenLineCount);
        Assert.Equal((6, 2), (model.NextVisible(3, 8), model.PreviousVisible(3)));
        Assert.Equal((6, 3), (model.NextVisible(3, 8), model.VisibleLineOf(5)));

        model.SetCollapsed(model.RegionAt(1)!, true);
        Assert.Equal([2, 3, 4, 5, 6], Enumerable.Range(0, 8).Where(model.IsHidden));
        Assert.Equal((0, 0, 5), (model.HiddenBefore(1), model.HiddenBefore(2), model.HiddenBefore(7)));

        Assert.True(model.Reveal(4));
        Assert.DoesNotContain(Enumerable.Range(0, 8), model.IsHidden);
        Assert.Equal(1, model.Innermost(2, collapsed: false)!.StartLine);
        Assert.Equal(3, model.Innermost(4, collapsed: false)!.StartLine);
    }

    [Fact]
    public void FoldedRegionsFollowEditsAndStayFoldedWhenNewRangesArrive()
    {
        var buffer = new TextBuffer(Code);
        var model = new FoldingModel();
        model.SetRanges([new FoldingRange(1, 6), new FoldingRange(3, 5)], buffer.Current.LineCount);
        model.SetCollapsed(model.RegionAt(3)!, true);

        model.Map(buffer.Insert(0, "using System;\n\n"));
        Assert.Equal([(3, 8, false), (5, 7, true)], model.Regions.Select(r => (r.StartLine, r.EndLine, r.IsCollapsed)));

        model.Map(buffer.Insert(buffer.Current.GetLine(5).End, " // run"));
        Assert.True(model.RegionAt(5)!.IsCollapsed);

        model.SetRanges([new FoldingRange(3, 8), new FoldingRange(5, 7) { Kind = FoldingRangeKind.Region }], buffer.Current.LineCount);
        Assert.True(model.RegionAt(5)!.IsCollapsed);

        model.Map(buffer.Insert(buffer.Current.GetLine(6).Start, "x"));
        Assert.False(model.RegionAt(5)!.IsCollapsed);

        model.Map(buffer.Delete(TextSpan.FromBounds(buffer.Current.GetLine(2).End - 1, buffer.Current.GetLine(3).Start + 1)));
        Assert.Equal([4], model.Regions.Select(r => r.StartLine));
    }

    [Fact]
    public void IndentationFoldsEachLineWithTheDeeperLinesAfterIt()
    {
        var ranges = IndentationFolding.Compute(TextSnapshot.Create("def run():\n    a = 1\n\n    if a:\n        b()\n\nprint(run)\n"), 4, TestContext.Current.CancellationToken);

        Assert.Equal([(0, 4), (3, 4)], ranges.Select(r => (r.StartLine, r.EndLine)));
    }

    [Fact]
    public Task FoldingAProvidersRegionHidesItsLinesInTheEditorAndMovesTheCaretPastThem() => Run(async () =>
    {
        var fakes = new FakeEditorServices();
        fakes.Syntax.Folding = _ => [new FoldingRange(2, 5), new FoldingRange(1, 6)];
        using var editor = new TextEditor(new FileTestDocument(FilePath, Code), fakes.Create());
        var window = Show(editor);
        await WaitAsync(() => editor.Area.Folding.Regions.Count == 2);
        var area = editor.Area;
        var rowsBefore = area.RowCount;

        editor.CaretOffset = area.Snapshot.GetLine(4).Start + 8;
        Assert.True(editor.Fold());
        Assert.False(area.Folding.RegionAt(1)!.IsCollapsed);
        Assert.True(area.Folding.RegionAt(2)!.IsCollapsed);
        Assert.Equal(rowsBefore - 3, area.RowCount);
        Assert.Equal(area.Snapshot.GetLine(2).End, editor.CaretOffset);
        Assert.Equal((2, 3, 6), (area.RowOf(2), area.RowOf(6), area.LineAtRow(3)));

        Press(editor, Key.Down);
        Assert.Equal(6, area.Snapshot.GetLineFromPosition(editor.CaretOffset).LineNumber);
        Press(editor, Key.Up);
        Assert.Equal(2, area.Snapshot.GetLineFromPosition(editor.CaretOffset).LineNumber);

        editor.Area.Select(EditorSelection.At(area.Snapshot.GetLine(4).Start));
        Assert.False(area.Folding.RegionAt(2)!.IsCollapsed);
        Assert.Equal(rowsBefore, area.RowCount);

        editor.FoldAll();
        Assert.Equal(5, area.Folding.HiddenLineCount);
        editor.UnfoldAll();
        Assert.Equal(0, area.Folding.HiddenLineCount);
        window.Close();
    });

    [Fact]
    public Task AClickOnTheFoldMarkerFoldsAndOnThePlaceholderUnfolds() => Run(async () =>
    {
        var fakes = new FakeEditorServices();
        using var editor = new TextEditor(new FileTestDocument(FilePath, Code), fakes.Create());
        var window = Show(editor);
        await WaitAsync(() => editor.Area.Folding.Regions.Count > 0);
        var area = editor.Area;
        Assert.Equal([(1, 5), (3, 4)], area.Folding.Regions.Select(r => (r.StartLine, r.EndLine)));

        Click(window, area, new Point(area.GutterWidth - 14 - TextArea.FoldMarginWidth / 2, 1.5 * area.LineHeight));
        Assert.True(area.Folding.RegionAt(1)!.IsCollapsed);
        Assert.Equal(6, area.LineAtRow(2));

        window.CaptureRenderedFrame();
        var lineEnd = area.GetCharacterRect(area.Snapshot.GetLine(1).End);
        Click(window, area, new Point(lineEnd.X + 2 * area.Formatting.CharacterWidth, lineEnd.Center.Y));
        Assert.False(area.Folding.RegionAt(1)!.IsCollapsed);
        window.Close();
    });

    [Fact]
    public Task EditorsWithoutDecorationsDoNotFold() => Run(async () =>
    {
        var fakes = new FakeEditorServices();
        fakes.Syntax.Folding = _ => [new FoldingRange(1, 5)];
        using var editor = new TextEditor(new FileTestDocument(FilePath, Code), fakes.Create()) { IsDecorated = false };
        var window = Show(editor);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Empty(editor.Area.Folding.Regions);
        Assert.False(editor.Area.ShowsFolding);
        Assert.False(editor.Fold());
        window.Close();
    });

    private static void Click(Window window, TextArea area, Point point)
    {
        var at = area.TranslatePoint(point, window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
    }

    private static void Press(TextEditor editor, Key key) =>
        editor.Area.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = editor.Area });

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
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private static Task<bool> Run(Func<Task> test) =>
        HeadlessSession.Value.Dispatch(async () =>
        {
            await test();
            return true;
        }, TestContext.Current.CancellationToken);
}
