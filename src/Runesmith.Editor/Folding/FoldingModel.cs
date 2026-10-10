using Runesmith.Sdk.Languages;
using Runesmith.Text;

namespace Runesmith.Editor.Folding;

/// <summary>Lines that can be folded, and whether they are.</summary>
public sealed class FoldRegion
{
    internal FoldRegion(int startLine, int endLine, FoldingRangeKind kind, string? collapsedText)
    {
        StartLine = startLine;
        EndLine = endLine;
        Kind = kind;
        CollapsedText = collapsedText;
    }

    /// <summary>Gets the line that stays visible while the region is folded.</summary>
    public int StartLine { get; internal set; }

    /// <summary>Gets the last line the region hides.</summary>
    public int EndLine { get; internal set; }

    public FoldingRangeKind Kind { get; }

    /// <summary>Gets the text shown in place of the hidden lines, or null for an ellipsis.</summary>
    public string? CollapsedText { get; }

    public bool IsCollapsed { get; internal set; }

    public override string ToString() => $"{StartLine}-{EndLine}{(IsCollapsed ? " folded" : "")}";
}

/// <summary>The folding regions of one editor and the lines their folding hides. Regions follow edits; folded ones stay folded unless an edit
/// changes their hidden lines.</summary>
public sealed class FoldingModel
{
    /// <summary>The most regions kept; a document with more keeps the first ones.</summary>
    public const int MaxRegions = 10_000;

    private List<FoldRegion> regions = [];

    // The runs of hidden lines, sorted and apart, and the number of hidden lines through each run.
    private int[] hiddenFirst = [];
    private int[] hiddenLast = [];
    private int[] hiddenThrough = [];

    /// <summary>Gets the regions, by start line.</summary>
    public IReadOnlyList<FoldRegion> Regions => regions;

    /// <summary>Gets whether any line is hidden.</summary>
    public bool HasHiddenLines => hiddenFirst.Length > 0;

    /// <summary>Gets the number of hidden lines.</summary>
    public int HiddenLineCount => hiddenThrough.Length == 0 ? 0 : hiddenThrough[^1];

    /// <summary>Raised when the regions or the hidden lines change.</summary>
    public event EventHandler? Changed;

    /// <summary>Replaces the regions with new ranges for a text of <paramref name="lineCount"/> lines; regions folded before that start on
    /// the same line stay folded.</summary>
    public void SetRanges(IReadOnlyList<FoldingRange> ranges, int lineCount)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        var folded = regions.Where(r => r.IsCollapsed).Select(r => r.StartLine).ToHashSet();
        var updated = new List<FoldRegion>(Math.Min(ranges.Count, MaxRegions));
        int? previousStart = null;
        foreach (var range in ranges.Where(r => r.StartLine >= 0 && r.EndLine > r.StartLine && r.StartLine < lineCount)
                     .OrderBy(r => r.StartLine).ThenByDescending(r => r.EndLine))
        {
            if (range.StartLine == previousStart)
                continue;

            previousStart = range.StartLine;
            var region = new FoldRegion(range.StartLine, Math.Min(range.EndLine, lineCount - 1), range.Kind, range.CollapsedText)
            {
                IsCollapsed = folded.Contains(range.StartLine),
            };
            if (region.EndLine > region.StartLine)
                updated.Add(region);
            if (updated.Count == MaxRegions)
                break;
        }

        if (SameRegions(regions, updated))
            return;

