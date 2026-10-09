using System.Diagnostics;

namespace Runesmith.Text.Tests;

public sealed class TextDiffTests
{
    [Fact]
    public void FindsNothingInEqualTexts()
    {
        Assert.Empty(Lines("a\nb\nc", "a\nb\nc"));
        Assert.Empty(Lines("", ""));
    }

    [Fact]
    public void FindsAddedLines() =>
        Assert.Equal([new DiffHunk(1, 0, 1, 2)], Lines("a\nd", "a\nb\nc\nd"));

    [Fact]
    public void FindsDeletedLinesBeforeTheLineAfterThem() =>
        Assert.Equal([new DiffHunk(1, 2, 1, 0)], Lines("a\nb\nc\nd", "a\nd"));

    [Fact]
    public void FindsChangedLines() =>
        Assert.Equal([new DiffHunk(1, 1, 1, 2)], Lines("a\nb\nc", "a\nB\nB2\nc"));

    [Fact]
    public void FindsChangesAtTheEnds()
    {
        Assert.Equal([new DiffHunk(0, 1, 0, 1), new DiffHunk(2, 1, 2, 1)], Lines("x\nb\ny", "X\nb\nY"));
        Assert.Equal([new DiffHunk(1, 0, 1, 1)], Lines("a", "a\nb"));
        Assert.Equal([new DiffHunk(1, 1, 1, 0)], Lines("a\nb", "a"));
    }

    [Fact]
    public void CountsTheEmptyLineAfterAFinalLineBreak() =>
        Assert.Equal([new DiffHunk(2, 0, 2, 1)], Lines("a\nb", "a\nb\n"));

    [Fact]
    public void IgnoresCarriageReturns() => Assert.Empty(Lines("a\r\nb\r\n", "a\nb\n"));

    [Fact]
    public void ComparesAgainstAnEmptyText()
    {
        Assert.Equal([new DiffHunk(0, 1, 0, 3)], Lines("", "a\nb\nc"));
        Assert.Equal([new DiffHunk(0, 3, 0, 1)], Lines("a\nb\nc", ""));
    }

    [Fact]
    public void PlacesAnInsertedBlockSoItEndsAtABlankLine()
    {
        var before = "void A() {\n}\n\nvoid C() {\n}";
        var after = "void A() {\n}\n\nvoid B() {\n}\n\nvoid C() {\n}";

        Assert.Equal([new DiffHunk(3, 0, 3, 3)], Lines(before, after));
    }

    [Fact]
    public void FindsTheShortestScriptForSmallRandomTexts()
    {
        var random = new Random(7);
        for (var run = 0; run < 500; run++)
        {
            var a = RandomLines(random, random.Next(0, 30), 4);
            var b = RandomLines(random, random.Next(0, 30), 4);
            var hunks = Lines(Join(a), Join(b));

            Assert.Equal(Split(Join(b)), Apply(Split(Join(a)), Split(Join(b)), hunks));
            var edits = hunks.Sum(h => h.OldLength + h.NewLength);
            Assert.Equal(EditDistance(Split(Join(a)), Split(Join(b))), edits);
        }
    }

    [Fact]
    public void DiffsLargeTextsWithFewChangesCorrectlyAndFast()
    {
        var random = new Random(11);
        var a = Enumerable.Range(0, 20_000).Select(i => $"    line {i} {random.Next()};").ToList();
        var b = new List<string>(a);
        for (var change = 0; change < 200; change++)
        {
            var at = random.Next(b.Count);
            switch (change % 3)
            {
                case 0:
                    b[at] = "changed " + b[at];
                    break;
                case 1:
                    b.Insert(at, "inserted " + change);
                    break;
                default:
                    b.RemoveAt(at);
                    break;
            }
        }

        var oldText = Join(a);
        var newText = Join(b);
        Lines(oldText, newText);

        var stopwatch = Stopwatch.StartNew();
        var hunks = Lines(oldText, newText);
        stopwatch.Stop();

        Assert.Equal(Split(newText), Apply(Split(oldText), Split(newText), hunks));
        Assert.True(hunks.Sum(h => h.OldLength + h.NewLength) <= 400, "The script is close to the shortest one.");
#if DEBUG
        Assert.True(stopwatch.ElapsedMilliseconds < 250, $"Took {stopwatch.ElapsedMilliseconds} ms.");
#else
        Assert.True(stopwatch.ElapsedMilliseconds < 50, $"Took {stopwatch.ElapsedMilliseconds} ms.");
#endif
    }

    [Fact]
    public void DiffsLargeUnrelatedTextsWithinTheCostLimit()
    {
        var random = new Random(3);
        var oldText = Join(RandomLines(random, 20_000, 20));
        var newText = Join(RandomLines(random, 20_000, 20));

        var stopwatch = Stopwatch.StartNew();
        var hunks = Lines(oldText, newText);
        stopwatch.Stop();

        Assert.Equal(Split(newText), Apply(Split(oldText), Split(newText), hunks));
        Assert.True(stopwatch.ElapsedMilliseconds < 1000, $"Took {stopwatch.ElapsedMilliseconds} ms.");
    }

    [Fact]
    public void StopsWhenCanceled()
    {
        var random = new Random(5);
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            TextDiff.Lines(Join(RandomLines(random, 5000, 100)), Join(RandomLines(random, 5000, 100)), source.Token));
    }

    [Fact]
    public void FindsChangedWords()
    {
        const string before = "var count = items.Count;";
        const string after = "var total = items.Count();";

        var hunks = TextDiff.Words(before, after);

        Assert.Equal(2, hunks.Count);
        Assert.Equal("count", before.Substring(hunks[0].OldStart, hunks[0].OldLength));
        Assert.Equal("total", after.Substring(hunks[0].NewStart, hunks[0].NewLength));
        Assert.Equal("", before.Substring(hunks[1].OldStart, hunks[1].OldLength));
        Assert.Equal("()", after.Substring(hunks[1].NewStart, hunks[1].NewLength));
    }

    [Fact]
    public void TreatsLongTextsAsOneWordChange()
    {
        var before = new string('a', 15_000);
        var after = new string('b', 15_000);

        Assert.Equal([new DiffHunk(0, 15_000, 0, 15_000)], TextDiff.Words(before, after));
        Assert.Empty(TextDiff.Words(before, before));
    }

    private static IReadOnlyList<DiffHunk> Lines(string oldText, string newText) =>
        TextDiff.Lines(oldText, newText, TestContext.Current.CancellationToken);

    private static List<string> RandomLines(Random random, int count, int distinct) =>
        [.. Enumerable.Range(0, count).Select(_ => "l" + random.Next(distinct))];

    private static string Join(IEnumerable<string> lines) => string.Join('\n', lines);

    private static string[] Split(string text) => text.Split('\n');

    private static List<string> Apply(string[] a, string[] b, IReadOnlyList<DiffHunk> hunks)
    {
        var result = new List<string>();
        var position = 0;
        foreach (var hunk in hunks)
        {
            Assert.True(hunk.OldStart >= position, "Hunks are in order and do not overlap.");
            result.AddRange(a[position..hunk.OldStart]);
            result.AddRange(b[hunk.NewStart..hunk.NewEnd]);
            position = hunk.OldEnd;
        }

        result.AddRange(a[position..]);
        return result;
    }

    private static int EditDistance(string[] a, string[] b)
    {
        var lcs = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
        {
            for (var j = b.Length - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
        }

        return a.Length + b.Length - 2 * lcs[0, 0];
    }
}
