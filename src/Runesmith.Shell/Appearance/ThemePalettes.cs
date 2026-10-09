using Avalonia.Media;
using HammerUI.Theming;
using Runesmith.Sdk.Appearance;

namespace Runesmith.Shell.Appearance;

/// <summary>Turns a contributed <see cref="ColorTheme"/> into a HammerUI palette, and checks that its text stays readable.</summary>
internal static class ThemePalettes
{
    /// <summary>Text below this contrast with its background is too faint to read, and the theme is left out.</summary>
    public const double UnreadableContrast = 3;

    /// <summary>Text below this contrast is hard to read, and the theme is warned about.</summary>
    public const double ReadableContrast = 4.5;

    /// <summary>Fills in the colors a theme leaves out from <paramref name="basePalette"/>, or tells why the theme cannot be used.</summary>
    /// <param name="problems">Gets why the theme is left out, if it is, and what could be hard to read.</param>
    /// <returns>The palette, or null when the theme is left out.</returns>
    public static ThemePalette? Create(ColorTheme theme, ThemePalette basePalette, List<string> problems)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(basePalette);
        var colors = theme.Colors ?? new ThemeColors();
        var invalid = new List<string>();

        Color Pick(string? value, Color fallback, string name)
        {
            if (value is null)
                return fallback;
            if (Color.TryParse(value, out var color))
                return color;
            invalid.Add($"{name} \"{value}\"");
            return fallback;
        }

        var accent = Pick(colors.Accent, basePalette.Accent, nameof(ThemeColors.Accent));
        var palette = new ThemePalette
        {
            Accent = accent,
            AccentForeground = colors.AccentForeground is null && colors.Accent is not null
                ? ForegroundOn(accent)
                : Pick(colors.AccentForeground, basePalette.AccentForeground, nameof(ThemeColors.AccentForeground)),
            Background = Pick(colors.Background, basePalette.Background, nameof(ThemeColors.Background)),
            Surface = Pick(colors.Surface, basePalette.Surface, nameof(ThemeColors.Surface)),
            SurfaceRaised = Pick(colors.SurfaceRaised, basePalette.SurfaceRaised, nameof(ThemeColors.SurfaceRaised)),
            SurfaceSunken = Pick(colors.SurfaceSunken, basePalette.SurfaceSunken, nameof(ThemeColors.SurfaceSunken)),
            SurfaceHover = Pick(colors.SurfaceHover, basePalette.SurfaceHover, nameof(ThemeColors.SurfaceHover)),
            SurfacePressed = Pick(colors.SurfacePressed, basePalette.SurfacePressed, nameof(ThemeColors.SurfacePressed)),
            BorderSubtle = Pick(colors.BorderSubtle, basePalette.BorderSubtle, nameof(ThemeColors.BorderSubtle)),
            BorderStrong = Pick(colors.BorderStrong, basePalette.BorderStrong, nameof(ThemeColors.BorderStrong)),
            TextPrimary = Pick(colors.TextPrimary, basePalette.TextPrimary, nameof(ThemeColors.TextPrimary)),
            TextSecondary = Pick(colors.TextSecondary, basePalette.TextSecondary, nameof(ThemeColors.TextSecondary)),
            TextMuted = Pick(colors.TextMuted, basePalette.TextMuted, nameof(ThemeColors.TextMuted)),
            TextDisabled = Pick(colors.TextDisabled, basePalette.TextDisabled, nameof(ThemeColors.TextDisabled)),
            Success = Pick(colors.Success, basePalette.Success, nameof(ThemeColors.Success)),
            Warning = Pick(colors.Warning, basePalette.Warning, nameof(ThemeColors.Warning)),
            Danger = Pick(colors.Danger, basePalette.Danger, nameof(ThemeColors.Danger)),
            Info = Pick(colors.Info, basePalette.Info, nameof(ThemeColors.Info)),
            Scrim = Pick(colors.Scrim, basePalette.Scrim, nameof(ThemeColors.Scrim)),
        };

        var errors = new List<string>();
        if (invalid.Count > 0)
            errors.Add($"these are not colors: {string.Join(", ", invalid)}");
        if (palette.IsDark != theme.IsDark)
            errors.Add(theme.IsDark ? "it is dark, but its background is light" : "it is light, but its background is dark");

        var unreadable = Surfaces(palette).Where(s => Contrast(palette.TextPrimary, s.Color) < UnreadableContrast).Select(s => s.Name).ToList();
        if (unreadable.Count > 0)
            errors.Add($"its text would be too faint to read on {string.Join(", ", unreadable)}");

        if (errors.Count > 0)
        {
            problems.Add($"The color theme {theme.Name} ({theme.Id}) is left out: {string.Join("; ", errors)}.");
            return null;
        }

        var faint = new List<string>();
        if (Surfaces(palette).Any(s => Contrast(palette.TextPrimary, s.Color) < ReadableContrast))
            faint.Add("its text");
        if (Contrast(palette.TextSecondary, palette.Surface) < UnreadableContrast)
            faint.Add("its secondary text");
        if (Contrast(palette.AccentForeground, palette.Accent) < UnreadableContrast)
            faint.Add("the text on its accent");
        if (faint.Count > 0)
            problems.Add($"The color theme {theme.Name} ({theme.Id}) may be hard to read: {string.Join(", ", faint)} has little contrast with its background.");

        return palette;
    }

    /// <summary>Gets white or near black, whichever reads better on <paramref name="background"/>.</summary>
    public static Color ForegroundOn(Color background) =>
        Contrast(Colors.White, background) >= Contrast(Color.Parse("#14161B"), background) ? Colors.White : Color.Parse("#14161B");

    /// <summary>Gets the contrast ratio, from 1 to 21, of a color drawn over another, after blending away its transparency.</summary>
    public static double Contrast(Color foreground, Color background)
    {
        var opaque = Color.FromRgb(background.R, background.G, background.B);
        var blended = ColorMath.Mix(opaque, Color.FromRgb(foreground.R, foreground.G, foreground.B), foreground.A / 255.0);
        var a = ColorMath.Luminance(blended);
        var b = ColorMath.Luminance(opaque);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static (string Name, Color Color)[] Surfaces(ThemePalette palette) =>
        [("Background", palette.Background), ("Surface", palette.Surface), ("SurfaceRaised", palette.SurfaceRaised), ("SurfaceSunken", palette.SurfaceSunken)];
}
