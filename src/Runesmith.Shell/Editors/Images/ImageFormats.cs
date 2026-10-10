namespace Runesmith.Shell.Editors.Images;

/// <summary>The image formats the image viewer reads.</summary>
internal enum ImageFormat
{
    Png,
    Jpeg,
    WebP,
    Gif,
    Bmp,
    Ico,
}

/// <summary>Tells an image's format from its first bytes, whatever its file name says.</summary>
internal static class ImageFormats
{
    /// <summary>The file patterns of the formats.</summary>
    public static IReadOnlyList<string> Patterns { get; } = ["*.png", "*.jpg", "*.jpeg", "*.jfif", "*.webp", "*.gif", "*.bmp", "*.dib", "*.ico"];

    public static ImageFormat? Detect(ReadOnlySpan<byte> bytes) => bytes switch
    {
        [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, ..] => ImageFormat.Png,
        [0xFF, 0xD8, 0xFF, ..] => ImageFormat.Jpeg,
        [(byte)'R', (byte)'I', (byte)'F', (byte)'F', _, _, _, _, (byte)'W', (byte)'E', (byte)'B', (byte)'P', ..] => ImageFormat.WebP,
        [(byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'7' or (byte)'9', (byte)'a', ..] => ImageFormat.Gif,
        [(byte)'B', (byte)'M', ..] when bytes.Length >= 26 => ImageFormat.Bmp,
        [0x00, 0x00, 0x01, 0x00, var count, 0x00, ..] when count > 0 && bytes.Length >= 22 => ImageFormat.Ico,
        _ => null,
    };

    public static string NameOf(ImageFormat format) => format switch
    {
        ImageFormat.Png => "PNG",
        ImageFormat.Jpeg => "JPEG",
        ImageFormat.WebP => "WebP",
        ImageFormat.Gif => "GIF",
        ImageFormat.Bmp => "BMP",
        _ => "ICO",
    };

    /// <summary>Gets a file size for people, such as <c>12.3 KB</c>.</summary>
    public static string SizeText(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} bytes",
        < 1024 * 1024 => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.#} KB"),
        _ => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024):0.#} MB"),
    };
}
