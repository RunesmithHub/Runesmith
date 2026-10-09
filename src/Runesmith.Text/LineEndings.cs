using System.Text;

namespace Runesmith.Text;

/// <summary>The characters that end a line in a file.</summary>
public enum LineEnding
{
    /// <summary>A line feed, \n, as on Linux and macOS.</summary>
    Lf,

    /// <summary>A carriage return and a line feed, \r\n, as on Windows.</summary>
    CrLf,

    /// <summary>A carriage return alone, \r, as on classic Mac OS.</summary>
    Cr,
}

/// <summary>Detects and converts line endings. Snapshots always use \n; files keep the ending they were read with.</summary>
public static class LineEndings
{
    private const int DetectionLimit = 4096;

    /// <summary>Finds the line ending most of the first few thousand line breaks use, or <paramref name="fallback"/> when there are none.</summary>
    public static LineEnding Detect(ReadOnlySpan<char> text, LineEnding fallback = LineEnding.Lf)
    {
        int lf = 0, crlf = 0, cr = 0;
        for (var i = 0; i < text.Length && lf + crlf + cr < DetectionLimit; i++)
        {
            if (text[i] == '\n')
            {
                lf++;
            }
            else if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else
                {
                    cr++;
                }
            }
        }

        if (lf + crlf + cr == 0)
            return fallback;
        var most = Math.Max(lf, Math.Max(crlf, cr));
        if (Count(fallback) == most)
            return fallback;
        return lf == most ? LineEnding.Lf : crlf == most ? LineEnding.CrLf : LineEnding.Cr;

        int Count(LineEnding ending) => ending switch
        {
            LineEnding.Lf => lf,
            LineEnding.CrLf => crlf,
            _ => cr,
        };
    }

    /// <summary>Turns every \r\n and every lone \r into \n.</summary>
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var first = text.IndexOf('\r');
        if (first < 0)
            return text;

        var builder = new StringBuilder(text.Length);
        builder.Append(text, 0, first);
        for (var i = first; i < text.Length; i++)
        {
            var c = text[i];
            if (c != '\r')
            {
                builder.Append(c);
                continue;
            }

            builder.Append('\n');
            if (i + 1 < text.Length && text[i + 1] == '\n')
                i++;
        }

        return builder.ToString();
    }

    /// <summary>Writes the \n line breaks of normalized <paramref name="text"/> as <paramref name="ending"/>.</summary>
    public static string Apply(string text, LineEnding ending)
    {
        ArgumentNullException.ThrowIfNull(text);
        return ending == LineEnding.Lf ? text : text.Replace("\n", ToText(ending), StringComparison.Ordinal);
    }

    /// <summary>Gets the characters of <paramref name="ending"/>.</summary>
    public static string ToText(LineEnding ending) => ending switch
    {
        LineEnding.Lf => "\n",
        LineEnding.CrLf => "\r\n",
        LineEnding.Cr => "\r",
        _ => throw new ArgumentOutOfRangeException(nameof(ending)),
    };
}
