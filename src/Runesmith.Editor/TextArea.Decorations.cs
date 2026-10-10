using Avalonia;
using Avalonia.Media;
using Runesmith.Editor.Decorations;
using Runesmith.Editor.Rendering;
using Runesmith.Sdk.Documents;
using Runesmith.Text;

namespace Runesmith.Editor;

public sealed partial class TextArea
{
    /// <summary>The width of the column of the gutter that holds gutter icons and the light bulb.</summary>
    internal const double GlyphMarginWidth = 18;

    private const double GlyphLeft = 7;
    private const double GlyphSize = 14;
    private static readonly int[] NoLines = [];

    private readonly DecorationStore decorations = new();
    private readonly List<(Rect Bounds, PlacedDecoration Lens)> lensBounds = [];
    private DecorationPaint? decorationPaint;
    private int[] lensLines = NoLines;
    private int? lightBulbLine;
    private EditorCaretStyle caretStyle;

    /// <summary>Gets the decorations, per source; change them with <c>SetDecorations</c>.</summary>
    public DecorationStore Decorations => decorations;

    /// <summary>Gets or sets the line the light bulb shows on, for the code actions there, or null to hide it.</summary>
    public int? LightBulbLine
    {
        get => lightBulbLine;
        set
        {
            if (lightBulbLine == value)
                return;

            lightBulbLine = value;
            InvalidateVisual();
        }
    }

    /// <summary>Gets or sets how the caret is drawn.</summary>
    public EditorCaretStyle CaretStyle
    {
        get => caretStyle;
        set
        {
            caretStyle = value;
            InvalidateVisual();
        }
    }

    /// <summary>Raised when decorations change, after the text area has taken them in.</summary>
    public event EventHandler? DecorationsChanged;

    /// <summary>Raised when the user clicks the light bulb.</summary>
    public event EventHandler? LightBulbClicked;

    /// <summary>Raised when the user clicks a gutter icon or a code lens that runs a command: its id and argument.</summary>
    public event EventHandler<(string CommandId, object? Argument)>? DecorationCommandRequested;

    /// <summary>Gets or sets what a click in the icon column of a line without a clickable icon does, such as adding a breakpoint: it gets the
    /// line's number and returns whether it did anything; null leaves such clicks to select the line.</summary>
    public Func<int, bool>? GlyphMarginClick { get; set; }

    private DecorationPaint Paint => decorationPaint ??= DecorationPaint.From(this, Formatting);

    /// <summary>Replaces one source's decorations; their spans must be in the current text.</summary>
    public void SetDecorations(object source, IReadOnlyList<Decoration> decorationList)
    {
        var hadInlays = decorations.HasInlayHints;
        if (decorations.Replace(source, decorationList, Snapshot.Length))
            OnDecorationsChanged(hadInlays || decorations.HasInlayHints);
    }

    /// <summary>Replaces one source's decorations with ones placed in the current text, such as by <see cref="DecorationStore.Place"/>.</summary>
    public void SetDecorations(object source, IReadOnlyList<PlacedDecoration> placed)
    {
        var hadInlays = decorations.HasInlayHints;
        if (decorations.Replace(source, placed))
            OnDecorationsChanged(hadInlays || decorations.HasInlayHints);
    }

    /// <summary>Removes one source's decorations.</summary>
    public void ClearDecorations(object source)
    {
        var hadInlays = decorations.HasInlayHints;
        decorations.Remove(source);
        OnDecorationsChanged(hadInlays);
    }

    /// <summary>Gets the text of the highlights and inlay hints at an offset that have tooltips, for the hover.</summary>
    public IReadOnlyList<string> GetDecorationToolTips(int offset)
    {
        if (decorations.Count == 0)
            return [];

        return
        [
            .. decorations.In<TextHighlight>(new TextSpan(offset, 0))
                .Concat(options.ShowInlayHints ? decorations.In<InlayHint>(new TextSpan(offset, 0)) : [])
                .Where(p => p.Decoration.ToolTip is { Length: > 0 } && p.Span.Start <= offset && offset <= p.Span.End)
                .Select(p => p.Decoration.ToolTip!),
        ];
    }

