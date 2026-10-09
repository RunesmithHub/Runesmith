using System.Globalization;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;
using HammerUI.Theming;
using Runesmith.Sdk.Documents;

namespace Runesmith.Editor.Rendering;

/// <summary>The brushes, pens and fonts decorations, the light bulb and code lenses are drawn with, taken from the theme.</summary>
internal sealed class DecorationPaint
{
    private readonly IBrush[] tones;
    private readonly IBrush[] backgrounds;
    private readonly Dictionary<(DecorationTone, UnderlineStyle), IPen> pens = [];

    private DecorationPaint(IBrush[] tones, IBrush[] backgrounds, IBrush muted, IBrush hintBackground, IBrush lightBulb)
    {
        this.tones = tones;
        this.backgrounds = backgrounds;
        Muted = muted;
        HintBackground = hintBackground;
        LightBulb = lightBulb;
    }

    /// <summary>Gets the color of inlay hints and code lenses.</summary>
    public IBrush Muted { get; }

    /// <summary>Gets the faint box behind an inlay hint.</summary>
    public IBrush HintBackground { get; }

    public IBrush LightBulb { get; }

    /// <summary>Gets the text properties of inlay hints and code lenses: the editor's font, a little smaller, in <see cref="Muted"/>.</summary>
    public TextRunProperties? HintText { get; private set; }

    public IBrush Tone(DecorationTone tone) => tones[(int)tone];

    public IBrush Background(DecorationTone tone) => backgrounds[(int)tone];

    public IPen Underline(DecorationTone tone, UnderlineStyle style)
    {
        if (pens.TryGetValue((tone, style), out var pen))
            return pen;

        var dashes = style switch
        {
            UnderlineStyle.Dotted => new DashStyle(new AvaloniaList<double> { 1, 2 }, 0),
            UnderlineStyle.Dashed => new DashStyle(new AvaloniaList<double> { 4, 3 }, 0),
            _ => null,
        };
        pen = new ImmutablePen((IImmutableBrush)Tone(tone), style == UnderlineStyle.Wavy ? 1.2 : 1, dashes?.ToImmutable());
        pens[(tone, style)] = pen;
        return pen;
    }

    public static DecorationPaint From(Control control, TextFormatting formatting)
    {
        Color Get(string key, Color fallback) =>
            control.TryFindResource(key, control.ActualThemeVariant, out var value) && value is Color color ? color : fallback;

        var colors = new[]
        {
            Get(ThemeKeys.TextMutedColor, Colors.Gray),
            Get(ThemeKeys.AccentColor, Color.Parse("#7073F6")),
            Get(ThemeKeys.InfoColor, Colors.DodgerBlue),
            Get(ThemeKeys.SuccessColor, Colors.SeaGreen),
            Get(ThemeKeys.WarningColor, Colors.Orange),
            Get(ThemeKeys.DangerColor, Colors.Red),
        };
        var muted = colors[0];
        var paint = new DecorationPaint(
            [.. colors.Select(c => (IBrush)new ImmutableSolidColorBrush(c))],
            [.. colors.Select(c => (IBrush)new ImmutableSolidColorBrush(c, 0.2))],
            new ImmutableSolidColorBrush(muted),
            new ImmutableSolidColorBrush(muted, 0.14),
            new ImmutableSolidColorBrush(colors[4]));
        paint.HintText = new GenericTextRunProperties(new Typeface(formatting.FontFamily), Math.Max(6, formatting.FontSize - 1.5), null, paint.Muted, null,
            BaselineAlignment.Baseline, CultureInfo.InvariantCulture);
        return paint;
    }
}
