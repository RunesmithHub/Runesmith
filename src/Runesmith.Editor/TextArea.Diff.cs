using Avalonia;
using Avalonia.Media;
using Runesmith.Editor.Rendering;
using Runesmith.Text;

namespace Runesmith.Editor;

public sealed partial class TextArea
{
    private DiffDecorations diff = DiffDecorations.Empty;

    // The lines with rows above them, blank rows of the diff or a code lens row, and the rows of all gaps up to each one; empty while there
    // are none, so rows are lines.
    private int[] gapLines = [];
    private int[] gapRowsThrough = [];

    /// <summary>Gets or sets what the editor draws as one side of a diff: changed lines and characters, and blank rows that keep it aligned
    /// with the other side.</summary>
    public DiffDecorations Diff
    {
        get => diff;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            diff = value;
            RebuildRows();
        }
    }

    // A line with code lenses gets one row above it, below any blank rows of the diff.
    private void RebuildRows()
    {
        var rows = new SortedDictionary<int, int>();
        foreach (var gap in diff.Gaps.Where(g => g.Rows > 0))
            rows[gap.Line] = rows.GetValueOrDefault(gap.Line) + gap.Rows;
        foreach (var line in lensLines)
            rows[line] = rows.GetValueOrDefault(line) + 1;

        gapLines = [.. rows.Keys];
        gapRowsThrough = new int[gapLines.Length];
        var total = 0;
        var index = 0;
        foreach (var count in rows.Values)
            gapRowsThrough[index++] = total += count;
        InvalidateVisual();
        ScrollChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Gets the number of rows the text takes: its lines and the blank rows of <see cref="Diff"/>, less the lines folding hides.</summary>
    public int RowCount => Snapshot.LineCount + (gapRowsThrough.Length == 0 ? 0 : gapRowsThrough[^1]) - Folding.HiddenLineCount;

    /// <summary>Gets the row a line is drawn in, counting blank rows above it; a hidden line has the row of the next line shown.</summary>
    public int RowOf(int lineNumber)
    {
        var hidden = Folding.HasHiddenLines ? Folding.HiddenBefore(lineNumber) : 0;
        if (gapLines.Length == 0)
            return lineNumber - hidden;

        var index = UpperBound(gapLines, lineNumber);
        return lineNumber + (index == 0 ? 0 : gapRowsThrough[index - 1]) - hidden;
    }

    /// <summary>Gets the line drawn in a row, or the line after the blank rows the row is in; never a line folding hides.</summary>
    public int LineAtRow(int row)
    {
        var count = Snapshot.LineCount;
        if (gapLines.Length == 0 && !Folding.HasHiddenLines)
            return Math.Clamp(row, 0, count - 1);

        int low = 0, high = count - 1;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (RowOf(middle) <= row)
                low = middle;
            else
                high = middle - 1;
        }

        var line = RowOf(low) < row && low < count - 1 ? low + 1 : low;
        if (!IsHiddenLine(line))
            return line;

        var next = Folding.NextVisible(line, count);
        return next < count ? next : Folding.VisibleLineOf(line);
    }

    private static int UpperBound(int[] values, int value)
    {
        int low = 0, high = values.Length;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (values[middle] <= value)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private void DrawDiffLines(DrawingContext context, EditorBrushes brushes, int first, int last, double left, double right)
    {
        if (diff.Lines.Count == 0)
            return;

        foreach (var range in diff.Lines)
        {
            var end = range.Start + range.Count - 1;
            if (end < first || range.Start > last)
                continue;

            var from = Math.Max(range.Start, first);
            var to = Math.Min(end, last);
            var top = LineTop(from);
            context.FillRectangle(brushes.DiffLine(range.Kind), new Rect(left, top, right - left, LineBottom(to) - top));
        }
    }

    private void DrawDiffSpans(DrawingContext context, EditorBrushes brushes, TextSpan visible, double left)
    {
        foreach (var span in diff.Spans)
        {
            if (span.Span.End < visible.Start || span.Span.Start > visible.End)
                continue;

            foreach (var rect in SpanRects(span.Span, left))
                context.FillRectangle(brushes.DiffSpan(span.Kind), rect, 2);
        }
    }

    private void DrawGaps(DrawingContext context, EditorBrushes brushes, double width)
    {
        if (gapLines.Length == 0 || diff.Gaps.Count == 0)
            return;

        var lineHeight = LineHeight;
        var height = Bounds.Height;
        for (var i = 0; i < gapLines.Length; i++)
        {
            var lens = Array.BinarySearch(lensLines, gapLines[i]) >= 0 ? 1 : 0;
            var rows = gapRowsThrough[i] - (i == 0 ? 0 : gapRowsThrough[i - 1]) - lens;
            if (rows <= 0)
                continue;

            var bottom = (RowOf(gapLines[i]) - lens) * lineHeight - scrollOffset.Y;
            var top = bottom - rows * lineHeight;
            if (bottom < 0 || top > height)
                continue;

            var rect = new Rect(0, top, width, bottom - top);
            context.FillRectangle(brushes.Background, rect);
            using (context.PushClip(rect))
            {
                const double step = 8;
                var rise = rect.Height;
                for (var x = Math.Floor((-rise - scrollOffset.X) / step) * step + scrollOffset.X; x < width; x += step)
                    context.DrawLine(brushes.Filler, new Point(x, bottom), new Point(x + rise, top));
            }
        }
    }
}