    /// <summary>Gets the tooltip of the gutter icon under a point, and the icon's rectangle, or null.</summary>
    public (string ToolTip, Rect Bounds)? GetGlyphToolTip(Point point)
    {
        if (GlyphLineAt(point) is not { } line || lightBulbLine == line || MarkerOn(line) is not { Decoration.ToolTip: { Length: > 0 } tip })
            return null;

        return (tip, GlyphRect(line));
    }

    // Called with every edit; decorations follow the text, and code lens rows move with their lines.
    private void MapDecorations(TextChangeSet changeSet)
    {
        if (decorations.Count == 0)
            return;

        var hadLenses = decorations.HasCodeLenses;
        decorations.Map(changeSet.Changes);
        if (hadLenses || decorations.HasCodeLenses)
            UpdateLensLines(changeSet.After);
        DecorationsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnDecorationsChanged(bool inlaysChanged)
    {
        if (inlaysChanged)
        {
            cache.Clear();
            PostExtentChanged();
        }

        UpdateLensLines(Snapshot);
        InvalidateVisual();
        DecorationsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateLensLines(TextSnapshot snapshot)
    {
        var lines = !options.ShowCodeLens || !decorations.HasCodeLenses
            ? NoLines
            : [.. decorations.All<CodeLens>().Select(p => snapshot.GetLineFromPosition(Math.Min(p.Span.Start, snapshot.Length)).LineNumber).Where(line => !IsHiddenLine(line)).Distinct().Order()];
        if (lines.AsSpan().SequenceEqual(lensLines))
            return;

        lensLines = lines;
        RebuildRows();
    }

    private LineInlays InlaysOn(int lineNumber)
    {
        if (!decorations.HasInlayHints || !options.ShowInlayHints)
            return LineInlays.None;

        var line = Snapshot.GetLine(lineNumber);
        return LineInlays.Create(decorations.In<InlayHint>(line.Span)
            .Where(p => p.Span.Start >= line.Start && p.Span.Start <= line.End)
            .Select(p => (InlayHint)p.Decoration is var hint && hint.Side == InlayHintSide.Before
                ? (p.Span.Start - line.Start, Padded(hint.Text), true)
                : (p.Span.Start - line.Start, hint.Text, false)));
    }

    // A hint before text is kept a space apart from it.
    private static string Padded(string text) => text.Length > 0 && !char.IsWhiteSpace(text[^1]) ? text + " " : text;

    private PlacedDecoration? MarkerOn(int lineNumber)
    {
        var line = Snapshot.GetLine(lineNumber);
        foreach (var placed in decorations.In<GutterMarker>(line.Span))
        {
            if (placed.Span.Start >= line.Start && placed.Span.Start <= line.End)
                return placed;
        }

        return null;
    }

    private Rect GlyphRect(int lineNumber) => new(GlyphLeft, LineTop(lineNumber) + (LineHeight - GlyphSize) / 2, GlyphSize, GlyphSize);

    private int? GlyphLineAt(Point point)
    {
        if (point.X < GlyphLeft - 2 || point.X > GlyphLeft + GlyphSize + 2 || lensLines.Length > 0 && LensAt(point) is not null)
            return null;

        var row = (int)Math.Floor((point.Y + scrollOffset.Y) / LineHeight);
        var line = LineAtRow(row);
        return RowOf(line) == row ? line : null;
    }

    private PlacedDecoration? LensAt(Point point)
    {
        foreach (var (bounds, lens) in lensBounds)
        {
            if (bounds.Contains(point))
                return lens;
        }

        return null;
    }

    // Handles a click on the light bulb, a gutter icon or a code lens; returns whether it did.
    private bool ClickDecoration(Point point)
    {
        if (ClickFolding(point))
            return true;

        if (lensLines.Length > 0 && LensAt(point) is { } lens)
        {
            if (lens.Decoration is CodeLens { CommandId: { } lensCommand } codeLens)
                DecorationCommandRequested?.Invoke(this, (lensCommand, codeLens.CommandArgument));
            return true;
        }

        if (GlyphLineAt(point) is not { } line)
            return false;

        if (line == lightBulbLine)
        {
            LightBulbClicked?.Invoke(this, EventArgs.Empty);
            return true;
        }

        if (MarkerOn(line) is { Decoration: GutterMarker { CommandId: { } command } marker })
        {
            DecorationCommandRequested?.Invoke(this, (command, marker.CommandArgument));
            return true;
        }

        return GlyphMarginClick?.Invoke(line) == true;
    }

    /// <summary>Whether a point is over something in the gutter or a code lens that a click runs.</summary>
    private bool IsOverClickableDecoration(Point point) =>
        IsOverFoldControl(point)
        || lensLines.Length > 0 && LensAt(point) is { Decoration: CodeLens { CommandId: not null } }
        || GlyphLineAt(point) is { } line && (line == lightBulbLine || GlyphMarginClick is not null || MarkerOn(line) is { Decoration: GutterMarker { CommandId: not null } });

    private void DrawHighlightBackgrounds(DrawingContext context, TextSpan visible, double left)
    {
        if (decorations.Count == 0)
            return;

        foreach (var placed in decorations.In<TextHighlight>(visible))
        {
            var highlight = (TextHighlight)placed.Decoration;
            if (!highlight.Background)
                continue;

            foreach (var rect in SpanRects(placed.Span, left))
                context.FillRectangle(Paint.Background(highlight.Tone), rect, 2);
        }
    }

    private void DrawHighlightUnderlines(DrawingContext context, TextSpan visible, double left)
    {
        if (decorations.Count == 0)
            return;

        foreach (var placed in decorations.In<TextHighlight>(visible))
        {
            var highlight = (TextHighlight)placed.Decoration;
            if (highlight.Underline == UnderlineStyle.None)
                continue;

            var pen = Paint.Underline(highlight.Tone, highlight.Underline);
            foreach (var rect in SpanRects(placed.Span.IsEmpty ? Widen(Snapshot, placed.Span.Start) : placed.Span, left))
            {
                if (highlight.Underline == UnderlineStyle.Wavy)
                    Squiggle(context, pen, rect.Left, rect.Right, rect.Bottom - 2);
                else
                    context.DrawLine(pen, new Point(rect.Left, rect.Bottom - 2.5), new Point(rect.Right, rect.Bottom - 2.5));
            }
        }
    }

    private void DrawInlayBackgrounds(DrawingContext context, int first, int last, double left)
    {
        if (!decorations.HasInlayHints || !options.ShowInlayHints)
            return;

        for (var number = first; number <= last; number = NextShownLine(number))
        {
            var visual = GetVisualLine(number);
            if (visual.Inlays.IsEmpty)
                continue;

            var top = LineTop(number);
            foreach (var (x1, x2, _) in visual.InlayBounds())
                context.FillRectangle(Paint.HintBackground, new Rect(left + x1, top + 2, Math.Max(0, x2 - x1), LineHeight - 4), 3);
        }
    }

    private void DrawGlyphs(DrawingContext context, int first, int last)
    {
        if (lightBulbLine is { } bulb && bulb >= first && bulb <= last)
            DrawIcon(context, HammerUI.Icons.Lightbulb, Paint.LightBulb, GlyphRect(bulb));

        if (decorations.Count == 0)
            return;

        var snapshot = Snapshot;
        var visible = TextSpan.FromBounds(snapshot.GetLine(first).Start, snapshot.GetLine(last).EndIncludingBreak);
        var drawn = new HashSet<int>();
        foreach (var placed in decorations.In<GutterMarker>(visible))
        {
            var line = snapshot.GetLineFromPosition(Math.Min(placed.Span.Start, snapshot.Length)).LineNumber;
            var marker = (GutterMarker)placed.Decoration;
            if (line < first || line > last || line == lightBulbLine || IsHiddenLine(line) || !drawn.Add(line) || HammerUI.Icons.Find(marker.Icon) is not { } icon)
                continue;

            DrawIcon(context, icon, Paint.Tone(marker.Tone), GlyphRect(line), marker.IsFilled);
        }
    }

    private static void DrawIcon(DrawingContext context, Geometry icon, IBrush brush, Rect rect, bool filled = false)
    {
        var scale = rect.Width / 24;
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(Math.Round(rect.X), Math.Round(rect.Y))))
            context.DrawGeometry(filled ? brush : null, new Pen(brush, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), icon);
    }

