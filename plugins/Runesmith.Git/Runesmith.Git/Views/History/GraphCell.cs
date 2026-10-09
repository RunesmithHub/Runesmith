using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Runesmith.Git.Views.History;

/// <summary>Draws one row of the branch graph: the lines through the row and the commit's dot.</summary>
internal sealed class GraphCell : Control
{
    /// <summary>The width of a lane.</summary>
    public const double LaneWidth = 14;

    private const double DotRadius = 4;

    private static readonly Color[] Palette =
    [
        Color.Parse("#3B82F6"), Color.Parse("#10B981"), Color.Parse("#F59E0B"), Color.Parse("#A855F7"),
        Color.Parse("#EC4899"), Color.Parse("#06B6D4"), Color.Parse("#84CC16"), Color.Parse("#F97316"),
    ];

    private static readonly IPen[] Pens = [.. Palette.Select(c => (IPen)new ImmutablePen(c.ToUInt32(), 1.6, lineCap: PenLineCap.Round))];
    private static readonly IBrush[] Brushes = [.. Palette.Select(c => (IBrush)new ImmutableSolidColorBrush(c))];

    private GraphRow? row;
    private bool isHead;
    private bool isMerge;

    /// <summary>Gets the color of a lane color index, for the labels of the branches in that lane.</summary>
    public static Color ColorOf(int color) => Palette[color % Palette.Length];

    /// <summary>Shows a row of the graph.</summary>
    public void Show(GraphRow? graph, bool head, bool merge)
    {
        row = graph;
        isHead = head;
        isMerge = merge;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (row is null)
            return;

        var height = Bounds.Height;
        var middle = height / 2;
        // A merge's dot is a ring, so the lines that meet it stop at its edge.
        var gap = isMerge && !isHead ? DotRadius : 0;
        foreach (var edge in row.Top)
            Line(context, edge, 0, middle - (edge.To == row.Lane ? gap : 0));
        foreach (var edge in row.Bottom)
            Line(context, edge, middle + (edge.From == row.Lane ? gap : 0), height);

        var center = new Point(X(row.Lane), middle);
        var brush = Brushes[row.Color % Brushes.Length];
        if (isHead)
        {
            context.DrawEllipse(null, Pens[row.Color % Pens.Length], center, DotRadius + 2.5, DotRadius + 2.5);
            context.DrawEllipse(brush, null, center, DotRadius, DotRadius);
        }
        else if (isMerge)
        {
            context.DrawEllipse(null, new ImmutablePen(Palette[row.Color % Palette.Length].ToUInt32(), 2), center, DotRadius - 0.5, DotRadius - 0.5);
        }
        else
        {
            context.DrawEllipse(brush, null, center, DotRadius, DotRadius);
        }
    }

    private static double X(int lane) => LaneWidth / 2 + lane * LaneWidth + 2;

    private static void Line(DrawingContext context, GraphEdge edge, double top, double bottom)
    {
        var pen = Pens[edge.Color % Pens.Length];
        var from = new Point(X(edge.From), top);
        var to = new Point(X(edge.To), bottom);
        if (edge.From == edge.To)
        {
            context.DrawLine(pen, from, to);
            return;
        }

        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            var bend = (top + bottom) / 2;
            path.BeginFigure(from, isFilled: false);
            path.CubicBezierTo(new Point(from.X, bend), new Point(to.X, bend), to);
            path.EndFigure(isClosed: false);
        }

        context.DrawGeometry(null, pen, geometry);
    }
}
