using Avalonia;
using Runesmith.Editor.Rendering;
using Runesmith.Sdk.Documents;
using Runesmith.Text;

namespace Runesmith.Editor.Tests;

public sealed class InlayHintTests
{
    [Fact]
    public void MapsColumnsAroundHintsByTheSideTheyBelongTo()
    {
        var inlays = LineInlays.Create([(4, "a: ", true), (2, ": int", false)]);

        Assert.Equal([2, 9], inlays.Items.Select(i => i.Start));
        Assert.Equal(2, inlays.ToIndex(2));
        Assert.Equal(8, inlays.ToIndex(3));
        Assert.Equal(12, inlays.ToIndex(4));
        Assert.Equal(13, inlays.ToIndex(5));

        Assert.Equal(2, inlays.ToColumn(4));
        Assert.Equal(4, inlays.ToColumn(10));
        Assert.Equal(4, inlays.ToColumn(12));
        Assert.Equal(5, inlays.ToColumn(13));
    }

    [Fact]
    public Task AHintPushesTheTextAfterItAndTheCaretSkipsIt() => HeadlessSession.Value.Dispatch(() =>
    {
        var area = new TextArea(new TestDocument("print(value)")) { Width = 600, Height = 200 };
        area.Measure(new Size(600, 200));
        area.Arrange(new Rect(0, 0, 600, 200));
        var before = area.GetCharacterRect(6).X;
        var source = new object();

        area.SetDecorations(source, [new InlayHint(6, "text: ")]);

        var after = area.GetCharacterRect(6).X;
        Assert.True(after > before + 20, $"The caret moved from {before} to {after}.");
        var insideHint = new Point((before + after) / 2, area.GetCharacterRect(6).Center.Y);
        Assert.Equal(6, area.GetOffsetFromPoint(insideHint));

        area.Options = area.Options with { ShowInlayHints = false };
        Assert.Equal(before, area.GetCharacterRect(6).X, 3);
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task TheGutterHasRoomForIconsAndTheLightBulb() => HeadlessSession.Value.Dispatch(() =>
    {
        var area = new TextArea(new TestDocument("a")) { Options = EditorOptions.Default with { ShowLineNumbers = false } };

        Assert.Equal(14 + TextArea.GlyphMarginWidth, area.GutterWidth);
    }, TestContext.Current.CancellationToken);
}
