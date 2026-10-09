using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Runesmith.Shell.Views;

/// <summary>Runesmith's logo at a given size.</summary>
public sealed class Logo : Image
{
    private static readonly Lazy<Bitmap> Mark = new(() => new Bitmap(AssetLoader.Open(new Uri("avares://Runesmith.Shell/Assets/runesmith.png"))));

    public Logo()
        : this(22)
    {
    }

    public Logo(double size)
    {
        Width = size;
        Height = size;
        Source = Mark.Value;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
    }
}
