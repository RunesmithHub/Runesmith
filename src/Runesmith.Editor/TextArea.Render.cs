using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Runesmith.Editor.Editing;
using Runesmith.Editor.Rendering;
using Runesmith.Sdk.Build;
using Runesmith.Text;

namespace Runesmith.Editor;

public sealed partial class TextArea
{
    private readonly Dictionary<(int Number, bool Current), Avalonia.Media.TextFormatting.TextLine> lineNumbers = [];
    private EditorBrushes? lineNumberBrushes;

    public override void Render(DrawingContext context)
    {
        var renderStart = System.Diagnostics.Stopwatch.GetTimestamp();
        var brushes = Brushes;
        var snapshot = Snapshot;
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(brushes.Background, bounds);

        var (first, last) = VisibleLines();
        highlighter?.Prioritize(first, last);
        var lineHeight = LineHeight;
        var caret = snapshot.GetPosition(selection.Caret);
        var gutterWidth = GutterWidth;

        if (options.HighlightCurrentLine && selection.IsEmpty && caret.Line >= first && caret.Line <= last)
            context.FillRectangle(brushes.CurrentLine, new Rect(0, LineTop(caret.Line), bounds.Width, lineHeight));

        DrawDiffLines(context, brushes, first, last, gutterWidth, bounds.Width);

        using (context.PushClip(new Rect(gutterWidth, 0, Math.Max(0, bounds.Width - gutterWidth), bounds.Height)))
        {
            var left = TextLeft;
            var visibleSpan = TextSpan.FromBounds(snapshot.GetLine(first).Start, snapshot.GetLine(last).EndIncludingBreak);
            DrawDiffSpans(context, brushes, visibleSpan, left);
            DrawSearchMatches(context, brushes, visibleSpan, left);
            DrawHighlightBackgrounds(context, visibleSpan, left);
            DrawSelection(context, brushes, snapshot, first, last, left);
            DrawBracketMatch(context, brushes, left);
            if (options.ShowIndentGuides)
                DrawIndentGuides(context, brushes, snapshot, first, last, left, caret.Line);

            DrawInlayBackgrounds(context, first, last, left);
            for (var number = first; number <= last; number++)
                GetVisualLine(number).Line.Draw(context, new Point(left, LineTop(number)));

            if (options.ShowWhitespace)
                DrawWhitespace(context, brushes, first, last, left);

            DrawDiagnostics(context, brushes, snapshot, visibleSpan, left);
            DrawHighlightUnderlines(context, visibleSpan, left);
            if (link is { } linkSpan)
                Underline(context, brushes.Link, linkSpan, left);

            if (IsFocused && caretVisible)
                DrawCaret(context, brushes, caret, left);
        }

        DrawGutter(context, brushes, snapshot, first, last, caret.Line, gutterWidth);
        DrawGaps(context, brushes, bounds.Width);
        DrawCodeLenses(context, first, last, bounds.Width);
        EditorMetrics.Render.Record(EditorMetrics.Since(renderStart));
        if (inputTimestamp != 0)
        {
            var input = inputTimestamp;
            inputTimestamp = 0;
            EditorMetrics.InputToRender.Record(EditorMetrics.Since(input));
            TopLevel.GetTopLevel(this)?.RequestAnimationFrame(_ => EditorMetrics.InputToFrame.Record(EditorMetrics.Since(input)));
        }
    }

    private void DrawSearchMatches(DrawingContext context, EditorBrushes brushes, TextSpan visible, double left)
    {
        foreach (var match in searchMatches)
        {
            if (match.End < visible.Start || match.Start > visible.End)
                continue;

            foreach (var rect in SpanRects(match, left))
                context.FillRectangle(brushes.SearchMatch, rect, 2);
        }

        if (currentSearchMatch is { } current && current.IntersectsWith(visible))
        {
            foreach (var rect in SpanRects(current, left))
                context.DrawRectangle(null, brushes.CurrentSearchMatch, rect.Inflate(0.5), 2, 2);
        }
    }

    private void DrawSelection(DrawingContext context, EditorBrushes brushes, TextSnapshot snapshot, int first, int last, double left)
    {
        if (selection.IsEmpty)
            return;

        var brush = IsFocused ? brushes.Selection : brushes.InactiveSelection;
        var newlineWidth = Formatting.CharacterWidth * 0.6;
        for (var number = first; number <= last; number++)
        {
            var line = snapshot.GetLine(number);
            var start = Math.Max(selection.Start, line.Start);
            var end = Math.Min(selection.End, line.End);
            var includesBreak = selection.End > line.End && selection.Start <= line.End && number < snapshot.LineCount - 1;
            if (start > end || start == end && !includesBreak)
                continue;

            var visual = GetVisualLine(number);
            var x1 = visual.GetX(start - line.Start);
            var x2 = visual.GetX(end - line.Start) + (includesBreak ? newlineWidth : 0);
            context.FillRectangle(brush, new Rect(left + x1, LineTop(number), Math.Max(1, x2 - x1), LineHeight), 2);
        }
    }

