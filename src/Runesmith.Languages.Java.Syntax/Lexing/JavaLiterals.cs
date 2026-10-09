using System.Globalization;
using System.Text;

namespace Runesmith.Languages.Java.Syntax;

/// <summary>Computes the values of Java literals from their source text, after Unicode escapes are decoded.</summary>
public static class JavaLiterals
{
    /// <summary>Gets the value of a string literal, such as <c>"a\tb"</c>, without its quotes and with its escapes applied.</summary>
    public static string StringValue(ReadOnlySpan<char> literal)
    {
        var body = literal.Length >= 2 && literal[^1] == '"' ? literal[1..^1] : literal[1..];
        return ApplyEscapes(body);
    }

    /// <summary>Gets the value of a character literal, such as <c>'\n'</c>.</summary>
    public static string CharacterValue(ReadOnlySpan<char> literal)
    {
        var body = literal.Length >= 2 && literal[^1] == '\'' ? literal[1..^1] : literal[1..];
        return ApplyEscapes(body);
    }

    /// <summary>Gets the value of a text block: its lines with the incidental indentation and trailing spaces removed, line ends as
    /// <c>\n</c>, and escapes applied last, as the language specifies.</summary>
    public static string TextBlockValue(ReadOnlySpan<char> literal)
    {
        var content = literal[3..];
        var firstLineEnd = content.IndexOfAny('\n', '\r');
        if (firstLineEnd < 0)
            return "";

        content = content[firstLineEnd..];
        content = content[(content.StartsWith("\r\n") ? 2 : 1)..];
        if (content.EndsWith("\"\"\""))
            content = content[..^3];

        var lines = SplitLines(content);

        // The last line holds the closing delimiter; when it is only whitespace, its indentation counts, and the block ends with a line end.
        var lastIsBlank = lines.Count > 0 && lines[^1].Trim().Length == 0;
        var indentation = int.MaxValue;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var isLast = i == lines.Count - 1;
            if (!isLast && line.Trim().Length == 0)
                continue;

            indentation = Math.Min(indentation, LeadingWhitespace(line));
        }

        if (indentation == int.MaxValue)
            indentation = 0;

        var result = new StringBuilder(content.Length);
        for (var i = 0; i < lines.Count; i++)
        {
            var isLast = i == lines.Count - 1;
            if (isLast && lastIsBlank)
                break;

            var line = lines[i];
            var stripped = line.Trim().Length == 0 ? "" : line[Math.Min(indentation, line.Length)..].TrimEnd(" \t\f".ToCharArray());
            result.Append(stripped);
            if (!isLast)
                result.Append('\n');
        }

        return ApplyEscapes(result.ToString());
    }

    /// <summary>Gets the value of an integer literal of type <c>int</c> or <c>long</c>, as an unsigned 64-bit number, or null when it does not
    /// fit; <c>2147483648</c> fits, because it may follow a minus sign.</summary>
    public static ulong? IntegerValue(ReadOnlySpan<char> literal)
    {
        var text = literal.ToString().Replace("_", "", StringComparison.Ordinal).TrimEnd('l', 'L');
        try
        {
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return ulong.Parse(text.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            if (text.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
                return ulong.Parse(text.AsSpan(2), NumberStyles.AllowBinarySpecifier, CultureInfo.InvariantCulture);
            if (text.Length > 1 && text[0] == '0')
                return Convert.ToUInt64(text, 8);
            return ulong.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is OverflowException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    private static List<string> SplitLines(ReadOnlySpan<char> content)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] is not ('\n' or '\r'))
                continue;

            lines.Add(content[start..i].ToString());
            if (content[i] == '\r' && i + 1 < content.Length && content[i + 1] == '\n')
                i++;
            start = i + 1;
        }

        lines.Add(content[start..].ToString());
        return lines;
    }

    private static int LeadingWhitespace(string line)
    {
        var count = 0;
        while (count < line.Length && line[count] is ' ' or '\t' or '\f')
            count++;
        return count;
    }

    private static string ApplyEscapes(ReadOnlySpan<char> text)
    {
        if (!text.Contains('\\'))
            return text.ToString();

        var result = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c != '\\' || i + 1 >= text.Length)
            {
                result.Append(c);
                continue;
            }

            var next = text[++i];
            switch (next)
            {
                case 'b': result.Append('\b'); break;
                case 't': result.Append('\t'); break;
                case 'n': result.Append('\n'); break;
                case 'f': result.Append('\f'); break;
                case 'r': result.Append('\r'); break;
                case 's': result.Append(' '); break;
                case '"' or '\'' or '\\': result.Append(next); break;
                case '\n':
                    break;
                case '\r':
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                        i++;
                    break;
                case >= '0' and <= '7':
                    var value = next - '0';
                    var max = next <= '3' ? 3 : 2;
                    var count = 1;
                    while (count < max && i + 1 < text.Length && text[i + 1] is >= '0' and <= '7')
                    {
                        value = value * 8 + (text[++i] - '0');
                        count++;
                    }

                    result.Append((char)value);
                    break;
                default:
                    result.Append('\\').Append(next);
                    break;
            }
        }

        return result.ToString();
    }
}
