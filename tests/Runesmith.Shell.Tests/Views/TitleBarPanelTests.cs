using Avalonia;
using Avalonia.Controls;
using Runesmith.Shell.Views;

namespace Runesmith.Shell.Tests.Views;

public sealed class TitleBarPanelTests
{
    [Theory]
    [InlineData(1440, 540, 360)]
    [InlineData(1100, 328, 360)]
    [InlineData(900, 312, 176)]
    public Task TheTitleBarCentersTheSearchBetweenItsNeighboursAndNeverOverThem(double width, double expectedX, double expectedWidth) => HeadlessSession.Value.Dispatch(() =>
    {
        var (left, search, right) = (Block(300), new Border { MaxWidth = 360 }, Block(400));
        var panel = new TitleBarPanel { Children = { left, search, right } };

        Layout(panel, width);

        Assert.Equal(new Rect(expectedX, 0, expectedWidth, 40), search.Bounds);
        Assert.Equal(width - 400, right.Bounds.X);
        return true;
    }, CancellationToken.None);

    [Fact]
    public Task TheTitleBarHidesTheSearchWhenItWouldBeNarrowerThanItsMinimum() => HeadlessSession.Value.Dispatch(() =>
    {
        var search = new Border { MaxWidth = 360 };
        var panel = new TitleBarPanel { Children = { Block(300), search, Block(400) } };

        Layout(panel, 850);

        Assert.Equal(0, search.Bounds.Width);
        return true;
    }, CancellationToken.None);

    private static Border Block(double width) => new() { Width = width, Height = 40 };

    private static void Layout(Control panel, double width)
    {
        panel.Measure(new Size(width, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, width, panel.DesiredSize.Height));
    }
}
