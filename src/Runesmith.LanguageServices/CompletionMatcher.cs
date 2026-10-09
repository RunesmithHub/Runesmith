namespace Runesmith.LanguageServices;

/// <summary>Matches suggestions against a typed word: a prefix is best, then the starts of word parts such as the <c>WL</c> of
/// <c>WriteLine</c>, then the letters anywhere in order. Matching does not allocate.</summary>
public sealed class CompletionMatcher
{
    private readonly string typed;
    private readonly ulong mask;

    public CompletionMatcher(string typed)
    {
        this.typed = typed ?? "";
        mask = MaskOf(this.typed);
    }

    /// <summary>Gets the typed word.</summary>
    public string Typed => typed;

    /// <summary>Gets a mask of the letters, digits and underscores in a text, to reject candidates that lack one of the typed characters.</summary>
    public static ulong MaskOf(ReadOnlySpan<char> text)
    {
        var result = 0UL;
        foreach (var character in text)
            result |= Bit(character);
        return result;
    }

    /// <summary>Scores a candidate, higher is better, or returns null when it does not match.</summary>
    /// <param name="candidateMask">The candidate's <see cref="MaskOf"/>, when it is known already.</param>
    public int? Score(ReadOnlySpan<char> candidate, ulong? candidateMask = null)
    {
        if (typed.Length == 0)
            return 0;

        if ((mask & ~(candidateMask ?? MaskOf(candidate))) != 0)
            return null;

        if (candidate.StartsWith(typed, StringComparison.Ordinal))
            return 1000 - (candidate.Length - typed.Length);

        if (candidate.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
            return 900 - (candidate.Length - typed.Length);

        // Jumping ahead to word starts can leave too few letters for the rest, so fall back to the earliest positions.
        return Subsequence(candidate, preferPartStarts: true) ?? Subsequence(candidate, preferPartStarts: false);
    }

    private int? Subsequence(ReadOnlySpan<char> candidate, bool preferPartStarts)
    {
        var score = 0;
        var index = 0;
        var previous = -2;
        foreach (var character in typed)
        {
            var found = -1;
            var lower = char.ToLowerInvariant(character);
            for (var i = index; i < candidate.Length; i++)
            {
                if (char.ToLowerInvariant(candidate[i]) != lower)
                    continue;

                var partStart = IsPartStart(candidate, i);
                if (found < 0 || partStart && !IsPartStart(candidate, found))
                    found = i;
                if (partStart || !preferPartStarts)
                    break;
            }

            if (found < 0)
                return null;

            score += IsPartStart(candidate, found) ? 30 : found == previous + 1 ? 15 : 2;
            if (candidate[found] == character)
                score++;
            previous = found;
            index = found + 1;
        }

        return score - candidate.Length / 4;
    }

    private static bool IsPartStart(ReadOnlySpan<char> text, int index) =>
        index == 0
        || char.IsUpper(text[index]) && !char.IsUpper(text[index - 1])
        || !char.IsLetterOrDigit(text[index - 1]) && char.IsLetterOrDigit(text[index]);

    private static ulong Bit(char character) => character switch
    {
        >= 'a' and <= 'z' => 1UL << (character - 'a'),
        >= 'A' and <= 'Z' => 1UL << (character - 'A'),
        >= '0' and <= '9' => 1UL << (26 + character - '0'),
        '_' => 1UL << 36,
        _ => 0,
    };
}
