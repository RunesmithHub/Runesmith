using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Runesmith.Languages.Java.Syntax;

namespace Runesmith.Languages.Java.Analysis;

/// <summary>Turns documentation comments into Markdown: Javadoc's inline tags and HTML, and the block tags of both kinds of comments.</summary>
internal static partial class JavadocMarkdown
{
    public static string Convert(DocComment comment) => Convert(comment.Content, comment.Kind == CommentKind.MarkdownLine);

    /// <summary>Converts a comment's content, without its delimiters; Markdown comments keep their text and only have their block tags converted.</summary>
    public static string Convert(string content, bool isMarkdown = false)
    {
        var (description, tags) = Split(content, isMarkdown);
        var result = new StringBuilder(Inline(description, isMarkdown));
        var parameters = new List<string>();
        var throws = new List<string>();
        foreach (var (tag, text) in tags)
        {
            switch (tag)
            {
                case "param":
                {
                    var (name, rest) = SplitFirstWord(text);
                    parameters.Add($"- `{name}` {Inline(rest, isMarkdown)}");
                    break;
                }

                case "throws" or "exception":
                {
                    var (name, rest) = SplitFirstWord(text);
                    throws.Add($"- `{name}` {Inline(rest, isMarkdown)}");
                    break;
                }

                case "return":
                    result.Append("\n\n**Returns** ").Append(Inline(text, isMarkdown));
                    break;
                case "deprecated":
                    result.Append("\n\n**Deprecated.** ").Append(Inline(text, isMarkdown));
                    break;
            }
        }

        if (parameters.Count > 0)
            result.Append("\n\n**Parameters**\n\n").AppendJoin('\n', parameters);
        if (throws.Count > 0)
            result.Append("\n\n**Throws**\n\n").AppendJoin('\n', throws);
        return result.ToString().Trim();
    }

    // Splits the main description from the block tags, each with its text up to the next one; a tag inside <pre> or a code fence is text.
    private static (string Description, List<(string Tag, string Text)> Tags) Split(string content, bool isMarkdown)
    {
        var description = new StringBuilder();
        var tags = new List<(string Tag, StringBuilder Text)>();
        var inCode = false;
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.TrimEnd();
            var trimmed = line.TrimStart();
            if (isMarkdown ? trimmed.StartsWith("```", StringComparison.Ordinal) : trimmed.Contains("<pre>", StringComparison.OrdinalIgnoreCase))
                inCode = !isMarkdown || !inCode;
            if (!isMarkdown && trimmed.Contains("</pre>", StringComparison.OrdinalIgnoreCase))
                inCode = false;

            if (!inCode && BlockTag().Match(trimmed) is { Success: true } match)
            {
                tags.Add((match.Groups["tag"].Value, new StringBuilder(match.Groups["rest"].Value)));
                continue;
            }

            if (tags.Count > 0)
                tags[^1].Text.Append('\n').Append(trimmed);
            else
                description.Append(line).Append('\n');
        }

        return (description.ToString().Trim(), [.. tags.Select(t => (t.Tag, t.Text.ToString().Trim()))]);
    }

    private static (string Word, string Remainder) SplitFirstWord(string text)
    {
        var space = text.IndexOfAny([' ', '\n', '\t']);
        return space < 0 ? (text, "") : (text[..space], text[(space + 1)..].Trim());
    }

    /// <summary>Converts Javadoc's inline tags and HTML in a text to Markdown.</summary>
    private static string Inline(string text, bool isMarkdown)
    {
        var withTags = InlineTags(text);
        return isMarkdown ? withTags : Html(withTags);
    }

    private static string InlineTags(string text)
    {
        var result = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] != '{' || i + 1 >= text.Length || text[i + 1] != '@')
            {
                result.Append(text[i++]);
                continue;
            }

            var end = MatchingBrace(text, i);
            if (end < 0)
            {
                result.Append(text, i, text.Length - i);
                break;
            }

            var body = text[(i + 2)..end];
            var (tag, argument) = SplitFirstWord(body);
            result.Append(tag switch
            {
                "code" => "`" + argument + "`",
                "literal" or "index" or "summary" => argument,
                "link" or "linkplain" => Link(argument, tag == "link"),
                "value" => argument.Length > 0 ? "`" + Reference(argument) + "`" : "",
                "inheritDoc" => "",
                _ => argument,
            });
            i = end + 1;
        }

        return result.ToString();
    }

    private static int MatchingBrace(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{')
                depth++;
            else if (text[i] == '}' && --depth == 0)
                return i;
        }

        return -1;
    }

    private static string Link(string argument, bool asCode)
    {
        var (reference, label) = SplitFirstWord(argument);
        if (label.Length > 0)
            return label;
        return asCode ? "`" + Reference(reference) + "`" : Reference(reference);
    }

    // A reference such as java.util.List#add(Object) reads as List.add(Object), and #size as size.
    private static string Reference(string reference)
    {
        var hash = reference.IndexOf('#', StringComparison.Ordinal);
        var type = hash < 0 ? reference : reference[..hash];
        var member = hash < 0 ? "" : reference[(hash + 1)..];
        var paren = type.IndexOf('(', StringComparison.Ordinal);
        var simpleType = type[((paren < 0 ? type : type[..paren]).LastIndexOf('.') + 1)..];
        return member.Length == 0 ? simpleType : simpleType.Length == 0 ? member : simpleType + "." + member;
    }

    private static string Html(string text)
    {
        var replaced = HtmlTag().Replace(text, match => match.Groups["name"].Value.ToLowerInvariant() switch
        {
            "p" when !match.Value.StartsWith("</", StringComparison.Ordinal) => "\n\n",
            "br" => "  \n",
            "code" or "tt" => "`",
            "b" or "strong" => "**",
            "i" or "em" => "*",
            "pre" => "\n```\n",
            "li" when !match.Value.StartsWith("</", StringComparison.Ordinal) => "\n- ",
            "ul" or "ol" => "\n",
            _ => "",
        });
        return WebUtility.HtmlDecode(replaced).Trim();
    }

    [GeneratedRegex(@"^@(?<tag>[A-Za-z]+)(\s+(?<rest>.*))?$", RegexOptions.CultureInvariant)]
    private static partial Regex BlockTag();

    [GeneratedRegex(@"</?(?<name>[A-Za-z][A-Za-z0-9]*)[^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTag();
}
