using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using Runesmith.Editor.Rendering;
using Runesmith.Text;

namespace Runesmith.Editor;

public sealed partial class TextArea
{
    private const double MarkerWidth = 3;
    private const double MarkerRight = 9;
    private static readonly TimeSpan ChangeDelay = TimeSpan.FromMilliseconds(100);

    private string? baseText;
    private string[] baseLines = [];
    private IReadOnlyList<DiffHunk> lineChanges = [];
    private DispatcherTimer? changeTimer;
    private CancellationTokenSource? changeRequest;

    // The base text whose lines are split already, so edits reuse them.
    private string? comparedBase;

    /// <summary>Gets or sets the text the change markers compare the document with, such as the file in the last commit, or null for no
    /// markers. The comparison runs in the background shortly after each edit.</summary>
    public string? BaseText
    {
        get => baseText;
        set
        {
            if (baseText == value)
                return;

            baseText = value;
            if (value is null)
            {
                Cancellation.Cancel(ref changeRequest);
                changeTimer?.Stop();
                SetLineChanges([], []);
                return;
            }

            _ = CompareWithBaseAsync();
        }
    }

    /// <summary>Gets the runs of lines that differ from <see cref="BaseText"/>, in order: old lines in the base, new lines in the document.</summary>
    public IReadOnlyList<DiffHunk> LineChanges => lineChanges;

    /// <summary>Raised when <see cref="LineChanges"/> changes.</summary>
    public event EventHandler? LineChangesChanged;

    /// <summary>Raised when the user clicks the change marker of a run of lines.</summary>
    public event EventHandler<DiffHunk>? LineChangeClicked;

    /// <summary>Gets the lines of the base that a run of changed lines replaced.</summary>
    public IReadOnlyList<string> GetBaseLines(DiffHunk change) =>
        change.OldEnd <= baseLines.Length ? baseLines[change.OldStart..change.OldEnd] : [];

    /// <summary>Gets the change at or after the caret's line, or before it, going round at the ends; null when nothing changed.</summary>
    public DiffHunk? FindChange(bool next)
    {
        if (lineChanges.Count == 0)
            return null;

        var line = Snapshot.GetLineFromPosition(selection.Caret).LineNumber;
        return next
            ? lineChanges.FirstOrDefault(c => c.NewStart > line, lineChanges[0])
            : lineChanges.LastOrDefault(c => c.NewStart < line, lineChanges[^1]);
    }

    /// <summary>Moves the caret to the start of a change and scrolls it into view.</summary>
    public void GoToChange(DiffHunk change)
    {
        var line = Math.Min(change.NewStart, Snapshot.LineCount - 1);
        Select(EditorSelection.At(Snapshot.GetLine(line).Start), scrollIntoView: false);
        ScrollIntoView(selection.Caret, center: true);
    }

    /// <summary>Gets the changes that touch the selected lines, or the caret's line.</summary>
    public IReadOnlyList<DiffHunk> ChangesInSelection()
    {
        var snapshot = Snapshot;
        var first = snapshot.GetLineFromPosition(selection.Start).LineNumber;
        var last = snapshot.GetLineFromPosition(selection.End).LineNumber;
        return [.. lineChanges.Where(c => c.IsDeletion ? c.NewStart >= first && c.NewStart <= last + 1 : c.NewStart <= last && c.NewEnd > first)];
    }

    /// <summary>Puts the base's lines back in place of changes, as one undo step.</summary>
    public void Rollback(IReadOnlyList<DiffHunk> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Count == 0 || baseText is null)
            return;

