using System.Globalization;
using Runesmith.Sdk.Localization;

namespace Runesmith.Localization;

/// <summary>The languages to look a message up in for a language, most specific first: <c>nl-BE</c>, then <c>nl</c>.</summary>
internal static class LanguageChain
{
    public static IReadOnlyList<string> For(string tag)
    {
        var chain = new List<string>();
        Add(tag);
        try
        {
            for (var culture = CultureInfo.GetCultureInfo(tag).Parent; culture.Name.Length > 0; culture = culture.Parent)
                Add(culture.Name);
        }
        catch (CultureNotFoundException)
        {
        }

        var parts = tag.Split('-');
        for (var count = parts.Length - 1; count >= 1; count--)
        {
            if (parts[count - 1].Length > 1)
                Add(string.Join('-', parts[..count]));
        }

        return chain;

        void Add(string candidate)
        {
            if (LanguageTag.Canonicalize(candidate) is { } canonical && !chain.Contains(canonical))
                chain.Add(canonical);
        }
    }

    /// <summary>Gets a culture for a tag, or null when .NET knows no culture by that name.</summary>
    public static CultureInfo? Culture(string tag)
    {
        try
        {
            return CultureInfo.GetCultureInfo(tag);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    public static bool IsEnglish(string tag) => For(tag) is { Count: > 0 } chain && chain[^1] == LanguageTag.English;
}
