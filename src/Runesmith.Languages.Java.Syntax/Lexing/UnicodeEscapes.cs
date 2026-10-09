using System.Text;

namespace Runesmith.Languages.Java.Syntax;

/// <summary>Turns Java's Unicode escapes (<c>\uXXXX</c>, with any number of <c>u</c>s) into the characters they stand for, before anything
/// else reads the text, as the language requires.</summary>
internal static class UnicodeEscapes
{
    /// <summary>Decodes the escapes; returns the text itself when it has none. <paramref name="map"/> then gives, for each decoded character
    /// and one past the end, its offset in the original text.</summary>
    public static string Decode(string text, List<SyntaxDiagnostic> diagnostics, out int[]? map)
    {
        map = null;
        if (!text.Contains("\\u", StringComparison.Ordinal))
            return text;

        var decoded = new StringBuilder(text.Length);
        var offsets = new List<int>(text.Length + 1);
        var rawBackslashes = 0;
        var i = 0;
        while (i < text.Length)
        {
            var character = text[i];
            if (character == '\\' && rawBackslashes % 2 == 0 && i + 1 < text.Length && text[i + 1] == 'u')
            {
                var j = i + 1;
                while (j < text.Length && text[j] == 'u')
                    j++;

                if (j + 4 <= text.Length && IsHex(text[j]) && IsHex(text[j + 1]) && IsHex(text[j + 2]) && IsHex(text[j + 3]))
                {
                    offsets.Add(i);
                    decoded.Append((char)Convert.ToInt32(text.Substring(j, 4), 16));
                    i = j + 4;

                    // A backslash made by an escape cannot start another escape, so the count starts over.
                    rawBackslashes = 0;
                    continue;
                }

                diagnostics.Add(new SyntaxDiagnostic(SourceSpan.FromBounds(i, Math.Min(text.Length, j + 4)), SyntaxDiagnostic.SyntaxError,
                    "A Unicode escape needs four hexadecimal digits after \\u"));
            }

            rawBackslashes = character == '\\' ? rawBackslashes + 1 : 0;
            offsets.Add(i);
            decoded.Append(character);
            i++;
        }

        offsets.Add(text.Length);
        map = [.. offsets];
        return decoded.ToString();
    }

    private static bool IsHex(char c) => char.IsAsciiHexDigit(c);
}