    private void DrawBracketMatch(DrawingContext context, EditorBrushes brushes, double left)
    {
        if (bracketMatch is not { } match)
            return;

        foreach (var offset in (ReadOnlySpan<int>)[match.Open, match.Close])
        {
            foreach (var rect in SpanRects(new TextSpan(offset, 1), left))
                context.DrawRectangle(brushes.BracketMatch, brushes.BracketMatchBorder, rect.Deflate(new Thickness(0, 1)), 2, 2);
        }
    }

    private void DrawIndentGuides(DrawingContext context, EditorBrushes brushes, TextSnapshot snapshot, int first, int last, double left, int caretLine)
    {
        var step = options.TabSize * Formatting.CharacterWidth;
        var depths = new int[last - first + 1];
        for (var number = first; number <= last; number++)
            depths[number - first] = IndentDepth(snapshot, number);

        // The active guide belongs to the block around the caret: the lines next to it that are indented at least as deeply.
        var active = (Index: -1, From: 0, To: -1);
        if (caretLine >= first && caretLine <= last && depths[caretLine - first] > 0)
        {
            var depth = depths[caretLine - first];
            var from = caretLine;
            var to = caretLine;
            while (from > first && depths[from - 1 - first] >= depth)
                from--;
            while (to < last && depths[to + 1 - first] >= depth)
                to++;
            active = (depth - 1, from, to);
        }

        for (var number = first; number <= last; number++)
        {
            var top = LineTop(number);
            for (var index = 0; index < depths[number - first]; index++)
            {
                var x = Math.Round(left + index * step) + 0.5;
                var isActive = index == active.Index && number >= active.From && number <= active.To;
                context.DrawLine(isActive ? brushes.ActiveIndentGuide : brushes.IndentGuide, new Point(x, top), new Point(x, top + LineHeight));
            }
        }
    }

    // Blank lines take the smaller indentation of the lines around them, so guides run through gaps in a block.
    private int IndentDepth(TextSnapshot snapshot, int number)
    {
        int Depth(string text) => EditOperations.VisualColumn(EditOperations.LeadingWhitespace(text), options.TabSize) / options.TabSize;

        var text = GetVisualLine(number).Text;
        if (!string.IsNullOrWhiteSpace(text))
            return Depth(text);

        int Neighbor(int direction)
        {
            for (var i = number + direction; i >= 0 && i < snapshot.LineCount && Math.Abs(i - number) < 100; i += direction)
            {
                var neighbor = snapshot.GetLineText(i);
                if (!string.IsNullOrWhiteSpace(neighbor))
                    return Depth(neighbor);
            }

            return 0;
        }

        return Math.Min(Neighbor(-1), Neighbor(1));
    }

    private void DrawWhitespace(DrawingContext context, EditorBrushes brushes, int first, int last, double left)
    {
        var size = Math.Max(1.5, Formatting.FontSize / 9);
        for (var number = first; number <= last; number++)
        {
            var visual = GetVisualLine(number);
            var middle = LineTop(number) + LineHeight / 2;
            for (var column = 0; column < visual.Text.Length; column++)
            {
                var character = visual.Text[column];
                if (character is not (' ' or '\t'))
                    continue;

                var x1 = left + visual.GetX(column);
                var x2 = left + visual.GetX(column + 1);
                if (character == ' ')
                {
                    context.FillRectangle(brushes.Whitespace, new Rect((x1 + x2 - size) / 2, middle - size / 2, size, size), (float)(size / 2));
                }
                else
                {
                    var pen = new Pen(brushes.Whitespace, 1);
                    context.DrawLine(pen, new Point(x1 + 2, middle), new Point(x2 - 2, middle));
                    context.DrawLine(pen, new Point(x2 - 5, middle - 3), new Point(x2 - 2, middle));
                    context.DrawLine(pen, new Point(x2 - 5, middle + 3), new Point(x2 - 2, middle));
                }
            }
        }
    }

    private void DrawDiagnostics(DrawingContext context, EditorBrushes brushes, TextSnapshot snapshot, TextSpan visible, double left)
    {
        foreach (var marker in diagnostics.OrderBy(m => m.Severity == DiagnosticSeverity.Error ? 1 : 0))
        {
            var span = marker.Span;
            if (span.IsEmpty)
                span = Widen(snapshot, span.Start);
            if (span.End < visible.Start || span.Start > visible.End || marker.Severity == DiagnosticSeverity.Hint)
                continue;

            var pen = marker.Severity switch
            {
                DiagnosticSeverity.Error => brushes.Error,
                DiagnosticSeverity.Warning => brushes.Warning,
                _ => brushes.Information,
            };
            foreach (var rect in SpanRects(span, left))
                Squiggle(context, pen, rect.Left, rect.Right, rect.Bottom - 2);
        }
    }