    private void DrawCodeLenses(DrawingContext context, int first, int last, double width)
    {
        lensBounds.Clear();
        if (lensLines.Length == 0 || Paint.HintText is not { } properties)
            return;

        var snapshot = Snapshot;
        var gutterWidth = GutterWidth;
        using var clip = context.PushClip(new Rect(gutterWidth, 0, Math.Max(0, width - gutterWidth), Bounds.Height));
        var visible = TextSpan.FromBounds(snapshot.GetLine(first).Start, snapshot.GetLine(Math.Min(last + 1, snapshot.LineCount - 1)).End);
        foreach (var group in decorations.In<CodeLens>(visible).GroupBy(p => snapshot.GetLineFromPosition(Math.Min(p.Span.Start, snapshot.Length)).LineNumber))
        {
            var line = group.Key;
            if (line < first || line > last + 1 || IsHiddenLine(line))
                continue;

            var text = snapshot.GetLineText(line);
            var indent = GetVisualLine(line).GetX(Editing.EditOperations.LeadingWhitespace(text).Length);
            var x = TextLeft + indent;
            var top = LineTop(line) - LineHeight;
            foreach (var lens in group.OrderBy(p => p.Span.Start))
            {
                if (x > TextLeft + indent)
                {
                    using var separator = Formatting.FormatPlain(" | ", Paint.Muted);
                    separator.Draw(context, new Point(x, top + 2));
                    x += separator.WidthIncludingTrailingWhitespace;
                }

                using var formatted = Avalonia.Media.TextFormatting.TextFormatter.Current.FormatLine(
                    new SingleRun(((CodeLens)lens.Decoration).Text, properties), 0, double.PositiveInfinity, Formatting.Paragraph(options.TabSize));
                if (formatted is null)
                    continue;

                formatted.Draw(context, new Point(x, top + 2));
                lensBounds.Add((new Rect(x, top, formatted.WidthIncludingTrailingWhitespace, LineHeight), lens));
                x += formatted.WidthIncludingTrailingWhitespace;
            }
        }
    }

