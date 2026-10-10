using Avalonia;
using Avalonia.Controls;

namespace Runesmith.Shell.Views;

/// <summary>Lays out its first child with its second on the right while the first keeps at least <see cref="MinMainWidth"/>, and the
/// second under the first when it would not.</summary>
internal sealed class SideBySidePanel : Panel
{
    private bool stacked;

    /// <summary>Gets or sets the narrowest the first child gets beside the second.</summary>
    public double MinMainWidth { get; set; } = 240;

    /// <summary>Gets or sets the space between the children, across or down.</summary>
    public double Spacing { get; set; } = 12;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count < 2)
            return Children.Count == 1 ? Measured(Children[0], availableSize) : default;

        var (main, side) = (Children[0], Children[1]);
        side.Measure(availableSize);
        var sideWidth = side.DesiredSize.Width;
        stacked = availableSize.Width - sideWidth - Spacing < MinMainWidth;
        if (stacked)
        {
            main.Measure(availableSize);
            return new Size(Math.Max(main.DesiredSize.Width, sideWidth), main.DesiredSize.Height + Spacing + side.DesiredSize.Height);
        }

        main.Measure(availableSize.WithWidth(Math.Max(0, availableSize.Width - sideWidth - Spacing)));
        return new Size(main.DesiredSize.Width + Spacing + sideWidth, Math.Max(main.DesiredSize.Height, side.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count < 2)
        {
            if (Children.Count == 1)
                Children[0].Arrange(new Rect(finalSize));
            return finalSize;
        }

        var (main, side) = (Children[0], Children[1]);
        if (stacked)
        {
            main.Arrange(new Rect(0, 0, finalSize.Width, main.DesiredSize.Height));
            side.Arrange(new Rect(0, main.DesiredSize.Height + Spacing, finalSize.Width, side.DesiredSize.Height));
        }
        else
        {
            var sideWidth = side.DesiredSize.Width;
            main.Arrange(new Rect(0, 0, Math.Max(0, finalSize.Width - sideWidth - Spacing), finalSize.Height));
            side.Arrange(new Rect(finalSize.Width - sideWidth, 0, sideWidth, finalSize.Height));
        }

        return finalSize;
    }

    private static Size Measured(Control child, Size availableSize)
    {
        child.Measure(availableSize);
        return child.DesiredSize;
    }
}
