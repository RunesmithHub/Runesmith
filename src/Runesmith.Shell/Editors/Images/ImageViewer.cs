using System.Composition;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Sdk.Editors;

namespace Runesmith.Shell.Editors.Images;

/// <summary>Runesmith's image viewer, the default editor of PNG, JPEG, WebP, GIF, BMP and ICO files.</summary>
[Export(typeof(IEditorProvider))]
public sealed class ImageViewerProvider : IEditorProvider
{
    public const string Id = "runesmith.imageViewer";

    public EditorProviderDefinition Definition { get; } = new(Id, "Image Viewer", ImageFormats.Patterns) { Icon = "image" };

    public async Task<CustomEditor> CreateEditorAsync(CustomEditorContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var image = await LoadedImage.ReadAsync(context.FilePath, cancellationToken);
        return new ImageViewer(context.FilePath, image);
    }
}

/// <summary>An image read from a file: the decoded bitmap, its format and the file's size. A GIF shows its first frame.</summary>
internal sealed record LoadedImage(Bitmap Bitmap, ImageFormat Format, long Length)
{
    /// <exception cref="InvalidDataException">The file is not an image the viewer reads.</exception>
    public static async Task<LoadedImage> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        return await Task.Run(() => Decode(bytes, Path.GetFileName(path)), cancellationToken);
    }

    /// <exception cref="InvalidDataException">The bytes are not an image the viewer reads.</exception>
    public static LoadedImage Decode(byte[] bytes, string name)
    {
        var format = ImageFormats.Detect(bytes) ?? throw new InvalidDataException($"{name} is not a PNG, JPEG, WebP, GIF, BMP or ICO image.");
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            return new LoadedImage(new Bitmap(stream), format, bytes.Length);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
        {
            throw new InvalidDataException($"{name} looks like a {ImageFormats.NameOf(format)} image but cannot be read: {exception.Message}", exception);
        }
    }
}

/// <summary>Shows an image with zoom and pan, and a bar with the zoom buttons, the format, the size in pixels and the file size.</summary>
internal sealed class ImageViewer : CustomEditor
{
    private readonly string path;
    private readonly ImageCanvas canvas;
    private readonly TextBlock scale = Muted();
    private readonly TextBlock info = Muted();
    private readonly DockPanel content;
    private LoadedImage image;

    public ImageViewer(string path, LoadedImage image)
    {
        this.path = path;
        this.image = image;
        canvas = new ImageCanvas(new ImageZoom());
        canvas.Zoom.Changed += (_, _) => scale.Text = string.Create(CultureInfo.InvariantCulture, $"{canvas.Zoom.Scale * 100:0.#}%");

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                IconButton(Icons.ZoomOut, "Zoom out (-)", () => canvas.Zoom.ZoomOut()),
                IconButton(Icons.ZoomIn, "Zoom in (+)", () => canvas.Zoom.ZoomIn()),
                TextButton("Fit", "Fit in the window (0)", canvas.Zoom.Fit),
                TextButton("1:1", "Actual size (1)", canvas.Zoom.ActualSize),
                scale,
            },
        };
        info.HorizontalAlignment = HorizontalAlignment.Right;
        info.VerticalAlignment = VerticalAlignment.Center;
        var bar = new Border
        {
            Height = 32,
            Padding = new Thickness(8, 0),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new DockPanel { Children = { buttons, info } },
        };
        DockPanel.SetDock(buttons, Dock.Left);
        bar[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSubtleBrush");
        DockPanel.SetDock(bar, Dock.Top);
        content = new DockPanel { Children = { bar, canvas } };
        Show(image);
    }

    public override Control Content => content;

    internal ImageZoom Zoom => canvas.Zoom;

    internal LoadedImage Image => image;

    public override async Task RevertAsync(CancellationToken cancellationToken)
    {
        var loaded = await LoadedImage.ReadAsync(path, cancellationToken);
        var old = image;
        Show(loaded);
        old.Bitmap.Dispose();
    }

    public override void Focus() => canvas.Focus();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            canvas.Image = null;
            image.Bitmap.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Show(LoadedImage loaded)
    {
        image = loaded;
        canvas.Image = loaded.Bitmap;
        var size = loaded.Bitmap.PixelSize;
        info.Text = $"{ImageFormats.NameOf(loaded.Format)}  {size.Width} × {size.Height}  {ImageFormats.SizeText(loaded.Length)}";
        canvas.Zoom.SetImage(loaded.Bitmap.Size);
        canvas.InvalidateVisual();
    }

    private static TextBlock Muted()
    {
        var text = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        text[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMutedBrush");
        return text;
    }

    private static Button IconButton(Geometry icon, string tip, Action action)
    {
        var button = new Button { Classes = { "icon", "small" }, Focusable = false, Content = new SymbolIcon { Data = icon, Size = 14 } };
        button.Click += (_, _) => action();
        ToolTip.SetTip(button, tip);
        return button;
    }

    private static Button TextButton(string text, string tip, Action action)
    {
        var button = new Button { Classes = { "small" }, Focusable = false, Content = text };
        button.Click += (_, _) => action();
        ToolTip.SetTip(button, tip);
        return button;
    }
}
