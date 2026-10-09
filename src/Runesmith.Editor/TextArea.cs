using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Runesmith.Editor.Rendering;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Text;

namespace Runesmith.Editor;

/// <summary>The surface of the editor that draws a document's text and edits it: the gutter, the text, the selection and the caret.</summary>
/// <remarks>It scrolls itself; <see cref="TextEditor"/> puts scroll bars, the find bar and popups around it.</remarks>
public sealed partial class TextArea : Control
{
    internal const double TextPadding = 8;
    private const int MinimumLineNumberDigits = 3;

    private readonly VisualLineCache cache = new();
    private readonly DispatcherTimer caretTimer;
    private EditorOptions options = EditorOptions.Default;
    private TextFormatting? formatting;
    private EditorBrushes? brushes;
    private EditorSelection selection;
    private Vector scrollOffset;
    private double maxLineWidth;
    private bool extentChangePosted;
    private bool caretVisible = true;
    private ISyntaxHighlighter? highlighter;
    private IReadOnlyList<DiagnosticMarker> diagnostics = [];
    private IReadOnlyList<TextSpan> searchMatches = [];
    private TextSpan? currentSearchMatch;
    private TextSpan? link;
    private (int Open, int Close)? bracketMatch;

    // An edit moves the selection with the text before it calls Select, which must still report the move.
    private bool selectionMovedByEdit;

    // When the last key press arrived, until the frame that shows its result is drawn.
    private long inputTimestamp;

