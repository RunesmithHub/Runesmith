using Avalonia;
using Avalonia.Media;
using Runesmith.Editor.Folding;
using Runesmith.Editor.Rendering;

namespace Runesmith.Editor;

public sealed partial class TextArea
{
    /// <summary>The width of the column of the gutter that holds the fold markers.</summary>
    internal const double FoldMarginWidth = 16;

    private const double FoldMarkerSize = 12;
    private const string DefaultPlaceholder = "...";

    private readonly List<(Rect Bounds, FoldRegion Region)> placeholderBounds = [];
    private bool showsFolding;
    private bool isPointerOverGutter;

    /// <summary>Gets the folding regions and the lines they hide.</summary>
    public FoldingModel Folding { get; } = new();

    /// <summary>Gets or sets whether the gutter shows fold markers; the editor turns it on while it folds.</summary>
    public bool ShowsFolding
    {
        get => showsFolding;
        set
        {
            if (showsFolding == value)
                return;

            showsFolding = value;
            InvalidateVisual();
            ScrollChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private double FoldMargin => showsFolding ? FoldMarginWidth : 0;

    /// <summary>Folds or unfolds the region that starts on a line; returns whether there is one.</summary>
    public bool ToggleFold(int line)
    {
        if (Folding.RegionAt(line) is not { } region)
            return false;

        SetFolded(region, !region.IsCollapsed);
        return true;
    }

    /// <summary>Folds a region, moving the caret out of the lines it hides, or unfolds it.</summary>
    public void SetFolded(FoldRegion region, bool folded)
    {
        ArgumentNullException.ThrowIfNull(region);
        Folding.SetCollapsed(region, folded);
        if (folded)
            KeepSelectionVisible();
    }

    /// <summary>Moves a caret that folding hid to the end of the line that hides it.</summary>
    internal void KeepSelectionVisible()
    {
        var snapshot = Snapshot;
        var caretLine = snapshot.GetLineFromPosition(selection.Caret).LineNumber;
        if (!Folding.IsHidden(caretLine))
            return;

        var shown = snapshot.GetLine(Folding.VisibleLineOf(caretLine));
        Select(Runesmith.Editor.EditorSelection.At(shown.End), scrollIntoView: false);
    }

    private void OnFoldingChanged(object? sender, EventArgs e)
    {
        var lensesBefore = lensLines;
        UpdateLensLines(Snapshot);
        if (ReferenceEquals(lensesBefore, lensLines))
            RebuildRows();
    }

    // The next line drawn after a line, past any lines folding hides.
    private int NextShownLine(int line) => Folding.HasHiddenLines ? Folding.NextVisible(line, int.MaxValue) : line + 1;

    private bool IsHiddenLine(int line) => Folding.HasHiddenLines && Folding.IsHidden(line);

    // The bottom of a line, or of the line that shows it while it is hidden.
    private double LineBottom(int line) => LineTop(Folding.HasHiddenLines ? Folding.VisibleLineOf(line) : line) + LineHeight;

    private Rect FoldMarkerRect(int line)
    {
        var left = GutterWidth - 14 - FoldMarkerSize - 1;
        return new Rect(left, LineTop(line) + (LineHeight - FoldMarkerSize) / 2, FoldMarkerSize, FoldMarkerSize);
    }

    private FoldRegion? FoldMarkerAt(Point point)
    {
        if (!showsFolding || Folding.Regions.Count == 0)
            return null;

        var right = GutterWidth - 14;
        if (point.X < right - FoldMarginWidth || point.X >= right)
            return null;

        var line = LineAtRow((int)Math.Floor((point.Y + scrollOffset.Y) / LineHeight));
        return RowOf(line) * LineHeight - scrollOffset.Y <= point.Y ? Folding.RegionAt(line) : null;
    }

    private FoldRegion? PlaceholderAt(Point point)
    {
        foreach (var (bounds, region) in placeholderBounds)
        {
            if (bounds.Contains(point))
                return region;
        }

        return null;
    }

    // Handles a click on a fold marker or a folded placeholder; returns whether it did.
    private bool ClickFolding(Point point)
    {
        if (FoldMarkerAt(point) is { } region)
        {
            SetFolded(region, !region.IsCollapsed);
            return true;
        }

        if (PlaceholderAt(point) is { } folded)
        {
            SetFolded(folded, false);
            return true;
        }

        return false;
    }

    private bool IsOverFoldControl(Point point) => FoldMarkerAt(point) is not null || PlaceholderAt(point) is not null;

    private void TrackGutterHover(Point? point)
    {
        var over = point is { } p && p.X < GutterWidth && p.X >= 0;
        if (over == isPointerOverGutter)
            return;

        isPointerOverGutter = over;
        if (showsFolding && Folding.Regions.Count > 0)
            InvalidateVisual();
    }

    private void DrawFoldMarkers(DrawingContext context, EditorBrushes brushes, int first, int last)
    {
        if (!showsFolding || Folding.Regions.Count == 0)
            return;

        for (var number = first; number <= last; number = NextShownLine(number))
        {
            if (Folding.RegionAt(number) is not { } region || !region.IsCollapsed && !isPointerOverGutter)
                continue;

            var rect = FoldMarkerRect(number);
            var icon = region.IsCollapsed ? HammerUI.Icons.ChevronRight : HammerUI.Icons.ChevronDown;
            var scale = rect.Width / 24;
            using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(Math.Round(rect.X), Math.Round(rect.Y))))
                context.DrawGeometry(null, new Pen(region.IsCollapsed ? brushes.CurrentLineNumber : brushes.FoldMarker, 2.4, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), icon);
        }
    }

    private void DrawFoldPlaceholders(DrawingContext context, EditorBrushes brushes, int first, int last, double left)
    {
        placeholderBounds.Clear();
        if (!Folding.HasHiddenLines)
            return;

        for (var number = first; number <= last; number = NextShownLine(number))
        {
            if (Folding.RegionAt(number) is not { IsCollapsed: true } region)
                continue;

            var visual = GetVisualLine(number);
            using var text = Formatting.FormatPlain(region.CollapsedText ?? DefaultPlaceholder, brushes.FoldPlaceholderText);
            var x = left + visual.Width + Formatting.CharacterWidth * 0.6;
            var top = LineTop(number);
            var bounds = new Rect(x, top + 2, text.WidthIncludingTrailingWhitespace + 10, LineHeight - 4);
            context.FillRectangle(brushes.FoldPlaceholder, bounds, 4);
            text.Draw(context, new Point(x + 5, top));
            placeholderBounds.Add((bounds, region));
        }
    }
}