    // An empty problem span is drawn under the word at it, or one character.
    private static TextSpan Widen(TextSnapshot snapshot, int offset)
    {
        var word = WordBoundaries.GetWordAt(snapshot, offset);
        if (!word.IsEmpty)
            return word;

        return offset < snapshot.Length && snapshot[offset] != '\n' ? new TextSpan(offset, 1) : new TextSpan(Math.Max(0, offset - 1), offset > 0 ? 1 : 0);
    }

    private static void Squiggle(DrawingContext context, IPen pen, double x1, double x2, double y)
    {
        if (x2 - x1 < 2)
            x2 = x1 + 4;

        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            const double step = 2;
            stream.BeginFigure(new Point(x1, y), false);
            var up = true;
            for (var x = x1 + step; x <= x2; x += step)
            {
                stream.LineTo(new Point(x, up ? y - 1.5 : y));
                up = !up;
            }

            stream.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private void Underline(DrawingContext context, IPen pen, TextSpan span, double left)
    {
        foreach (var rect in SpanRects(span, left))
            context.DrawLine(pen, new Point(rect.Left, rect.Bottom - 3), new Point(rect.Right, rect.Bottom - 3));
    }

    private IEnumerable<Rect> SpanRects(TextSpan span, double left)
    {
        var snapshot = Snapshot;
        var (first, last) = VisibleLines();
        var startLine = Math.Max(first, snapshot.GetLineFromPosition(Math.Min(span.Start, snapshot.Length)).LineNumber);
        var endLine = Math.Min(last, snapshot.GetLineFromPosition(Math.Min(span.End, snapshot.Length)).LineNumber);
        for (var number = startLine; number <= endLine; number++)
        {
            var line = snapshot.GetLine(number);
            var start = Math.Max(span.Start, line.Start) - line.Start;
            var end = Math.Min(span.End, line.End) - line.Start;
            if (end < start)
                continue;

            var visual = GetVisualLine(number);
            var x1 = visual.GetX(start);
            var x2 = visual.GetX(end);
            yield return new Rect(left + x1, LineTop(number), Math.Max(2, x2 - x1), LineHeight);
        }
    }

    private void DrawGutter(DrawingContext context, EditorBrushes brushes, TextSnapshot snapshot, int first, int last, int caretLine, double gutterWidth)
    {
        context.FillRectangle(brushes.Background, new Rect(0, 0, gutterWidth, Bounds.Height));
        DrawDiffLines(context, brushes, first, last, 0, gutterWidth);
        if (options.HighlightCurrentLine && selection.IsEmpty)
            context.FillRectangle(brushes.CurrentLine, new Rect(0, LineTop(caretLine), gutterWidth, LineHeight));

        DrawProblemMarkers(context, brushes, snapshot, first, last);
        DrawChangeMarkers(context, brushes);
        DrawGlyphs(context, first, last);
        if (!options.ShowLineNumbers)
            return;

        if (!ReferenceEquals(lineNumberBrushes, brushes) || lineNumbers.Count > 800)
        {
            foreach (var cached in lineNumbers.Values)
                cached.Dispose();
            lineNumbers.Clear();
            lineNumberBrushes = brushes;
        }

        var right = gutterWidth - 14;
        for (var number = first; number <= last; number++)
        {
            var current = number == caretLine;
            if (!lineNumbers.TryGetValue((number, current), out var text))
            {
                text = Formatting.FormatPlain((number + 1).ToString(CultureInfo.InvariantCulture), current ? brushes.CurrentLineNumber : brushes.LineNumber);
                lineNumbers[(number, current)] = text;
            }

            text.Draw(context, new Point(right - text.WidthIncludingTrailingWhitespace, LineTop(number)));
        }
    }

    private void DrawProblemMarkers(DrawingContext context, EditorBrushes brushes, TextSnapshot snapshot, int first, int last)
    {
        if (diagnostics.Count == 0)
            return;

        var worst = new Dictionary<int, DiagnosticSeverity>();
        var visible = TextSpan.FromBounds(snapshot.GetLine(first).Start, snapshot.GetLine(last).EndIncludingBreak);
        foreach (var marker in diagnostics)
        {
            if (marker.Severity > DiagnosticSeverity.Warning || marker.Span.Start > visible.End || marker.Span.End < visible.Start)
                continue;

            var number = snapshot.GetLineFromPosition(Math.Min(marker.Span.Start, snapshot.Length)).LineNumber;
            if (!worst.TryGetValue(number, out var severity) || marker.Severity < severity)
                worst[number] = marker.Severity;
        }

        foreach (var (number, severity) in worst)
        {
            var brush = severity == DiagnosticSeverity.Error ? brushes.ErrorMarker : brushes.WarningMarker;
            context.FillRectangle(brush, new Rect(3, LineTop(number) + 3, 3, LineHeight - 6), 1.5f);
        }
    }
}
