using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using HammerUI;
using HammerUI.Controls;

namespace Runesmith.Shell.Palette;

/// <summary>The parts the command palette and the plugins' quick input share, so both look the same.</summary>
internal static class PaletteChrome
{
    public const double Width = 660;

    /// <summary>Creates the borderless search box of the header.</summary>
    public static TextBox QueryBox(string? text = null)
    {
        var query = new TextBox { Text = text, FontSize = 15, Padding = new Thickness(10, 12), BorderThickness = new Thickness(0), Classes = { "bare" } };
        query.Resources["TextControlBorderBrushFocused"] = Brushes.Transparent;
        query.Resources["TextControlBorderBrushPointerOver"] = Brushes.Transparent;
        query.Resources["TextControlBackgroundFocused"] = Brushes.Transparent;
        query.Resources["TextControlBackgroundPointerOver"] = Brushes.Transparent;
        query.Resources["TextControlBackground"] = Brushes.Transparent;
        query.FocusAdorner = null;
        return query;
    }

    /// <summary>Creates the floating card the palette's parts sit in.</summary>
    public static Border Card(Control child)
    {
        var card = new Border
        {
            Width = Width,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Child = child,
        };
        card[!Border.BackgroundProperty] = new DynamicResourceExtension("Surface2Brush");
        card[!Border.BorderBrushProperty] = new DynamicResourceExtension("OutlineBrush");
        card[!Border.BoxShadowProperty] = new DynamicResourceExtension("ShadowPopup");
        return card;
    }

    public static SymbolIcon MutedIcon(Geometry icon, double size)
    {
        var symbol = new SymbolIcon { Data = icon, Size = size };
        symbol[!SymbolIcon.ForegroundProperty] = new DynamicResourceExtension("TextMutedBrush");
        return symbol;
    }

    public static TextBlock Caption(string text, double rightMargin) =>
        new() { Text = text, Classes = { "caption", "muted" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, rightMargin, 0) };

    public static Border Divider() => new() { Classes = { "divider-h" }, Margin = new Thickness(0) };

    /// <summary>Creates the rounded tile a row's icon sits on.</summary>
    public static Border IconTile(Geometry? icon, IBrush? brush = null, bool isFilled = false)
    {
        var symbol = new SymbolIcon { Data = icon ?? Icons.ChevronRight, Size = 14, IsFilled = isFilled };
        if (brush is not null)
            symbol.Foreground = brush;
        var tile = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(7), Child = symbol };
        tile[!Border.BackgroundProperty] = new DynamicResourceExtension("SurfaceSunkenBrush");
        return tile;
    }

    /// <summary>Creates a text whose matched characters are drawn in the accent color.</summary>
    public static TextBlock Highlighted(string text, IReadOnlyCollection<int> matches)
    {
        var title = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        if (matches.Count == 0)
        {
            title.Text = text;
            return title;
        }

        var matched = matches as ISet<int> ?? matches.ToHashSet();
        for (var i = 0; i < text.Length;)
        {
            var isMatch = matched.Contains(i);
            var end = i;
            while (end < text.Length && matched.Contains(end) == isMatch)
                end++;
            var run = new Run(text[i..end]);
            if (isMatch)
            {
                run.FontWeight = FontWeight.SemiBold;
                run[!TextElement.ForegroundProperty] = new DynamicResourceExtension("AccentBrush");
            }

            title.Inlines!.Add(run);
            i = end;
        }

        return title;
    }
}
