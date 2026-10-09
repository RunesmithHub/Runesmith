using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;

namespace Runesmith.Shell.Running;

/// <summary>Draws one console line with its ANSI colors, and its file locations as links that open the file at the line when clicked.</summary>
internal sealed class ConsoleLineView : TextBlock
{
    private static readonly string[] DarkPalette =
    [
        "#6E7681", "#F47067", "#8DDB8C", "#E6C76A", "#6CB6FF", "#DCBDFB", "#56D4DD", "#D1D7E0",
        "#8B949E", "#FF938A", "#B4F1B4", "#F7DB8B", "#96D0FF", "#EECCFF", "#7BE7F0", "#FFFFFF",
    ];

    private static readonly string[] LightPalette =
    [
        "#24292F", "#CF222E", "#116329", "#8A6400", "#0550AE", "#8250DF", "#1B7C83", "#6E7781",
        "#57606A", "#A40E26", "#1A7F37", "#9A6700", "#0969DA", "#A475F9", "#3192AA", "#8C959F",
    ];

    private readonly List<(ConsoleLink Link, string Path)> links = [];
    private readonly Action<string, int, int> open;

    public ConsoleLineView(ConsoleLine line, ConsoleLinkResolver resolver, Action<string, int, int> open)
    {
        this.open = open;
        TextWrapping = TextWrapping.NoWrap;
        foreach (var link in ConsoleLinks.Find(line.Text))
        {
            if (resolver.Resolve(link) is { } path)
                links.Add((link, path));
        }

        Fill(line);
        if (links.Count > 0)
        {
            PointerMoved += (_, e) => Cursor = LinkAt(e) is not null ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
            PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && LinkAt(e) is { } target)
                {
                    e.Handled = true;
                    this.open(target.Path, target.Link.Line, target.Link.Column);
                }
            };
        }
    }

    private void Fill(ConsoleLine line)
    {
        var isDark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
        var palette = isDark ? DarkPalette : LightPalette;
        var defaultBrush = line.Source switch
        {
            ConsoleSource.Error => Resource("DangerBrush"),
            ConsoleSource.System => Resource("TextMutedBrush"),
            ConsoleSource.Input => Resource("AccentBrush"),
            _ => null,
        };
        if (line.Spans.Count == 1 && links.Count == 0 && line.Spans[0].Style == ConsoleStyle.Default)
        {
            Text = line.Text;
            if (defaultBrush is not null)
                Foreground = defaultBrush;
            return;
        }

        var inlines = new InlineCollection();
        var linkBrush = Resource("AccentBrush");
        foreach (var span in line.Spans)
        {
            var position = span.Start;
            var end = span.Start + span.Length;
            while (position < end)
            {
                var link = links.FirstOrDefault(l => l.Link.Start + l.Link.Length > position && l.Link.Start < end);
                var isLink = link.Path is not null && link.Link.Start <= position;
                var stop = link.Path is null ? end : isLink ? Math.Min(end, link.Link.Start + link.Link.Length) : Math.Min(end, link.Link.Start);
                var run = new Run(line.Text[position..stop]);
                Style(run, span.Style, palette, defaultBrush);
                if (isLink)
                {
                    run.TextDecorations = Avalonia.Media.TextDecorations.Underline;
                    if (span.Style.Foreground < 0 && linkBrush is not null)
                        run.Foreground = linkBrush;
                }

                inlines.Add(run);
                position = stop;
            }
        }

        Inlines = inlines;
    }

    private static void Style(Run run, ConsoleStyle style, string[] palette, IBrush? defaultBrush)
    {
        if (style.Foreground >= 0)
            run.Foreground = SolidColorBrush.Parse(palette[style.Foreground]);
        else if (defaultBrush is not null)
            run.Foreground = defaultBrush;
        if (style.Background >= 0)
            run.Background = new SolidColorBrush(Color.Parse(palette[style.Background]), 0.35);
        if (style.Bold)
            run.FontWeight = FontWeight.Bold;
    }

    private static IBrush? Resource(string key) =>
        Application.Current is { } app && app.TryGetResource(key, app.ActualThemeVariant, out var value) ? value as IBrush : null;

    private (ConsoleLink Link, string Path)? LinkAt(PointerEventArgs e)
    {
        var hit = TextLayout.HitTestPoint(e.GetPosition(this) - new Point(Padding.Left, Padding.Top));
        if (!hit.IsInside)
            return null;

        var index = hit.TextPosition;
        foreach (var link in links)
        {
            if (index >= link.Link.Start && index < link.Link.Start + link.Link.Length)
                return link;
        }

        return null;
    }
}
