using Avalonia;
using Avalonia.Controls;
using Runesmith.Shell.Views;

namespace Runesmith.Shell.Tests.Views;

public sealed class SideBySidePanelTests
{
    [Fact]
    public Task ASideControlMovesUnderTheMainOneWhenItWouldSqueezeIt() => HeadlessSession.Value.Dispatch(() =>
    {
        var text = new TextBlock { Text = "Version 0.1.0 is installed from the hub.", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var buttons = new Border { Width = 200, Height = 30 };
        var panel = new SideBySidePanel { MinMainWidth = 200, Spacing = 10, Children = { text, buttons } };

        Layout(panel, 600);
        Assert.Equal(new Rect(400, 0, 200, panel.Bounds.Height), buttons.Bounds);
        Assert.Equal(390, text.Bounds.Width);

        Layout(panel, 300);
        Assert.Equal(300, text.Bounds.Width);
        Assert.Equal(text.Bounds.Bottom + 10, buttons.Bounds.Y);
        return true;
    }, CancellationToken.None);

    private static void Layout(Control panel, double width)
    {
        panel.Measure(new Size(width, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, width, panel.DesiredSize.Height));
    }
}
