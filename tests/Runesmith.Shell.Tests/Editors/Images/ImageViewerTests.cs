using Avalonia;
using Runesmith.Sdk.Editors;
using Runesmith.Shell.Editors.Images;

namespace Runesmith.Shell.Tests.Editors.Images;

public sealed class ImageViewerTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("runesmith-images-").FullName;

    public static TheoryData<string, string> Formats => new()
    {
        { nameof(SampleImages.Png), "Png" },
        { nameof(SampleImages.Jpeg), "Jpeg" },
        { nameof(SampleImages.WebP), "WebP" },
        { nameof(SampleImages.Gif), "Gif" },
        { nameof(SampleImages.Bmp), "Bmp" },
        { nameof(SampleImages.Ico), "Ico" },
    };

    public void Dispose() => TestFolders.Delete(folder);

    [Theory]
    [MemberData(nameof(Formats))]
    public void TellsTheFormatFromTheFirstBytes(string sample, string format) => Assert.Equal(format, ImageFormats.Detect(Sample(sample))?.ToString());

    [Fact]
    public void TextIsNotAnImage() => Assert.Null(ImageFormats.Detect("hello, world"u8));

    [Theory]
    [MemberData(nameof(Formats))]
    public Task LoadsEachFormatAtItsSize(string sample, string format) => HeadlessSession.Value.Dispatch(async () =>
    {
        var path = Path.Combine(folder, "picture.bin");
        await File.WriteAllBytesAsync(path, Sample(sample), TestContext.Current.CancellationToken);

        using var editor = await new ImageViewerProvider().CreateEditorAsync(new CustomEditorContext(path, false), TestContext.Current.CancellationToken);

        var viewer = Assert.IsType<ImageViewer>(editor);
        Assert.Equal(format, viewer.Image.Format.ToString());
        Assert.Equal(new PixelSize(3, 2), viewer.Image.Bitmap.PixelSize);
        Assert.False(viewer.IsModified);
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AFileThatIsNotAnImageDoesNotOpen() => HeadlessSession.Value.Dispatch(async () =>
    {
        var path = Path.Combine(folder, "notes.png");
        await File.WriteAllTextAsync(path, "not a picture", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidDataException>(() => new ImageViewerProvider().CreateEditorAsync(new CustomEditorContext(path, false), TestContext.Current.CancellationToken));
    }, TestContext.Current.CancellationToken);

    [Fact]
    public void TheViewerHandlesTheImageFormatsByDefault()
    {
        var definition = new ImageViewerProvider().Definition;

        Assert.Equal(EditorPriority.Default, definition.Priority);
        Assert.Contains("*.png", definition.FilePatterns);
        Assert.Contains("*.webp", definition.FilePatterns);
        Assert.Contains("*.ico", definition.FilePatterns);
    }

    [Fact]
    public void FitsALargeImageAndCentersASmallOneAtItsActualSize()
    {
        var zoom = new ImageZoom();
        zoom.SetViewport(new Size(400, 300));
        zoom.SetImage(new Size(800, 400));

        Assert.True(zoom.IsFit);
        Assert.Equal(0.5, zoom.Scale);
        Assert.Equal(new Rect(0, 50, 400, 200), zoom.ImageBounds);

        zoom.SetImage(new Size(100, 50));
        Assert.Equal(1, zoom.Scale);
        Assert.Equal(new Rect(150, 125, 100, 50), zoom.ImageBounds);
    }

    [Fact]
    public void ZoomingKeepsThePointUnderThePointerInPlace()
    {
        var zoom = new ImageZoom();
        zoom.SetViewport(new Size(400, 300));
        zoom.SetImage(new Size(400, 300));
        var anchor = new Point(100, 60);

        zoom.ZoomTo(2, anchor);

        Assert.False(zoom.IsFit);
        Assert.Equal(2, zoom.Scale);
        Assert.Equal(new Point(-100, -60), zoom.Offset);
        Assert.Equal((anchor - zoom.Offset) / zoom.Scale, new Point(100, 60));
    }

    [Fact]
    public void StepsThroughTheZoomLevelsWithinTheLimits()
    {
        var zoom = new ImageZoom();
        zoom.SetViewport(new Size(100, 100));
        zoom.SetImage(new Size(100, 100));

        zoom.ZoomIn();
        Assert.Equal(1.5, zoom.Scale);
        zoom.ZoomOut();
        zoom.ZoomOut();
        Assert.Equal(0.75, zoom.Scale, 3);

        for (var i = 0; i < 50; i++)
            zoom.ZoomIn();
        Assert.Equal(ImageZoom.MaxScale, zoom.Scale);
        for (var i = 0; i < 50; i++)
            zoom.ZoomOut();
        Assert.Equal(ImageZoom.MinScale, zoom.Scale);
    }

    [Fact]
    public void PanningStopsAtTheImagesEdges()
    {
        var zoom = new ImageZoom();
        zoom.SetViewport(new Size(100, 100));
        zoom.SetImage(new Size(100, 100));
        zoom.ZoomTo(4, new Point(0, 0));

        zoom.Pan(new Vector(-50, -20));
        Assert.Equal(new Point(-50, -20), zoom.Offset);

        zoom.Pan(new Vector(-1000, 1000));
        Assert.Equal(new Point(-300, 0), zoom.Offset);
    }

    [Fact]
    public void FittingAgainFollowsTheViewport()
    {
        var zoom = new ImageZoom();
        zoom.SetViewport(new Size(100, 100));
        zoom.SetImage(new Size(200, 200));
        zoom.ActualSize();
        Assert.Equal(1, zoom.Scale);

        zoom.Fit();
        zoom.SetViewport(new Size(50, 80));

        Assert.Equal(0.25, zoom.Scale);
        Assert.Equal(new Rect(0, 15, 50, 50), zoom.ImageBounds);
    }

    private static byte[] Sample(string name) => (byte[])typeof(SampleImages).GetProperty(name)!.GetValue(null)!;
}
