using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Runesmith.Shell.Editors.Images;

/// <summary>Draws an image on a checkerboard at the zoom's scale, zooms with the wheel at the pointer and pans by dragging.</summary>
internal sealed class ImageCanvas : Control
{
    private const double WheelStep = 1.2;
    private const int CheckSize = 8;

    private static readonly IBrush Checkerboard = CreateCheckerboard();

    private Point? dragFrom;

    public ImageCanvas(ImageZoom zoom)
    {
        Zoom = zoom;
        ClipToBounds = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        zoom.Changed += (_, _) => InvalidateVisual();
    }

    public ImageZoom Zoom { get; }

    public Bitmap? Image { get; set; }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (Image is not { } image)
            return;

        var bounds = Zoom.ImageBounds;
        context.FillRectangle(Checkerboard, bounds);
        var mode = Zoom.Scale >= 2 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality;
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = mode }))
            context.DrawImage(image, new Rect(image.Size), bounds);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Zoom.SetViewport(e.NewSize);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Delta.Y == 0)
            return;

        Zoom.ZoomBy(Math.Pow(WheelStep, e.Delta.Y), e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (e.ClickCount == 2)
        {
            if (Zoom.IsFit)
                Zoom.ZoomTo(1, e.GetPosition(this));
            else
                Zoom.Fit();
        }
        else
        {
            dragFrom = e.GetPosition(this);
            e.Pointer.Capture(this);
        }

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (dragFrom is not { } from)
            return;

        var to = e.GetPosition(this);
        Zoom.Pan(to - from);
        dragFrom = to;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        dragFrom = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        dragFrom = null;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var modifiers = e.KeyModifiers & ~KeyModifiers.Shift;
        if (modifiers is not (KeyModifiers.None or KeyModifiers.Control))
            return;

        switch (e.Key)
        {
            case Key.OemPlus or Key.Add:
                Zoom.ZoomIn();
                break;
            case Key.OemMinus or Key.Subtract:
                Zoom.ZoomOut();
                break;
            case Key.D0 or Key.NumPad0:
                Zoom.Fit();
                break;
            case Key.D1 or Key.NumPad1:
                Zoom.ActualSize();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private static DrawingBrush CreateCheckerboard()
    {
        var dark = new SolidColorBrush(Color.FromRgb(0xD6, 0xD6, 0xD6));
        var group = new DrawingGroup
        {
            Children =
            {
                new GeometryDrawing { Brush = Brushes.White, Geometry = new RectangleGeometry(new Rect(0, 0, CheckSize * 2, CheckSize * 2)) },
                new GeometryDrawing { Brush = dark, Geometry = new RectangleGeometry(new Rect(0, 0, CheckSize, CheckSize)) },
                new GeometryDrawing { Brush = dark, Geometry = new RectangleGeometry(new Rect(CheckSize, CheckSize, CheckSize, CheckSize)) },
            },
        };
        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            DestinationRect = new RelativeRect(0, 0, CheckSize * 2, CheckSize * 2, RelativeUnit.Absolute),
        };
    }
}
