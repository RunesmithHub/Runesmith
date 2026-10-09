using Runesmith.Editor;
using Runesmith.Text;

namespace Runesmith.Shell.Diffs;

/// <summary>Turns the changed lines of two texts into what each side of a diff draws: tinted lines, changed words, and blank rows where the
/// other side has more lines, so the sides stay aligned row for row.</summary>
internal static class DiffLayout
{
    // Past this share of changed characters, a run reads as rewritten, and highlighting its words would only add noise.
    private const double MostlyRewritten = 0.6;

    public static (DiffDecorations Left, DiffDecorations Right) Build(TextSnapshot left, TextSnapshot right, IReadOnlyList<DiffHunk> hunks)
    {
        List<DiffLineRange> leftLines = [], rightLines = [];
        List<DiffSpan> leftSpans = [], rightSpans = [];
        List<LineGap> leftGaps = [], rightGaps = [];
        foreach (var hunk in hunks)
        {
            if (hunk.IsAddition)
            {
                rightLines.Add(new DiffLineRange(hunk.NewStart, hunk.NewLength, DiffKind.Added));
                leftGaps.Add(new LineGap(hunk.OldStart, hunk.NewLength));
                continue;
            }

            if (hunk.IsDeletion)
            {
                leftLines.Add(new DiffLineRange(hunk.OldStart, hunk.OldLength, DiffKind.Removed));
                rightGaps.Add(new LineGap(hunk.NewStart, hunk.OldLength));
                continue;
            }

            leftLines.Add(new DiffLineRange(hunk.OldStart, hunk.OldLength, DiffKind.Changed));
            rightLines.Add(new DiffLineRange(hunk.NewStart, hunk.NewLength, DiffKind.Changed));
            if (hunk.OldLength < hunk.NewLength)
                leftGaps.Add(new LineGap(hunk.OldEnd, hunk.NewLength - hunk.OldLength));
            else if (hunk.NewLength < hunk.OldLength)
                rightGaps.Add(new LineGap(hunk.NewEnd, hunk.OldLength - hunk.NewLength));

            AddWords(left, right, hunk, leftSpans, rightSpans);
        }

        return (new DiffDecorations(leftLines, leftSpans, leftGaps), new DiffDecorations(rightLines, rightSpans, rightGaps));
    }

    private static void AddWords(TextSnapshot left, TextSnapshot right, DiffHunk hunk, List<DiffSpan> leftSpans, List<DiffSpan> rightSpans)
    {
        var leftRun = Lines(left, hunk.OldStart, hunk.OldEnd);
        var rightRun = Lines(right, hunk.NewStart, hunk.NewEnd);
        var oldText = left.GetText(leftRun);
        var newText = right.GetText(rightRun);
        var words = TextDiff.Words(oldText, newText);
        var changed = words.Sum(w => w.OldLength + w.NewLength);
        if (changed > MostlyRewritten * (oldText.Length + newText.Length))
            return;

        foreach (var word in words)
        {
            AddTrimmed(leftSpans, oldText, leftRun.Start, word.OldStart, word.OldEnd);
            AddTrimmed(rightSpans, newText, rightRun.Start, word.NewStart, word.NewEnd);
        }
    }

    // A changed run is highlighted line by line without the whitespace around it, so indentation and line breaks stay plain.
    private static void AddTrimmed(List<DiffSpan> spans, string text, int offset, int start, int end)
    {
        while (start < end)
        {
            var lineEnd = text.IndexOf('\n', start, end - start) is var found and >= 0 ? found : end;
            int from = start, to = lineEnd;
            while (from < to && char.IsWhiteSpace(text[from]))
                from++;
            while (to > from && char.IsWhiteSpace(text[to - 1]))
                to--;
            if (to > from)
                spans.Add(new DiffSpan(new TextSpan(offset + from, to - from), DiffKind.Changed));
            start = lineEnd + 1;
        }
    }

    private static TextSpan Lines(TextSnapshot snapshot, int start, int end) =>
        TextSpan.FromBounds(snapshot.GetLine(start).Start, snapshot.GetLine(end - 1).End);
}
