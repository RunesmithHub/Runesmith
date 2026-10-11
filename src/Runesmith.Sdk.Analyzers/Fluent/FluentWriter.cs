using System.Globalization;
using System.Text;

namespace Runesmith.Sdk.Analyzers.Fluent;

/// <summary>Writes text as Fluent: messages whose value shows exactly the text, and ids made from it.</summary>
internal static class FluentWriter
{
    private const int IdWords = 5;
    private const int IdLength = 40;

    /// <summary>Writes a message, on one line or, for text of several lines, indented under its id.</summary>
    /// <param name="pattern">The value, from <see cref="Escape"/> and placeables.</param>
    public static string Message(string id, string pattern)
    {
        var lines = pattern.Split('\n');
        if (lines.Length == 1)
            return $"{id} = {pattern}";

        var message = new StringBuilder(id).Append(" =");
        foreach (var line in lines)
        {
            message.Append('\n');
            if (line.Length > 0)
                message.Append("    ").Append(line);
        }

        return message.ToString();
    }

    /// <summary>Escapes text for a message's value: braces become quoted placeables, and spaces at the edges, which Fluent drops, and the
    /// characters that would start an attribute or a variant on a new line are quoted too.</summary>
    /// <param name="atStart">Whether the text starts the value, so its leading spaces would be dropped.</param>
    /// <param name="atEnd">Whether the text ends the value, so its trailing spaces would be dropped.</param>
    public static string Escape(string text, bool atStart = true, bool atEnd = true)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var multiline = lines.Length > 1;
        var result = new StringBuilder();
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
                result.Append('\n');

            var line = lines[i];
            var leading = line.Length - line.TrimStart(' ').Length;
            var trailing = line.TrimStart(' ').Length - line.Trim(' ').Length;
            var body = line.Substring(leading, line.Length - leading - trailing);
            var lineStart = i > 0 || atStart || multiline;
            if (leading > 0 && lineStart)
                result.Append(Quoted(new string(' ', leading)));
            else
                result.Append(' ', leading);

            for (var j = 0; j < body.Length; j++)
            {
                var character = body[j];
                var special = character is '{' or '}' || (j == 0 && leading == 0 && (i > 0 || multiline) && character is '[' or '*' or '.');
                if (special)
                    result.Append(Quoted(character.ToString()));
                else
                    result.Append(character);
            }

            if (trailing > 0 && (i < lines.Length - 1 || atEnd))
                result.Append(Quoted(new string(' ', trailing)));
            else
                result.Append(' ', trailing);
        }

        return result.ToString();
    }

    /// <summary>Makes a message id from text, such as <c>save-all-files</c> for "Save all files", that is not yet taken.</summary>
    public static string Id(string text, Func<string, bool> isTaken)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        foreach (var character in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;
            if (character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
            {
                word.Append(char.ToLowerInvariant(character));
                continue;
            }

            if (word.Length > 0)
                words.Add(word.ToString());
            word.Clear();
        }

        if (word.Length > 0)
            words.Add(word.ToString());

        var id = new StringBuilder();
        foreach (var part in words.Take(IdWords))
        {
            if (id.Length > 0 && id.Length + part.Length + 1 > IdLength)
                break;
            if (id.Length > 0)
                id.Append('-');
            id.Append(part);
        }

        var stem = id.Length == 0 ? "text" : id[0] is >= 'a' and <= 'z' ? id.ToString() : "text-" + id;
        var candidate = stem;
        for (var number = 2; isTaken(candidate); number++)
            candidate = stem + "-" + number.ToString(CultureInfo.InvariantCulture);
        return candidate;
    }

    private static string Quoted(string text) => "{\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}";
}
