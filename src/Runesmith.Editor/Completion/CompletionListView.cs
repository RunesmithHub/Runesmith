using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;

namespace Runesmith.Editor.Completion;

/// <summary>The suggestion list, drawn row by row like the editor's text, so a new filter costs a redraw of the visible rows and no
/// controls.</summary>
internal sealed class CompletionListView : Control
{
    public const double RowHeight = 24;
    private const double IconSize = 14;
    private const double Padding = 4;

    private readonly Dictionary<CompletionEntry, (TextLayout Label, TextLayout? Detail)> layouts = new(ReferenceEqualityComparer.Instance);
    private IReadOnlyList<CompletionEntry> entries = [];
    private int selectedIndex = -1;
    private int firstVisible;
    private int visibleRows = 12;
    private Palette? palette;

    public CompletionListView()
    {
        ClipToBounds = true;
        Focusable = false;
        Cursor = new Cursor(StandardCursorType.Hand);
        ActualThemeVariantChanged += (_, _) => ResetPalette();
    }

    /// <summary>Raised when another suggestion is selected, also when new entries put another item at the selected row.</summary>
    public event EventHandler? SelectedChanged;

    /// <summary>Raised when a suggestion is double-clicked.</summary>
    public event EventHandler? Accepted;

    public CompletionEntry? Selected => selectedIndex >= 0 && selectedIndex < entries.Count ? entries[selectedIndex] : null;

    public int Count => entries.Count;

    /// <summary>Gets or sets how many rows show before the list scrolls.</summary>
    public int VisibleRows
    {
        get => visibleRows;
        set
        {
            visibleRows = value;
            InvalidateMeasure();
        }
    }

