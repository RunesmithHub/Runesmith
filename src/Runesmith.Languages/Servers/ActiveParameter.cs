using Runesmith.Text;

namespace Runesmith.Languages.Servers;

/// <summary>Works out which argument of a call the caret is in, for servers that do not say.</summary>
internal static class ActiveParameter
{
    private const int MaxDistance = 8192;

    /// <summary>Counts the commas between the call's opening parenthesis and the offset that are not inside nested brackets, strings or
    /// character literals; returns 0 when no call encloses the offset.</summary>
    public static int Find(TextSnapshot snapshot, int offset)
    {
        var start = Math.Max(0, offset - MaxDistance);
        var text = snapshot.GetText(TextSpan.FromBounds(start, Math.Clamp(offset, start, snapshot.Length)));
        var open = FindOpeningParenthesis(text);
        return open < 0 ? 0 : CountArguments(text.AsSpan(open + 1));
    }

    private static int FindOpeningParenthesis(string text)
    {
        var depth = 0;
        for (var i = text.Length - 1; i >= 0; i--)
        {
            switch (text[i])
            {
                case ')' or ']' or '}':
                    depth++;
                    break;
                case '(' when depth == 0:
                    return i;
                case '(' or '[' or '{':
                    if (depth == 0)
                        return -1;
                    depth--;
                    break;
                case ';' when depth == 0:
                    return -1;
            }
        }

        return -1;
    }

    private static int CountArguments(ReadOnlySpan<char> text)
    {
        var commas = 0;
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '"' or '\'')
            {
                var verbatim = c == '"' && i > 0 && text[i - 1] == '@';
                for (i++; i < text.Length; i++)
                {
                    if (verbatim && text[i] == '"' && i + 1 < text.Length && text[i + 1] == '"')
                        i++;
                    else if (!verbatim && text[i] == '\\')
                        i++;
                    else if (text[i] == c || text[i] == '\n')
                        break;
                }
            }
            else if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (c == ',' && depth == 0)
            {
                commas++;
            }
        }

        return commas;
    }
}
