using System.Collections.Concurrent;
using Avalonia.Media;
using Runesmith.Sdk.Languages;
using TextMateSharp.Themes;
using FontStyle = TextMateSharp.Themes.FontStyle;

namespace Runesmith.Languages.Highlighting;

/// <summary>Turns a theme's color ids and font styles into shared <see cref="SyntaxStyle"/> instances.</summary>
internal sealed class StyleTable(Theme theme, string defaultForeground)
{
    private readonly ConcurrentDictionary<(int Foreground, FontStyle Style), SyntaxStyle?> styles = new();

    /// <summary>Gets the style of a token, or null when it looks like plain text.</summary>
    public SyntaxStyle? Get(int foreground, FontStyle fontStyle) => styles.GetOrAdd((foreground, fontStyle), Create);

    private SyntaxStyle? Create((int Foreground, FontStyle Style) key)
    {
        var style = key.Style == FontStyle.NotSet ? FontStyle.None : key.Style;
        var color = theme.GetColor(key.Foreground);
        var isDefault = color is null || string.Equals(color, defaultForeground, StringComparison.OrdinalIgnoreCase);
        if (isDefault && style == FontStyle.None)
            return null;

        return new SyntaxStyle(
            Color.TryParse(color ?? defaultForeground, out var parsed) ? parsed : Colors.Transparent,
            IsBold: style.HasFlag(FontStyle.Bold),
            IsItalic: style.HasFlag(FontStyle.Italic),
            IsUnderline: style.HasFlag(FontStyle.Underline));
    }
}
