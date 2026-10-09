namespace Runesmith.Editor.Tests;

public sealed class LineGapTests
{
    [Fact]
    public Task MapsLinesToRowsAroundBlankRows() => HeadlessSession.Value.Dispatch(() =>
    {
        var area = new TextArea(new TestDocument("a\nb\nc\nd"));
        Assert.Equal(2, area.RowOf(2));

        area.Diff = new DiffDecorations([], [], [new LineGap(0, 1), new LineGap(2, 2), new LineGap(4, 3)]);

        Assert.Equal([1, 2, 5, 6], Enumerable.Range(0, 4).Select(area.RowOf));
        Assert.Equal(10, area.RowCount);
        Assert.Equal([0, 0, 1, 2, 2, 2, 3, 3, 3, 3], Enumerable.Range(0, 10).Select(area.LineAtRow));
    }, TestContext.Current.CancellationToken);
}
