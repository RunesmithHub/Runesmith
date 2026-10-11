using System.Text;

namespace Runesmith.Sdk.Localization;

/// <summary>BCP 47 language tags, such as <c>nl</c>, <c>nl-BE</c> and <c>zh-Hant-TW</c>: whether text is one, and its canonical case.</summary>
/// <remarks>Shared as source with the SDK's build tools, which target .NET Standard 2.0.</remarks>
internal static class LanguageTag
{
    /// <summary>The tag of English, the language every message has.</summary>
    public const string English = "en";

    /// <summary>Gets whether the text is a well-formed tag, in any case.</summary>
    public static bool IsValid(string? text) => Canonicalize(text) is not null;

    /// <summary>Gets the tag in its canonical case, such as <c>nl-BE</c> for <c>NL-be</c>, or null when the text is not a well-formed tag.</summary>
    public static string? Canonicalize(string? text)
    {
        if (text is null || text.Length == 0 || text.Length > 64)
            return null;

        var parts = text.Split('-');
        foreach (var part in parts)
        {
            if (part.Length is 0 or > 8 || !part.All(IsAlphanumeric))
                return null;
        }

        var result = new StringBuilder(text.Length);
        var index = 0;
        if (!string.Equals(parts[0], "x", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsLanguage(parts[0]))
                return null;

            Append(result, parts[index++].ToLowerInvariant());
            for (var extlangs = 0; extlangs < 3 && parts[0].Length <= 3 && index < parts.Length && parts[index].Length == 3 && parts[index].All(IsLetter); extlangs++)
                Append(result, parts[index++].ToLowerInvariant());
            if (index < parts.Length && parts[index].Length == 4 && parts[index].All(IsLetter))
                Append(result, char.ToUpperInvariant(parts[index][0]) + parts[index++].Substring(1).ToLowerInvariant());
            if (index < parts.Length && ((parts[index].Length == 2 && parts[index].All(IsLetter)) || (parts[index].Length == 3 && parts[index].All(IsDigit))))
                Append(result, parts[index++].ToUpperInvariant());

            var variants = new HashSet<string>(StringComparer.Ordinal);
            while (index < parts.Length && IsVariant(parts[index]))
            {
                if (!variants.Add(parts[index].ToLowerInvariant()))
                    return null;
                Append(result, parts[index++].ToLowerInvariant());
            }

            var singletons = new HashSet<char>();
            while (index < parts.Length && parts[index].Length == 1 && !string.Equals(parts[index], "x", StringComparison.OrdinalIgnoreCase))
            {
                if (!singletons.Add(char.ToLowerInvariant(parts[index][0])))
                    return null;
                Append(result, parts[index++].ToLowerInvariant());
                var start = index;
                while (index < parts.Length && parts[index].Length >= 2)
                    Append(result, parts[index++].ToLowerInvariant());
                if (index == start)
                    return null;
            }
        }

        if (index < parts.Length && string.Equals(parts[index], "x", StringComparison.OrdinalIgnoreCase))
        {
            Append(result, "x");
            var start = ++index;
            while (index < parts.Length)
                Append(result, parts[index++].ToLowerInvariant());
            if (index == start)
                return null;
        }

        return index == parts.Length ? result.ToString() : null;
    }

    /// <summary>Gets whether two tags name the same language, in any case.</summary>
    public static bool AreSame(string? first, string? second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

    private static bool IsLanguage(string part) => part.Length is >= 2 and <= 8 && part.Length != 4 && part.All(IsLetter);

    private static bool IsVariant(string part) => (part.Length >= 5 && part.Length <= 8) || (part.Length == 4 && IsDigit(part[0]));

    private static void Append(StringBuilder result, string part)
    {
        if (result.Length > 0)
            result.Append('-');
        result.Append(part);
    }

    private static bool IsLetter(char character) => character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');

    private static bool IsDigit(char character) => character is >= '0' and <= '9';

    private static bool IsAlphanumeric(char character) => IsLetter(character) || IsDigit(character);
}
