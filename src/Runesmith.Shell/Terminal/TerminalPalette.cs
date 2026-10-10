using System.Globalization;
using Avalonia.Media;

namespace Runesmith.Shell.Terminal;

/// <summary>The colors a terminal draws with: the default text and background, the 16 ANSI colors from the color theme, and the rest of the
/// 256 indexed colors.</summary>
internal sealed class TerminalPalette
{
    private readonly Color[] indexed = new Color[256];

    /// <summary>Creates a palette from a theme's colors; <paramref name="ansi"/> gives the 16 ANSI colors, or null to derive them.</summary>
    public TerminalPalette(ThemeInput theme, IReadOnlyList<Color>? ansi)
    {
        Foreground = theme.Foreground;
        Background = theme.Background;
        Cursor = theme.Foreground;
        Selection = Color.FromArgb(0x55, theme.Accent.R, theme.Accent.G, theme.Accent.B);
        Link = theme.Accent;
        var basic = ansi is { Count: 16 } ? ansi : Derive(theme);
        for (var i = 0; i < 16; i++)
            indexed[i] = basic[i];

        int[] levels = [0, 95, 135, 175, 215, 255];
        for (var i = 0; i < 216; i++)
            indexed[16 + i] = Color.FromRgb((byte)levels[i / 36], (byte)levels[i / 6 % 6], (byte)levels[i % 6]);
        for (var i = 0; i < 24; i++)
        {
            var gray = (byte)(8 + (i * 10));
            indexed[232 + i] = Color.FromRgb(gray, gray, gray);
        }
    }

    public Color Foreground { get; }

    public Color Background { get; }

    public Color Cursor { get; }

    public Color Selection { get; }

    public Color Link { get; }

    /// <summary>Gets an indexed color.</summary>
    public Color this[int index] => indexed[Math.Clamp(index, 0, 255)];

    /// <summary>Gets the color of a cell's text or background, the default one when <paramref name="color"/> is the default.</summary>
    public Color Resolve(TerminalColor color, bool background) =>
        color.IsDefault ? (background ? Background : Foreground)
        : color.IsIndexed ? indexed[color.Index]
        : Color.FromRgb(color.R, color.G, color.B);

    /// <summary>Writes a color the way xterm answers color queries, such as <c>rgb:1e1e/1f1f/2424</c>.</summary>
    public static string ToQueryAnswer(Color color) =>
        string.Create(CultureInfo.InvariantCulture, $"rgb:{color.R:x2}{color.R:x2}/{color.G:x2}{color.G:x2}/{color.B:x2}{color.B:x2}");

    /// <summary>Derives the 16 ANSI colors from the theme's status colors, so programs' colors match the rest of the window.</summary>
    public static Color[] Derive(ThemeInput theme)
    {
        var black = Color.FromRgb(0, 0, 0);
        var white = Color.FromRgb(255, 255, 255);
        var magenta = Mix(theme.Danger, theme.Info, 0.5);
        var cyan = Mix(theme.Info, theme.Success, 0.45);
        Color[] normal = theme.IsDark
            ? [Mix(theme.Background, black, 0.35), theme.Danger, theme.Success, theme.Warning, theme.Info, magenta, cyan, theme.Secondary]
            : [theme.Foreground, theme.Danger, theme.Success, theme.Warning, theme.Info, magenta, cyan, Mix(theme.Background, black, 0.18)];
        var toward = theme.IsDark ? white : black;
        var amount = theme.IsDark ? 0.25 : 0.15;
        Color[] bright =
        [
            theme.Muted,
            .. normal[1..7].Select(c => Mix(c, toward, amount)),
            theme.IsDark ? Mix(theme.Foreground, white, 0.4) : Mix(theme.Background, black, 0.06),
        ];
        return [.. normal, .. bright];
    }

    private static Color Mix(Color from, Color to, double amount) => Color.FromRgb(
        (byte)Math.Round(from.R + ((to.R - from.R) * amount)),
        (byte)Math.Round(from.G + ((to.G - from.G) * amount)),
        (byte)Math.Round(from.B + ((to.B - from.B) * amount)));

    /// <summary>The theme's colors a palette is made from.</summary>
    internal sealed record ThemeInput(bool IsDark, Color Background, Color Foreground, Color Secondary, Color Muted, Color Accent, Color Success, Color Warning,
        Color Danger, Color Info);
}