    public void SetEntries(IReadOnlyList<CompletionEntry> value, int selected)
    {
        var heightChanged = Math.Min(entries.Count, visibleRows) != Math.Min(value.Count, visibleRows);
        var previous = Selected?.Item;
        entries = value;
        if (layouts.Count > 512)
            layouts.Clear();
        firstVisible = 0;
        var index = value.Count == 0 ? -1 : Math.Clamp(selected, 0, value.Count - 1);
        Select(index, !ReferenceEquals(previous, index >= 0 ? value[index].Item : null));
        if (heightChanged)
            InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>Moves the selection by some rows, wrapping around at the ends when moving one row.</summary>
    public void MoveSelection(int rows)
    {
        if (entries.Count == 0)
            return;

        var index = selectedIndex + rows;
        Select(Math.Abs(rows) == 1 ? (index % entries.Count + entries.Count) % entries.Count : Math.Clamp(index, 0, entries.Count - 1));
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 380 : availableSize.Width, Math.Max(1, Math.Min(entries.Count, visibleRows)) * RowHeight + 2 * Padding);

    public override void Render(DrawingContext context)
    {
        var colors = palette ??= Palette.From(this);
        var width = Bounds.Width;
        var last = Math.Min(entries.Count, firstVisible + visibleRows);
        for (var index = firstVisible; index < last; index++)
        {
            var entry = entries[index];
            var top = Padding + (index - firstVisible) * RowHeight;
            if (index == selectedIndex)
                context.FillRectangle(colors.Selection, new Rect(Padding, top, width - 2 * Padding, RowHeight), 4);

            DrawIcon(context, colors, entry, new Point(Padding + 8, top + (RowHeight - IconSize) / 2));
            var (label, detail) = LayoutsOf(entry, colors);
            var textLeft = Padding + 8 + IconSize + 8;
            var detailWidth = detail?.WidthIncludingTrailingWhitespace ?? 0;
            label.Draw(context, new Point(textLeft, top + (RowHeight - label.Height) / 2));
            detail?.Draw(context, new Point(width - Padding - 8 - detailWidth, top + (RowHeight - detail.Height) / 2));
        }

        if (entries.Count > visibleRows)
        {
            var trackHeight = Bounds.Height - 2 * Padding;
            var thumbHeight = Math.Max(16, trackHeight * visibleRows / entries.Count);
            var thumbTop = Padding + (trackHeight - thumbHeight) * firstVisible / Math.Max(1, entries.Count - visibleRows);
            context.FillRectangle(colors.Scroll, new Rect(width - 5, thumbTop, 3, thumbHeight), 1.5f);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var index = IndexAt(e.GetPosition(this));
        if (index < 0)
            return;

        Select(index);
        if (e.ClickCount >= 2)
            Accepted?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        Scroll(firstVisible - (int)Math.Round(e.Delta.Y * 3));
        e.Handled = true;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        layouts.Clear();
    }

    private void Select(int index, bool itemChanged = false)
    {
        var changed = itemChanged || index != selectedIndex;
        selectedIndex = index;
        if (index >= 0)
        {
            if (index < firstVisible)
                firstVisible = index;
            else if (index >= firstVisible + visibleRows)
                firstVisible = index - visibleRows + 1;
        }

        InvalidateVisual();
        if (changed)
            SelectedChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Scroll(int first)
    {
        firstVisible = Math.Clamp(first, 0, Math.Max(0, entries.Count - visibleRows));
        InvalidateVisual();
    }

    private int IndexAt(Point point)
    {
        var index = firstVisible + (int)Math.Floor((point.Y - Padding) / RowHeight);
        return point.Y < Padding || index >= Math.Min(entries.Count, firstVisible + visibleRows) ? -1 : index;
    }

    private (TextLayout Label, TextLayout? Detail) LayoutsOf(CompletionEntry entry, Palette colors)
    {
        if (layouts.TryGetValue(entry, out var cached))
            return cached;

        var detailText = entry.Item.Detail?.ReplaceLineEndings(" ");
        TextLayout? detail = string.IsNullOrEmpty(detailText)
            ? null
            : new TextLayout(detailText, colors.Typeface, 12, colors.Muted, TextAlignment.Left, TextWrapping.NoWrap, TextTrimming.CharacterEllipsis,
                null, FlowDirection.LeftToRight, 150, RowHeight, 0, 0, 1, null, null);
        var labelWidth = Math.Max(40, Bounds.Width - 2 * Padding - 8 - IconSize - 8 - 8 - (detail?.WidthIncludingTrailingWhitespace + 12 ?? 0));
        var label = new TextLayout(entry.Item.Label, colors.Typeface, 13, colors.Text, TextAlignment.Left, TextWrapping.NoWrap, TextTrimming.CharacterEllipsis,
            null, FlowDirection.LeftToRight, labelWidth, RowHeight, 0, 0, 1, null, Highlights(entry, colors));
        layouts[entry] = (label, detail);
        return (label, detail);
    }

    // Matched characters are drawn bold in the accent color, as one run per stretch of matched characters.
    private static List<ValueSpan<TextRunProperties>>? Highlights(CompletionEntry entry, Palette colors)
    {
        if (entry.Matched.Count == 0)
            return null;

        var spans = new List<ValueSpan<TextRunProperties>>();
        var sorted = entry.Matched.Order().ToList();
        var start = sorted[0];
        var length = 1;
        for (var i = 1; i <= sorted.Count; i++)
        {
            if (i < sorted.Count && sorted[i] == start + length)
            {
                length++;
                continue;
            }

            if (start + length <= entry.Item.Label.Length)
                spans.Add(new ValueSpan<TextRunProperties>(start, length, colors.Match));
            if (i < sorted.Count)
            {
                start = sorted[i];
                length = 1;
            }
        }

        return spans;
    }

    private static void DrawIcon(DrawingContext context, Palette colors, CompletionEntry entry, Point origin)
    {
        var geometry = CompletionKinds.Icon(entry.Item.Kind);
        using (context.PushTransform(Matrix.CreateScale(IconSize / 24, IconSize / 24) * Matrix.CreateTranslation(origin.X, origin.Y)))
            context.DrawGeometry(null, colors.IconPen(CompletionKinds.ColorKey(entry.Item.Kind)), geometry);
    }

    private void ResetPalette()
    {
        palette = null;
        layouts.Clear();
        InvalidateVisual();
    }

    /// <summary>The theme's colors for the list, resolved once per theme.</summary>
    private sealed class Palette
    {
        private readonly Dictionary<string, IPen> iconPens = [];
        private readonly Control owner;

        private Palette(Control owner) => this.owner = owner;

        public required IBrush Text { get; init; }

        public required IBrush Muted { get; init; }

        public required IBrush Selection { get; init; }

        public required IBrush Scroll { get; init; }

        public required TextRunProperties Match { get; init; }

        public required Typeface Typeface { get; init; }

        public static Palette From(Control control)
        {
            var typeface = new Typeface(FontFamily.Default);
            var text = Brush(control, "TextPrimaryBrush", Colors.White);
            var accent = Brush(control, "AccentBrush", Colors.SlateBlue);
            return new Palette(control)
            {
                Text = text,
                Muted = Brush(control, "TextMutedBrush", Colors.Gray),
                Selection = Brush(control, "AccentSubtleBrush", Color.FromArgb(48, 112, 115, 246)),
                Scroll = Brush(control, "BorderStrongBrush", Colors.DimGray),
                Typeface = typeface,
                Match = new GenericTextRunProperties(new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold), 13, null, accent, null,
                    BaselineAlignment.Baseline, CultureInfo.CurrentCulture, null),
            };
        }

        public IPen IconPen(string key)
        {
            if (!iconPens.TryGetValue(key, out var pen))
            {
                pen = new ImmutablePen(Brush(owner, key, Colors.Gray), 2, null, PenLineCap.Round, PenLineJoin.Round);
                iconPens[key] = pen;
            }

            return pen;
        }

        private static ImmutableSolidColorBrush Brush(Control control, string key, Color fallback) =>
            control.TryFindResource(key, control.ActualThemeVariant, out var value) && value is ISolidColorBrush brush
                ? new ImmutableSolidColorBrush(brush.Color, brush.Opacity)
                : new ImmutableSolidColorBrush(fallback);
    }
}
