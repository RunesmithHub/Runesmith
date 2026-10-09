using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace Runesmith.Editor.Popups;

/// <summary>Shows the Markdown language servers use for documentation: paragraphs, code blocks, headings, lists and inline code, bold and
/// italics. Links show as their text.</summary>
internal static class MarkdownView
{
    private static readonly FontFamily Mono = new(EditorOptions.BundledFontFamily);

    public static Control Create(string markdown, double fontSize = 12.5)
    {
        var panel = new StackPanel { Spacing = 6 };
        var paragraph = new StringBuilder();
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        void Flush()
        {
            if (paragraph.Length > 0)
                panel.Children.Add(Paragraph(paragraph.ToString().Trim(), fontSize));
            paragraph.Clear();
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                Flush();
                var code = new StringBuilder();
                for (i++; i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal); i++)
                    code.AppendLine(lines[i]);
                panel.Children.Add(CodeBlock(code.ToString().TrimEnd(), fontSize));
            }
            else if (trimmed.Length == 0)
            {
                Flush();
            }
            else if (trimmed is "---" or "***" or "___")
            {
                Flush();
                panel.Children.Add(new Border { Height = 1, Classes = { "separator" }, Background = Resource("BorderSubtleBrush"), Margin = new Thickness(0, 2) });
            }
            else if (trimmed.StartsWith('#'))
            {
                Flush();
                panel.Children.Add(Paragraph(trimmed.TrimStart('#').Trim(), fontSize, FontWeight.SemiBold));
            }
            else if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                Flush();
                panel.Children.Add(Paragraph("•  " + trimmed[2..], fontSize));
            }
            else
            {
                paragraph.Append(paragraph.Length > 0 ? " " : "").Append(trimmed);
            }
        }

        Flush();
        return panel;
    }

    private static SelectableTextBlock Paragraph(string text, double fontSize, FontWeight weight = FontWeight.Normal)
    {
        var block = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, FontSize = fontSize, FontWeight = weight };
        block.Inlines!.AddRange(Inlines(text));
        return block;
    }

    private static Border CodeBlock(string code, double fontSize) => new()
    {
        Background = Resource("SurfaceSunkenBrush"),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(8, 6),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Child = new SelectableTextBlock { Text = code, FontFamily = Mono, FontSize = fontSize, TextWrapping = TextWrapping.Wrap },
    };

    // Splits text into runs at `code`, **bold**, *italic*, _italic_ and [text](link), and drops backslash escapes.
    private static IEnumerable<Inline> Inlines(string text)
    {
        var plain = new StringBuilder();
        var i = 0;
        Run? Plain()
        {
            if (plain.Length == 0)
                return null;
            var run = new Run(plain.ToString());
            plain.Clear();
            return run;
        }

        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length && char.IsPunctuation(text[i + 1]) | char.IsSymbol(text[i + 1]))
            {
                plain.Append(text[i + 1]);
                i += 2;
                continue;
            }

            if (c == '`' && text.IndexOf('`', i + 1) is var end and > 0)
            {
                if (Plain() is { } run)
                    yield return run;
                yield return new Run(text[(i + 1)..end]) { FontFamily = Mono, Background = Resource("SurfaceSunkenBrush") };
                i = end + 1;
                continue;
            }

            if (text.AsSpan(i).StartsWith("**") && text.IndexOf("**", i + 2, StringComparison.Ordinal) is var boldEnd and > 0)
            {
                if (Plain() is { } run)
                    yield return run;
                yield return new Run(text[(i + 2)..boldEnd]) { FontWeight = FontWeight.SemiBold };
                i = boldEnd + 2;
                continue;
            }

            if (c is '*' or '_' && (i == 0 || !char.IsLetterOrDigit(text[i - 1])) && text.IndexOf(c, i + 1) is var italicEnd and > 0 && italicEnd > i + 1)
            {
                if (Plain() is { } run)
                    yield return run;
                yield return new Run(text[(i + 1)..italicEnd]) { FontStyle = FontStyle.Italic };
                i = italicEnd + 1;
                continue;
            }

            if (c == '[' && text.IndexOf("](", i, StringComparison.Ordinal) is var close and > 0 && text.IndexOf(')', close) is var linkEnd and > 0)
            {
                plain.Append(text[(i + 1)..close]);
                i = linkEnd + 1;
                continue;
            }

            plain.Append(c);
            i++;
        }

        if (Plain() is { } last)
            yield return last;
    }

    private static IBrush? Resource(string key) =>
        Application.Current?.TryGetResource(key, Application.Current.ActualThemeVariant, out var value) == true ? value as IBrush : null;
}
