using Avalonia;

namespace Runesmith.Shell.Editors.Images;

/// <summary>How an image sits in the viewer: its scale and where its top-left corner is, kept so the image fills or centers in the
/// viewport and never drifts out of it.</summary>
internal sealed class ImageZoom
{
    public const double MinScale = 0.02;
    public const double MaxScale = 64;

    private static readonly double[] Steps = [0.02, 0.05, 0.1, 0.125, 0.25, 1 / 3.0, 0.5, 2 / 3.0, 0.75, 1, 1.5, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64];

    private Size image;
    private Size viewport;

    /// <summary>Gets the scale: 1 shows one image pixel per device-independent pixel.</summary>
    public double Scale { get; private set; } = 1;

    /// <summary>Gets where the image's top-left corner is in the viewport.</summary>
    public Point Offset { get; private set; }

    /// <summary>Gets whether the scale follows the viewport, fitting the image in it without enlarging it.</summary>
    public bool IsFit { get; private set; } = true;

    public Rect ImageBounds => new(Offset, image * Scale);

    /// <summary>Raised when the scale or the offset changes.</summary>
    public event EventHandler? Changed;

    public void SetImage(Size size)
    {
        image = size;
        Update();
    }

    public void SetViewport(Size size)
    {
        viewport = size;
        Update();
    }

    /// <summary>Fits the image in the viewport, at most at its actual size, and keeps it fitted as the viewport changes.</summary>
    public void Fit()
    {
        IsFit = true;
        Update();
    }

    public void ActualSize() => ZoomTo(1, Center);

    public void ZoomIn(Point? anchor = null) => ZoomTo(Steps.FirstOrDefault(s => s > Scale * 1.0001, MaxScale), anchor ?? Center);

    public void ZoomOut(Point? anchor = null) => ZoomTo(Steps.LastOrDefault(s => s < Scale / 1.0001, MinScale), anchor ?? Center);

    /// <summary>Multiplies the scale, such as by a wheel turn, keeping the image point under <paramref name="anchor"/> where it is.</summary>
    public void ZoomBy(double factor, Point anchor) => ZoomTo(Scale * factor, anchor);

    public void ZoomTo(double scale, Point anchor)
    {
        scale = Math.Clamp(scale, MinScale, MaxScale);
        var point = (anchor - Offset) / Scale;
        IsFit = false;
        Scale = scale;
        Offset = anchor - (point * scale);
        Clamp();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Moves the image by a drag; it stops where its edges reach the viewport's.</summary>
    public void Pan(Vector delta)
    {
        Offset += delta;
        Clamp();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private Point Center => new(viewport.Width / 2, viewport.Height / 2);

    private void Update()
    {
        if (IsFit)
        {
            Scale = image.Width <= 0 || image.Height <= 0 || viewport.Width <= 0 || viewport.Height <= 0
                ? 1
                : Math.Clamp(Math.Min(1, Math.Min(viewport.Width / image.Width, viewport.Height / image.Height)), MinScale, MaxScale);
        }

        Clamp();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Clamp()
    {
        static double Axis(double offset, double extent, double room) =>
            extent <= room ? Math.Round((room - extent) / 2) : Math.Clamp(offset, room - extent, 0);

        var extent = image * Scale;
        Offset = new Point(Axis(Offset.X, extent.Width, viewport.Width), Axis(Offset.Y, extent.Height, viewport.Height));
    }
}
