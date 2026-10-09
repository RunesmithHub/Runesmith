using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Runesmith.Editor.Rendering;
using Runesmith.Sdk.Documents;

namespace Runesmith.Editor.Decorations;

/// <summary>Marks beside the vertical scroll bar for the lines of decorations that ask for one, placed where the lines are in the whole text.</summary>
internal sealed class OverviewRuler : Control
{
    private const double MarkHeight = 3;

    private readonly TextArea area;
    private DecorationPaint? paint;

    public OverviewRuler(TextArea area)
    {
        this.area = area;
        IsHitTestVisible = false;
        Width = 6;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        area.DecorationsChanged += (_, _) => InvalidateVisual();
        ActualThemeVariantChanged += (_, _) => paint = null;
        ResourcesChanged += (_, _) => paint = null;
    }

    public override void Render(DrawingContext context)
    {
        var decorations = area.Decorations;
        if (decorations.Count == 0 || Bounds.Height <= 0)
            return;

        paint ??= DecorationPaint.From(area, area.Formatting);
        var snapshot = area.Snapshot;
        var rows = Math.Max(1, area.RowCount);
        var usable = Bounds.Height - MarkHeight;
        foreach (var placed in decorations.All<Decoration>())
        {
            var tone = placed.Decoration switch
            {
                GutterMarker { ShowsInOverviewRuler: true } marker => marker.Tone,
                TextHighlight { ShowsInOverviewRuler: true } highlight => highlight.Tone,
                _ => (DecorationTone?)null,
            };
            if (tone is not { } shown)
                continue;

            var line = snapshot.GetLineFromPosition(Math.Min(placed.Span.Start, snapshot.Length)).LineNumber;
            var y = Math.Round(area.RowOf(line) / (double)rows * usable);
            context.FillRectangle(paint.Tone(shown), new Rect(0, y, Bounds.Width, MarkHeight));
        }
    }
}
