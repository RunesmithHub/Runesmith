using Avalonia;
using Avalonia.Controls;

namespace Runesmith.Shell.Views;

/// <summary>Lays out the title bar's three children: the first at the left, the third at the right, and the second centered in the window
/// as far as the space between them allows, never over them. The second is as wide as its <see cref="Avalonia.Layout.Layoutable.MaxWidth"/> where there is
/// room, narrower down to <see cref="MinCenterWidth"/>, and hidden below that.</summary>
internal sealed class TitleBarPanel : Panel
{
    private const double Gap = 12;

    /// <summary>Gets or sets the narrowest the centered child gets before it hides.</summary>
    public double MinCenterWidth { get; set; } = 160;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count != 3)
            return default;

        var (left, middle, right) = (Children[0], Children[1], Children[2]);
        left.Measure(availableSize);
        right.Measure(availableSize);
        var width = double.IsInfinity(availableSize.Width) ? left.DesiredSize.Width + right.DesiredSize.Width + 2 * Gap + middle.MaxWidth : availableSize.Width;
        var center = Center(width, left.DesiredSize.Width, right.DesiredSize.Width, middle);
        middle.Measure(new Size(center.Width, availableSize.Height));
        var height = Math.Max(left.DesiredSize.Height, Math.Max(right.DesiredSize.Height, middle.DesiredSize.Height));
        return new Size(double.IsInfinity(availableSize.Width) ? width : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count != 3)
            return finalSize;

        var (left, middle, right) = (Children[0], Children[1], Children[2]);
        var center = Center(finalSize.Width, left.DesiredSize.Width, right.DesiredSize.Width, middle);
        left.Arrange(new Rect(0, 0, left.DesiredSize.Width, finalSize.Height));
        right.Arrange(new Rect(finalSize.Width - right.DesiredSize.Width, 0, right.DesiredSize.Width, finalSize.Height));
        middle.Arrange(new Rect(center.X, 0, center.Width, finalSize.Height));
        return finalSize;
    }

    private Rect Center(double width, double leftWidth, double rightWidth, Control middle)
    {
        var start = leftWidth + Gap;
        var end = width - rightWidth - Gap;
        var preferred = double.IsInfinity(middle.MaxWidth) ? 360 : middle.MaxWidth;
        var size = Math.Min(preferred, end - start);
        if (size < MinCenterWidth)
            return new Rect(start, 0, 0, 0);

        var x = Math.Clamp((width - size) / 2, start, end - size);
        return new Rect(x, 0, size, 0);
    }
}