        var snapshot = Snapshot;
        var edits = changes.OrderBy(c => c.NewStart).Select(c => RollbackEdit(snapshot, c)).ToList();
        ApplyChanges(edits, EditorSelection.At(edits[0].Span.Start));
    }

    // Replaces the change's lines with the base's, keeping the line breaks around them right when the change reaches the end of the text.
    private TextChange RollbackEdit(TextSnapshot snapshot, DiffHunk change)
    {
        var old = GetBaseLines(change);
        if (change.NewEnd < snapshot.LineCount)
            return new TextChange(TextSpan.FromBounds(snapshot.GetLine(change.NewStart).Start, snapshot.GetLine(change.NewEnd).Start), string.Concat(old.Select(l => l + "\n")));

        if (change.NewStart == 0)
            return new TextChange(new TextSpan(0, snapshot.Length), string.Join('\n', old));

        var start = snapshot.GetLine(change.NewStart - 1).End;
        return new TextChange(TextSpan.FromBounds(start, snapshot.Length), string.Concat(old.Select(l => "\n" + l)));
    }

    /// <summary>Gets the rectangle of a change's marker in the gutter, relative to this control.</summary>
    public Rect GetChangeMarkerRect(DiffHunk change)
    {
        var x = GutterWidth - MarkerRight;
        if (change.IsDeletion)
            return new Rect(x, LineTop(change.NewStart) - 5, 7, 10);

        var top = LineTop(change.NewStart);
        return new Rect(x, top, MarkerWidth, LineBottom(change.NewStart + change.NewLength - 1) - top);
    }

    private DiffHunk? ChangeMarkerAt(Point point)
    {
        var gutterWidth = GutterWidth;
        if (lineChanges.Count == 0 || point.X < gutterWidth - MarkerRight - 4 || point.X > gutterWidth - 1)
            return null;

        foreach (var change in VisibleChanges())
        {
            var rect = GetChangeMarkerRect(change);
            if (point.Y >= rect.Top - (change.IsDeletion ? 1 : 0) && point.Y < rect.Bottom + (change.IsDeletion ? 1 : 0))
                return change;
        }

        return null;
    }

    private IEnumerable<DiffHunk> VisibleChanges()
    {
        var (first, last) = VisibleLines();
        int low = 0, high = lineChanges.Count;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (lineChanges[middle].NewEnd < first)
                low = middle + 1;
            else
                high = middle;
        }

        for (var i = low; i < lineChanges.Count && lineChanges[i].NewStart <= last + 1; i++)
            yield return lineChanges[i];
    }

    private void DrawChangeMarkers(DrawingContext context, EditorBrushes brushes)
    {
        if (lineChanges.Count == 0)
            return;

        foreach (var change in VisibleChanges())
        {
            var rect = GetChangeMarkerRect(change);
            if (change.IsDeletion)
            {
                var geometry = new StreamGeometry();
                using (var stream = geometry.Open())
                {
                    stream.BeginFigure(new Point(rect.Left, rect.Top), true);
                    stream.LineTo(new Point(rect.Right, rect.Center.Y));
                    stream.LineTo(new Point(rect.Left, rect.Bottom));
                    stream.EndFigure(true);
                }

                context.DrawGeometry(brushes.DeletedMarker, null, geometry);
            }
            else
            {
                context.FillRectangle(change.IsAddition ? brushes.AddedMarker : brushes.ModifiedMarker, rect.Deflate(new Thickness(0, 1)), 1);
            }
        }
    }

    // Edits move the markers below them at once; the comparison that follows a moment later corrects the rest.
    private void FollowEditWithChanges(int firstLine, int lastLineBefore, int lineDelta)
    {
        if (baseText is null)
            return;

        if (lineDelta != 0 && lineChanges.Count > 0)
        {
            var moved = new DiffHunk[lineChanges.Count];
            for (var i = 0; i < moved.Length; i++)
            {
                var change = lineChanges[i];
                moved[i] = change.NewStart > lastLineBefore ? change with { NewStart = Math.Max(firstLine, change.NewStart + lineDelta) } : change;
            }

            lineChanges = moved;
        }

        if (changeTimer is null)
        {
            changeTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = ChangeDelay };
            changeTimer.Tick += (_, _) =>
            {
                changeTimer.Stop();
                _ = CompareWithBaseAsync();
            };
        }

        changeTimer.Stop();
        changeTimer.Start();
    }

    private async Task CompareWithBaseAsync()
    {
        if (baseText is not { } text)
            return;

        var token = Cancellation.Renew(ref changeRequest);
        var snapshot = Snapshot;
        var reusedLines = ReferenceEquals(text, comparedBase) ? baseLines : null;
        try
        {
            var (changes, lines) = await Task.Run(() =>
            {
                var current = snapshot.GetText();
                var lines = reusedLines ?? SplitLines(text);
                return (TextDiff.Lines(text, current, token), lines);
            }, token);
            if (token.IsCancellationRequested || !ReferenceEquals(text, baseText) || !ReferenceEquals(snapshot, Snapshot))
                return;

            comparedBase = text;
            SetLineChanges(changes, lines);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static string[] SplitLines(string text)
    {
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].EndsWith('\r'))
                lines[i] = lines[i][..^1];
        }

        return lines;
    }

    private void SetLineChanges(IReadOnlyList<DiffHunk> changes, string[] lines)
    {
        baseLines = lines;
        if (changes.Count == 0 && lineChanges.Count == 0)
            return;

        lineChanges = changes;
        InvalidateVisual();
        LineChangesChanged?.Invoke(this, EventArgs.Empty);
    }
}
