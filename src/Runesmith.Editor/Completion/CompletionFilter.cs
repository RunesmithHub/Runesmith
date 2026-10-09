namespace Runesmith.Editor.Completion;

/// <summary>Matches completion items against the word being typed: a prefix is best, then the start of each word part such as the
/// <c>WL</c> of <c>WriteLine</c>, then the letters anywhere in order.</summary>
internal static class CompletionFilter
{
    /// <summary>Scores a candidate, higher is better, or returns null when it does not match; matched holds the matched character indexes.</summary>
    public static int? Score(string candidate, string typed, List<int>? matched = null)
    {
        matched?.Clear();
        if (typed.Length == 0)
            return 0;

        if (candidate.StartsWith(typed, StringComparison.Ordinal))
            return Prefix(candidate, typed, matched, 1000);

        if (candidate.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
            return Prefix(candidate, typed, matched, 900);

        // Jumping ahead to word starts can leave too few letters for the rest, so fall back to the earliest positions.
        return Subsequence(candidate, typed, matched, preferPartStarts: true) ?? Subsequence(candidate, typed, matched, preferPartStarts: false);
    }

    private static int? Subsequence(string candidate, string typed, List<int>? matched, bool preferPartStarts)
    {
        matched?.Clear();
        var score = 0;
        var index = 0;
        var previousMatch = -2;
        foreach (var character in typed)
        {
            var found = -1;
            for (var i = index; i < candidate.Length; i++)
            {
                if (char.ToLowerInvariant(candidate[i]) != char.ToLowerInvariant(character))
                    continue;

                var partStart = IsPartStart(candidate, i);
                if (found < 0 || partStart && !IsPartStart(candidate, found))
                    found = i;
                if (partStart || !preferPartStarts)
                    break;
            }

            if (found < 0)
                return null;

            score += IsPartStart(candidate, found) ? 30 : found == previousMatch + 1 ? 15 : 2;
            if (candidate[found] == character)
                score += 1;
            matched?.Add(found);
            previousMatch = found;
            index = found + 1;
        }

        return score - candidate.Length / 4;
    }

    /// <summary>Gets a mask of the letters, digits and underscores in a text, ignoring case, to reject candidates that lack one of the
    /// typed characters with one AND.</summary>
    public static ulong MaskOf(string text)
    {
        var result = 0UL;
        foreach (var character in text)
        {
            result |= character switch
            {
                >= 'a' and <= 'z' => 1UL << (character - 'a'),
                >= 'A' and <= 'Z' => 1UL << (character - 'A'),
                >= '0' and <= '9' => 1UL << (26 + character - '0'),
                '_' => 1UL << 36,
                _ => 0,
            };
        }

        return result;
    }

    private static int Prefix(string candidate, string typed, List<int>? matched, int score)
    {
        for (var i = 0; i < typed.Length; i++)
            matched?.Add(i);
        return score - (candidate.Length - typed.Length);
    }

    private static bool IsPartStart(string text, int index) =>
        index == 0
        || char.IsUpper(text[index]) && !char.IsUpper(text[index - 1])
        || !char.IsLetterOrDigit(text[index - 1]) && char.IsLetterOrDigit(text[index]);
}