    public TextArea(IDocument document)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Focusable = true;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Ibeam);
        caretTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(530) };
        caretTimer.Tick += (_, _) => BlinkCaret();
        document.Buffer.Changed += OnBufferChanged;
        ResourcesChanged += (_, _) => ResetTheme();
        ActualThemeVariantChanged += (_, _) => ResetTheme();
        RecomputeMaxLineWidth();
    }

    public IDocument Document { get; }

    /// <summary>Gets the text as it is now.</summary>
    public TextSnapshot Snapshot => Document.Buffer.Current;

    /// <summary>Gets or sets the document's language, for comments, brackets and quotes.</summary>
    public LanguageDefinition? Language { get; set; }

    public EditorOptions Options
    {
        get => options;
        set
        {
            if (options == value)
                return;

            options = value;
            ResetFormatting();
        }
    }

    /// <summary>Gets the selection; use <see cref="Select"/> to change it.</summary>
    public EditorSelection Selection => selection;

    /// <summary>Gets or sets the highlighter that colors the text.</summary>
    public ISyntaxHighlighter? Highlighter
    {
        get => highlighter;
        set
        {
            if (highlighter is not null)
                highlighter.Changed -= OnHighlightingChanged;
            highlighter = value;
            if (highlighter is not null)
                highlighter.Changed += OnHighlightingChanged;
            cache.Clear();
            InvalidateVisual();
        }
    }

    internal IReadOnlyList<DiagnosticMarker> Diagnostics
    {
        get => diagnostics;
        set
        {
            diagnostics = value;
            InvalidateVisual();
        }
    }

    /// <summary>Gets or sets the spans the find bar found, drawn highlighted.</summary>
    public IReadOnlyList<TextSpan> SearchMatches
    {
        get => searchMatches;
        set
        {
            searchMatches = value;
            InvalidateVisual();
        }
    }

    /// <summary>Gets or sets the search match the find bar is at, drawn with a border.</summary>
    public TextSpan? CurrentSearchMatch
    {
        get => currentSearchMatch;
        set
        {
            currentSearchMatch = value;
            InvalidateVisual();
        }
    }

    /// <summary>Gets or sets a span drawn as a link, such as the symbol under the pointer while Ctrl is held.</summary>
    public TextSpan? Link
    {
        get => link;
        set
        {
            if (link == value)
                return;

            link = value;
            Cursor = new Cursor(value is null ? StandardCursorType.Ibeam : StandardCursorType.Hand);
            InvalidateVisual();
        }
    }

    /// <summary>Gets or sets how far the text is scrolled.</summary>
    public Vector ScrollOffset
    {
        get => scrollOffset;
        set
        {
            var extent = Extent;
            var clamped = new Vector(
                Math.Clamp(value.X, 0, Math.Max(0, extent.Width - Bounds.Width)),
                Math.Clamp(value.Y, 0, Math.Max(0, extent.Height - Bounds.Height)));
            if (clamped == scrollOffset)
                return;

            scrollOffset = clamped;
            InvalidateVisual();
            ScrollChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Gets the size of everything that can be scrolled to; the last line can scroll up to near the top.</summary>
    public Size Extent
    {
        get
        {
            var lineHeight = LineHeight;
            var height = RowCount * lineHeight + Math.Max(0, Bounds.Height - 3 * lineHeight);
            return new Size(GutterWidth + TextPadding + maxLineWidth + Formatting.CharacterWidth * 4, height);
        }
    }

    public double LineHeight => Formatting.LineHeight;

    /// <summary>Gets the width of the gutter, where the line numbers are.</summary>
    public double GutterWidth =>
        options.ShowLineNumbers
            ? Math.Max(MinimumLineNumberDigits, Snapshot.LineCount.ToString(System.Globalization.CultureInfo.InvariantCulture).Length) * Formatting.CharacterWidth + 30 + GlyphMarginWidth
            : 14 + GlyphMarginWidth;

    /// <summary>Raised when the caret moves or the selection changes.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Raised when the scroll offset or the extent changes.</summary>
    public event EventHandler? ScrollChanged;

    /// <summary>Raised after the user types a character, for completion and signature help.</summary>
    public event EventHandler<char>? CharacterTyped;

    /// <summary>Raised when the user asks to zoom with Ctrl and the mouse wheel: 1 to zoom in, -1 to zoom out.</summary>
    public event EventHandler<int>? ZoomRequested;

    /// <summary>Raised when the user Ctrl+clicks a symbol, with its offset.</summary>
    public event EventHandler<int>? DefinitionRequested;

    internal TextFormatting Formatting => formatting ??= new TextFormatting(options, Brushes.Text);

    private EditorBrushes Brushes => brushes ??= EditorBrushes.From(this);

    /// <summary>Selects a span or moves the caret, and scrolls the caret into view.</summary>
    public void Select(EditorSelection value, bool scrollIntoView = true)
    {
        value = value.Clamp(Snapshot.Length);
        var changed = value != selection || selectionMovedByEdit;
        selectionMovedByEdit = false;
        selection = value;
        preferredX = null;
        UpdateBracketMatch();
        ShowCaret();
        if (scrollIntoView)
            ScrollToCaret();
        if (changed)
            SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Scrolls so the caret is in view, with some room around it.</summary>
    public void ScrollToCaret() => ScrollIntoView(selection.Caret, center: false);

    /// <summary>Scrolls so an offset is in view; with <paramref name="center"/>, an offset off screen ends up in the middle.</summary>
    public void ScrollIntoView(int offset, bool center)
    {
        if (Bounds.Height <= 0)
        {
            pendingScrollOffset = (offset, center);
            return;
        }

        var position = Snapshot.GetPosition(Math.Clamp(offset, 0, Snapshot.Length));
        var lineHeight = LineHeight;
        var top = RowOf(position.Line) * lineHeight;
        var y = scrollOffset.Y;
        if (top < y + lineHeight || top + lineHeight > y + Bounds.Height - lineHeight)
        {
            var offScreen = top < y || top + lineHeight > y + Bounds.Height;
            y = center && offScreen
                ? top - (Bounds.Height - lineHeight) / 2
                : top < y + lineHeight ? top - lineHeight : top + 2 * lineHeight - Bounds.Height;
        }

        var x = GetVisualLine(position.Line).GetX(position.Column);
        var width = Bounds.Width - GutterWidth - TextPadding;
        var margin = Formatting.CharacterWidth * 4;
        var scrollX = scrollOffset.X;
        if (x < scrollX + margin)
            scrollX = Math.Max(0, x - margin);
        else if (x > scrollX + width - margin)
            scrollX = x - width + margin;

        ScrollOffset = new Vector(scrollX, y);
    }

    /// <summary>Gets the caret's rectangle, relative to this control.</summary>
    public Rect GetCaretRect() => GetCharacterRect(selection.Caret);

    /// <summary>Gets the rectangle of the caret position before an offset, relative to this control.</summary>
    public Rect GetCharacterRect(int offset)
    {
        var position = Snapshot.GetPosition(Math.Clamp(offset, 0, Snapshot.Length));
        var x = TextLeft + GetVisualLine(position.Line).GetX(position.Column);
        return new Rect(x, LineTop(position.Line), 2, LineHeight);
    }

    /// <summary>Gets the offset closest to a point relative to this control, or null when the point is in the gutter.</summary>
    public int? GetOffsetFromPoint(Point point, bool allowGutter = false)
    {
        if (!allowGutter && point.X < GutterWidth)
            return null;

        var snapshot = Snapshot;
        var lineNumber = LineAtRow((int)Math.Floor((point.Y + scrollOffset.Y) / LineHeight));
        var line = snapshot.GetLine(lineNumber);
        return line.Start + GetVisualLine(lineNumber).GetColumn(point.X - TextLeft);
    }

    /// <summary>Whether a point lies over text rather than past the end of its line or below the last one.</summary>
    public bool IsOverText(Point point, int offset)
    {
        var rect = GetCharacterRect(offset);
        return point.Y >= rect.Top && point.Y < rect.Bottom && Math.Abs(point.X - rect.X) < Formatting.CharacterWidth * 1.5;
    }

    private (int Offset, bool Center)? pendingScrollOffset;

    private double TextLeft => GutterWidth + TextPadding - scrollOffset.X;

    private double LineTop(int lineNumber) => RowOf(lineNumber) * LineHeight - scrollOffset.Y;

    private (int First, int Last) VisibleLines()
    {
        var lineHeight = LineHeight;
        var first = LineAtRow(Math.Max(0, (int)Math.Floor(scrollOffset.Y / lineHeight)));
        var last = LineAtRow((int)Math.Ceiling((scrollOffset.Y + Bounds.Height) / lineHeight));
        return (first, Math.Max(first, last));
    }

    private VisualLine GetVisualLine(int lineNumber)
    {
        if (cache.TryGet(lineNumber, out var cached))
            return cached;

        var text = Snapshot.GetLineText(lineNumber);
        var tokens = highlighter?.GetTokens(lineNumber) ?? [];
        var formattingNow = Formatting;
        var inlays = InlaysOn(lineNumber);
        var source = new LineTextSource(text, tokens, formattingNow, inlays, inlays.IsEmpty ? null : Paint.HintText);
        var textLine = Avalonia.Media.TextFormatting.TextFormatter.Current.FormatLine(source, 0, double.PositiveInfinity, formattingNow.Paragraph(options.TabSize))
            ?? throw new InvalidOperationException("The text formatter returned no line.");
        var line = new VisualLine(text, textLine, inlays);
        cache.Add(lineNumber, line);
        if (line.Width > maxLineWidth)
        {
            maxLineWidth = line.Width;
            PostExtentChanged();
        }

        return line;
    }

    // Lines are measured while they are drawn, when scroll bars must not change, so they learn of a wider line right after the frame.
    private void PostExtentChanged()
    {
        if (extentChangePosted)
            return;

        extentChangePosted = true;
        Dispatcher.UIThread.Post(() =>
        {
            extentChangePosted = false;
            ScrollChanged?.Invoke(this, EventArgs.Empty);
        }, DispatcherPriority.Background);
    }

    private void ResetFormatting()
    {
        formatting = null;
        decorationPaint = null;
        cache.Clear();
        RecomputeMaxLineWidth();
        UpdateLensLines(Snapshot);
        InvalidateVisual();
        ScrollChanged?.Invoke(this, EventArgs.Empty);
    }

    // Estimated from character counts so a large file opens without formatting every line; drawn lines correct it.
    private void RecomputeMaxLineWidth()
    {
        var snapshot = Snapshot;
        var longest = 0;
        for (var i = 0; i < snapshot.LineCount; i++)
            longest = Math.Max(longest, snapshot.GetLine(i).Length);
        maxLineWidth = longest * Formatting.CharacterWidth;
    }

    private void OnHighlightingChanged(object? sender, HighlightingChangedEventArgs e)
    {
        cache.Invalidate(e.FirstLine, e.LastLine);
        InvalidateVisual();
    }

    private void UpdateBracketMatch()
    {
        bracketMatch = Language is null || !selection.IsEmpty ? null : BracketMatcher.Find(Snapshot, selection.Caret, Language.Brackets);
    }

    private void BlinkCaret()
    {
        caretVisible = !caretVisible;
        InvalidateVisual();
    }

    private void ShowCaret()
    {
        caretVisible = true;
        if (IsFocused)
        {
            caretTimer.Stop();
            caretTimer.Start();
        }

        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width,
        double.IsInfinity(availableSize.Height) ? 300 : availableSize.Height);

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (pendingScrollOffset is { } pending && e.NewSize.Height > 0)
        {
            pendingScrollOffset = null;
            ScrollIntoView(pending.Offset, pending.Center);
        }

        ScrollOffset = scrollOffset;
        ScrollChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        ShowCaret();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        caretTimer.Stop();
        InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ResetTheme();
    }

    private void ResetTheme()
    {
        brushes = null;
        ResetFormatting();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        caretTimer.Stop();
    }

    /// <summary>Stops following the document; call it when the editor closes.</summary>
    public void Detach()
    {
        Document.Buffer.Changed -= OnBufferChanged;
        BaseText = null;
        Highlighter = null;
        caretTimer.Stop();
        cache.Clear();
    }
}
