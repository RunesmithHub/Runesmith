using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using HammerUI.Controls;

namespace Runesmith.Plugins.Views;

/// <summary>Small builders the version control views share, so they look alike.</summary>
internal static class ViewHelpers
{
    /// <summary>Gets a text block whose color follows a theme brush, such as <c>TextMutedBrush</c>.</summary>
    public static TextBlock Label(string? text, string brush = "TextPrimaryBrush", double fontSize = 13, FontWeight weight = FontWeight.Normal, bool wrap = false)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = weight,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Brush(block, TextBlock.ForegroundProperty, brush);
        return block;
    }

    /// <summary>Binds a property to a theme brush, so it follows the theme.</summary>
    public static T Brush<T>(T control, AvaloniaProperty property, string brush)
        where T : Control
    {
        control.Bind(property, control.GetResourceObservable(brush));
        return control;
    }

    /// <summary>Gets an icon in a theme brush.</summary>
    public static SymbolIcon Icon(Geometry icon, double size = 16, string brush = "TextSecondaryBrush") =>
        Brush(new SymbolIcon { Data = icon, Size = size, VerticalAlignment = VerticalAlignment.Center }, SymbolIcon.ForegroundProperty, brush);

    /// <summary>Gets a button showing an icon and a label.</summary>
    public static Button IconButton(Geometry icon, string text, params string[] classes)
    {
        var button = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { new SymbolIcon { Data = icon, Size = 14, VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } },
            },
        };
        button.Classes.AddRange(classes);
        return button;
    }

    /// <summary>Gets a button that looks like a link.</summary>
    public static Button Link(string text, Action click)
    {
        var label = Label(text, "AccentBrush", 12);
        label.TextDecorations = null;
        var button = new Button { Content = label, Classes = { "subtle", "small" }, Padding = new Thickness(4, 2), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>Gets a numbered step of instructions, with parts of it in bold.</summary>
    public static Grid Step(int number, params (string Text, bool Bold)[] parts)
    {
        var badge = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = number.ToString(System.Globalization.CultureInfo.InvariantCulture), FontSize = 11, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        Brush(badge, Border.BackgroundProperty, "AccentSubtleBrush");
        Brush((TextBlock)badge.Child, TextBlock.ForegroundProperty, "AccentBrush");
        var text = Label(null, "TextSecondaryBrush", wrap: true);
        text.VerticalAlignment = VerticalAlignment.Top;
        text.Margin = new Thickness(0, 1, 0, 0);
        text.Inlines = [];
        foreach (var (part, bold) in parts)
            text.Inlines.Add(new Run(part) { FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal });
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,10,*"), Children = { badge, text } };
        Grid.SetColumn(text, 2);
        return grid;
    }

    /// <summary>Gets a thin line that separates sections.</summary>
    public static Border Divider(Thickness margin) =>
        Brush(new Border { Height = 1, Margin = margin }, Border.BackgroundProperty, "BorderSubtleBrush");

    /// <summary>Gets how long ago a time was, in the short form lists use, such as "3 hours ago".</summary>
    public static string Ago(DateTimeOffset? when, DateTimeOffset now)
    {
        if (when is not { } time)
            return "";

        var span = now - time;
        return span.TotalMinutes < 1 ? "just now"
            : span.TotalHours < 1 ? Plural((int)span.TotalMinutes, "minute")
            : span.TotalDays < 1 ? Plural((int)span.TotalHours, "hour")
            : span.TotalDays < 30 ? Plural((int)span.TotalDays, "day")
            : span.TotalDays < 365 ? Plural((int)(span.TotalDays / 30), "month")
            : Plural((int)(span.TotalDays / 365), "year");

        static string Plural(int count, string unit) => FormattableString.Invariant($"{count} {unit}{(count == 1 ? "" : "s")} ago");
    }

    /// <summary>Gets a count in the short form lists use, such as 1.2k.</summary>
    public static string Count(int count) => count >= 1000
        ? (count / 1000.0).ToString(count >= 10_000 ? "0" : "0.#", System.Globalization.CultureInfo.InvariantCulture) + "k"
        : count.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
