using Runesmith.Editor;
using Runesmith.Shell.Diffs;
using Runesmith.Text;

namespace Runesmith.Shell.Tests.Diffs;

public sealed class DiffLayoutTests
{
    [Fact]
    public void AddedLinesAreGreenOnTheRightWithBlankRowsOnTheLeft()
    {
        var (left, right) = Build("a\nd", "a\nb\nc\nd");

        Assert.Equal([new DiffLineRange(1, 2, DiffKind.Added)], right.Lines);
        Assert.Equal([new LineGap(1, 2)], left.Gaps);
        Assert.Empty(left.Lines);
        Assert.Empty(right.Gaps);
    }

    [Fact]
    public void DeletedLinesAreRedOnTheLeftWithBlankRowsOnTheRight()
    {
        var (left, right) = Build("a\nb\nc", "a\nc");

        Assert.Equal([new DiffLineRange(1, 1, DiffKind.Removed)], left.Lines);
        Assert.Equal([new LineGap(1, 1)], right.Gaps);
    }

    [Fact]
    public void ChangedRunsAreBlueOnBothSidesAndTheShorterOneGetsBlankRowsBelowIt()
    {
        var (left, right) = Build("a\nold\nz", "a\nnew one\nnew two\nz");

        Assert.Equal([new DiffLineRange(1, 1, DiffKind.Changed)], left.Lines);
        Assert.Equal([new DiffLineRange(1, 2, DiffKind.Changed)], right.Lines);
        Assert.Equal([new LineGap(2, 1)], left.Gaps);
        Assert.Empty(right.Gaps);
    }

    [Fact]
    public void BothSidesTakeAsManyRows()
    {
        const string before = "a\nb\nc\nd\ne\nf";
        const string after = "a\nB\nB2\nB3\nd\nnew\ne";
        var (left, right) = Build(before, after);

        Assert.Equal(6 + left.Gaps.Sum(g => g.Rows), 7 + right.Gaps.Sum(g => g.Rows));
    }

    [Fact]
    public void HighlightsTheChangedWordsWithoutTheWhitespaceAroundThem()
    {
        const string before = "    var count = items.Count;";
        const string after = "    var total = items.Count;";
        var (left, right) = Build(before, after);

        Assert.Equal("count", before.Substring(left.Spans[0].Span.Start, left.Spans[0].Span.Length));
        Assert.Equal("total", after.Substring(right.Spans[0].Span.Start, right.Spans[0].Span.Length));
    }

    [Fact]
    public void LeavesRewrittenLinesWithoutWordHighlights()
    {
        var (left, right) = Build("first version of a line", "something else entirely");

        Assert.Empty(left.Spans);
        Assert.Empty(right.Spans);
    }

    private static (DiffDecorations Left, DiffDecorations Right) Build(string before, string after) =>
        DiffLayout.Build(TextSnapshot.Create(before), TextSnapshot.Create(after), TextDiff.Lines(before, after, TestContext.Current.CancellationToken));
}