    private void DrawCaret(DrawingContext context, EditorBrushes brushes, TextPosition caret, double left)
    {
        var visual = GetVisualLine(caret.Line);
        var x = left + visual.GetX(caret.Column);
        var top = LineTop(caret.Line);
        switch (caretStyle)
        {
            case EditorCaretStyle.Block or EditorCaretStyle.Underline:
                var next = caret.Column < visual.Text.Length ? left + visual.GetX(caret.Column + 1) : x + Formatting.CharacterWidth;
                var width = Math.Max(2, next - x);
                if (caretStyle == EditorCaretStyle.Block)
                    context.FillRectangle(new Avalonia.Media.Immutable.ImmutableSolidColorBrush(((ISolidColorBrush)brushes.Caret).Color, 0.55), new Rect(x, top, width, LineHeight));
                else
                    context.FillRectangle(brushes.Caret, new Rect(x, top + LineHeight - 2, width, 2));
                break;
            default:
                context.FillRectangle(brushes.Caret, new Rect(Math.Round(x) - 1, top, 2, LineHeight));
                break;
        }
    }

    private sealed class SingleRun(string text, Avalonia.Media.TextFormatting.TextRunProperties properties) : Avalonia.Media.TextFormatting.ITextSource
    {
        public Avalonia.Media.TextFormatting.TextRun? GetTextRun(int textSourceIndex) =>
            textSourceIndex < text.Length
                ? new Avalonia.Media.TextFormatting.TextCharacters(text.AsMemory(textSourceIndex), properties)
                : new Avalonia.Media.TextFormatting.TextEndOfParagraph();
    }
}