        regions = updated;
        Rebuild();
    }

    /// <summary>Gets the region that starts on a line, or null.</summary>
    public FoldRegion? RegionAt(int line)
    {
        var index = IndexOfStart(line);
        return index < regions.Count && regions[index].StartLine == line ? regions[index] : null;
    }

    /// <summary>Gets the innermost region whose lines include a line, folded or not as asked.</summary>
    public FoldRegion? Innermost(int line, bool collapsed)
    {
        FoldRegion? found = null;
        foreach (var region in regions)
        {
            if (region.StartLine > line)
                break;
            if (region.EndLine >= line && region.IsCollapsed == collapsed)
                found = found is null || region.StartLine >= found.StartLine ? region : found;
        }

        return found;
    }

    /// <summary>Folds or unfolds a region.</summary>
    public void SetCollapsed(FoldRegion region, bool collapsed)
    {
        ArgumentNullException.ThrowIfNull(region);
        if (region.IsCollapsed == collapsed)
            return;

        region.IsCollapsed = collapsed;
        Rebuild();
    }

    /// <summary>Folds every region, or only those of one kind; returns whether any changed.</summary>
    public bool CollapseAll(FoldingRangeKind? kind = null) => SetAll(true, kind);

    /// <summary>Unfolds every region; returns whether any changed.</summary>
    public bool ExpandAll() => SetAll(false, null);

    /// <summary>Unfolds the regions that hide a line; returns whether any did.</summary>
    public bool Reveal(int line)
    {
        if (!IsHidden(line))
            return false;

        foreach (var region in regions)
        {
            if (region.StartLine >= line)
                break;
            if (region.IsCollapsed && region.EndLine >= line)
                region.IsCollapsed = false;
        }

        Rebuild();
        return true;
    }

    /// <summary>Whether folding hides a line.</summary>
    public bool IsHidden(int line) => RunOf(line) >= 0;

    /// <summary>Gets the number of hidden lines before a line.</summary>
    public int HiddenBefore(int line)
    {
        if (hiddenFirst.Length == 0)
            return 0;

        var index = UpperBound(hiddenFirst, line - 1) - 1;
        if (index < 0)
            return 0;

        var through = hiddenThrough[index];
        return line <= hiddenLast[index] ? through - (hiddenLast[index] - line + 1) : through;
    }

    /// <summary>Gets the first line after a line that is not hidden, or <paramref name="lineCount"/> when there is none.</summary>
    public int NextVisible(int line, int lineCount)
    {
        var next = line + 1;
        var run = RunOf(next);
        return run < 0 ? Math.Min(next, lineCount) : Math.Min(hiddenLast[run] + 1, lineCount);
    }

    /// <summary>Gets the last line before a line that is not hidden, or -1 when there is none.</summary>
    public int PreviousVisible(int line)
    {
        var previous = line - 1;
        var run = RunOf(previous);
        return run < 0 ? previous : hiddenFirst[run] - 1;
    }

    /// <summary>Gets the line that shows a hidden line: the start of the folded region that hides it, or the line itself.</summary>
    public int VisibleLineOf(int line)
    {
        var run = RunOf(line);
        return run < 0 ? line : hiddenFirst[run] - 1;
    }

    /// <summary>Moves the regions through an edit. A region whose first line is joined to the line before it is dropped, and a folded region
    /// whose hidden lines change unfolds.</summary>
    public void Map(TextChangeSet changeSet)
    {
        ArgumentNullException.ThrowIfNull(changeSet);
        if (regions.Count == 0 || changeSet.Changes.Count == 0)
            return;

        var before = changeSet.Before;
        var after = changeSet.After;
        var changes = changeSet.Changes;
        var kept = new List<FoldRegion>(regions.Count);
        var changed = false;
        var reordered = false;
        var firstLine = before.GetLineFromPosition(changes[0].Span.Start).LineNumber;
        var lastLine = before.GetLineFromPosition(changes[^1].Span.End).LineNumber;
        var delta = after.LineCount - before.LineCount;
        foreach (var region in regions)
        {
            if (region.EndLine >= before.LineCount)
            {
                changed = true;
                continue;
            }

            // Only the regions around the edited lines need their anchors moved; the rest stay or shift whole.
            if (region.EndLine < firstLine || region.StartLine > lastLine)
            {
                if (region.StartLine > lastLine && delta != 0)
                {
                    region.StartLine += delta;
                    region.EndLine += delta;
                    changed = true;
                }

                kept.Add(region);
                continue;
            }

            reordered = true;

            var start = before.GetLine(region.StartLine).Start;
            var end = before.GetLine(region.EndLine).Start;
            var hidden = TextSpan.FromBounds(before.GetLine(region.StartLine + 1).Start, before.GetLine(region.EndLine).EndIncludingBreak);
            if (changes.Any(c => c.Span.Start < start && c.Span.End > start))
            {
                changed = true;
                continue;
            }

            if (region.IsCollapsed && changes.Any(c => c.Span.Start < hidden.End && c.Span.End > hidden.Start || c.Span.IsEmpty && c.Span.Start >= hidden.Start && c.Span.Start < hidden.End))
            {
                region.IsCollapsed = false;
                changed = true;
            }

            var startLine = after.GetLineFromPosition(Map(changes, start)).LineNumber;
            var endLine = after.GetLineFromPosition(Map(changes, end)).LineNumber;
            if (endLine <= startLine)
            {
                changed = true;
                continue;
            }

            changed |= startLine != region.StartLine || endLine != region.EndLine;
            region.StartLine = startLine;
            region.EndLine = endLine;
            kept.Add(region);
        }

        if (!changed)
            return;

        regions = reordered ? [.. kept.OrderBy(r => r.StartLine).ThenByDescending(r => r.EndLine).DistinctBy(r => r.StartLine)] : kept;
        Rebuild();
    }

    private bool SetAll(bool collapsed, FoldingRangeKind? kind)
    {
        var changed = false;
        foreach (var region in regions)
        {
            if (region.IsCollapsed == collapsed || kind is { } only && region.Kind != only)
                continue;

            region.IsCollapsed = collapsed;
            changed = true;
        }

        if (changed)
            Rebuild();
        return changed;
    }

    // A folded region inside lines another folded region hides adds nothing.
    private void Rebuild()
    {
        var first = new List<int>();
        var last = new List<int>();
        foreach (var region in regions)
        {
            if (!region.IsCollapsed || last.Count > 0 && region.StartLine <= last[^1] && region.StartLine >= first[^1])
                continue;

            var from = region.StartLine + 1;
            if (last.Count > 0 && from <= last[^1] + 1)
            {
                last[^1] = Math.Max(last[^1], region.EndLine);
                continue;
            }

            first.Add(from);
            last.Add(region.EndLine);
        }

        hiddenFirst = [.. first];
        hiddenLast = [.. last];
        hiddenThrough = new int[hiddenFirst.Length];
        var total = 0;
        for (var i = 0; i < hiddenFirst.Length; i++)
            hiddenThrough[i] = total += hiddenLast[i] - hiddenFirst[i] + 1;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private int RunOf(int line)
    {
        if (hiddenFirst.Length == 0)
            return -1;

        var index = UpperBound(hiddenFirst, line) - 1;
        return index >= 0 && line <= hiddenLast[index] ? index : -1;
    }

    private int IndexOfStart(int line)
    {
        int low = 0, high = regions.Count;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (regions[middle].StartLine < line)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private static int Map(IReadOnlyList<TextChange> changes, int offset)
    {
        var delta = 0;
        foreach (var change in changes)
        {
            if (offset < change.Span.Start || offset == change.Span.Start && !change.Span.IsEmpty)
                break;

            if (offset < change.Span.End)
                return change.Span.Start + delta + change.NewText.Length;

            delta += change.NewText.Length - change.Span.Length;
        }

        return offset + delta;
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

    private static bool SameRegions(List<FoldRegion> a, List<FoldRegion> b) =>
        a.Count == b.Count && a.Zip(b).All(pair => pair.First.StartLine == pair.Second.StartLine && pair.First.EndLine == pair.Second.EndLine
            && pair.First.Kind == pair.Second.Kind && pair.First.CollapsedText == pair.Second.CollapsedText && pair.First.IsCollapsed == pair.Second.IsCollapsed);
}
