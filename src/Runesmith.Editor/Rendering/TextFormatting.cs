using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;
using Runesmith.Sdk.Languages;

namespace Runesmith.Editor.Rendering;

/// <summary>The font of an editor and the text run properties of each syntax style, created once per style.</summary>
internal sealed class TextFormatting
{
    private readonly Dictionary<SyntaxStyle, TextRunProperties> styles = [];
    private readonly FontFeatureCollection? features;

    public TextFormatting(EditorOptions options, IBrush defaultForeground)
    {
        var family = string.Equals(options.FontFamily, "JetBrains Mono", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(options.FontFamily)
            ? EditorOptions.BundledFontFamily
            : $"{options.FontFamily}, {EditorOptions.BundledFontFamily}";
        FontFamily = new FontFamily(family);
        FontSize = options.FontSize;
        features = options.Ligatures ? null : [new FontFeature { Tag = "calt", Value = 0 }, new FontFeature { Tag = "liga", Value = 0 }];
        Default = Create(new Typeface(FontFamily), defaultForeground, null);
        LineHeight = Math.Ceiling(FontSize * 1.5);

        var probe = TextFormatter.Current.FormatLine(new SingleRunSource("MMMMMMMMMM", Default), 0, double.PositiveInfinity, Paragraph(options.TabSize));
        CharacterWidth = probe is null ? FontSize * 0.6 : probe.WidthIncludingTrailingWhitespace / 10;
        probe?.Dispose();
        TabSize = options.TabSize;
    }

    public FontFamily FontFamily { get; }

    public double FontSize { get; }

    public double LineHeight { get; }

    public double CharacterWidth { get; }

    public int TabSize { get; }

    public TextRunProperties Default { get; }

    public TextParagraphProperties Paragraph(int tabSize) => new EditorParagraphProperties(Default, LineHeight, tabSize * CharacterWidth);

    public TextRunProperties Get(SyntaxStyle style)
    {
        if (styles.TryGetValue(style, out var properties))
            return properties;

        var typeface = new Typeface(FontFamily, style.IsItalic ? FontStyle.Italic : FontStyle.Normal, style.IsBold ? FontWeight.Bold : FontWeight.Normal);
        properties = Create(typeface, new ImmutableSolidColorBrush(style.Foreground), Decorations(style));
        styles[style] = properties;
        return properties;
    }

    private static TextDecorationCollection? Decorations(SyntaxStyle style) => (style.IsUnderline, style.IsStrikethrough) switch
    {
        (true, true) => new TextDecorationCollection(TextDecorations.Underline.Concat(TextDecorations.Strikethrough)),
        (true, false) => TextDecorations.Underline,
        (false, true) => TextDecorations.Strikethrough,
        _ => null,
    };

    /// <summary>Formats a short piece of text in one color, such as a line number.</summary>
    public TextLine FormatPlain(string text, IBrush foreground) =>
        TextFormatter.Current.FormatLine(new SingleRunSource(text, Create(new Typeface(FontFamily), foreground, null)), 0, double.PositiveInfinity, Paragraph(TabSize))
        ?? throw new InvalidOperationException("The text formatter returned no line.");

    private GenericTextRunProperties Create(Typeface typeface, IBrush foreground, TextDecorationCollection? decorations) =>
        new(typeface, FontSize, decorations, foreground, null, BaselineAlignment.Baseline, CultureInfo.InvariantCulture, features);

    private sealed class SingleRunSource(string text, TextRunProperties properties) : ITextSource
    {
        public TextRun? GetTextRun(int textSourceIndex) =>
            textSourceIndex < text.Length ? new TextCharacters(text.AsMemory(textSourceIndex), properties) : new TextEndOfParagraph();
    }
}

/// <summary>Paragraph properties for one editor line: no wrapping, a fixed line height and tab stops every tab size.</summary>
internal sealed class EditorParagraphProperties(TextRunProperties defaultProperties, double lineHeight, double tabWidth) : TextParagraphProperties
{
    public override FlowDirection FlowDirection => FlowDirection.LeftToRight;

    public override TextAlignment TextAlignment => TextAlignment.Left;

    public override double LineHeight => lineHeight;

    public override bool FirstLineInParagraph => true;

    public override TextRunProperties DefaultTextRunProperties => defaultProperties;

    public override TextWrapping TextWrapping => TextWrapping.NoWrap;

    public override double Indent => 0;

    public override double DefaultIncrementalTab => tabWidth;
}
