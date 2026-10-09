namespace Runesmith.Workspace.Search;

/// <summary>Scores how well a query matches a name, letting the query's letters appear anywhere in order, as editors' quick pickers do.</summary>
/// <remarks>Letters at the start, at word starts (after <c>/ \ . _ -</c> and space, and at camelCase humps) and in runs of consecutive letters
/// score higher, as do shorter names. Case and spaces in the query are ignored.</remarks>
public static class FuzzyMatcher
{
    /// <summary>Returns 0 when the query's letters do not appear in order in <paramref name="candidate"/>, and otherwise a positive score.</summary>
    public static double Score(ReadOnlySpan<char> candidate, string query) => Match(candidate, query, null);

    /// <summary>Scores like <see cref="Score(ReadOnlySpan{char}, string)"/> and also returns the indices of the matched characters.</summary>
    public static double Score(string candidate, string query, out int[] matches)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var found = new List<int>(query.Length);
        var score = Match(candidate, query, found);
        matches = score > 0 ? [.. found] : [];
        return score;
    }

    private static double Match(ReadOnlySpan<char> text, string query, List<int>? matches)
    {
        ArgumentNullException.ThrowIfNull(query);
        Span<char> pattern = query.Length <= 256 ? stackalloc char[query.Length] : new char[query.Length];
        var length = 0;
        foreach (var c in query)
        {
            if (c != ' ')
                pattern[length++] = char.ToUpperInvariant(c);
        }

        pattern = pattern[..length];
        if (pattern.IsEmpty)
            return 1;
        if (pattern.Length > text.Length)
            return 0;

        // The latest position each letter may take so that the letters after it still fit; a better-scoring later match must not strand them.
        Span<int> latest = pattern.Length <= 256 ? stackalloc int[pattern.Length] : new int[pattern.Length];
        var j = text.Length - 1;
        for (var k = pattern.Length - 1; k >= 0; k--)
        {
            while (j >= 0 && char.ToUpperInvariant(text[j]) != pattern[k])
                j--;
            if (j < 0)
                return 0;
            latest[k] = j--;
        }

        double score = 0;
        var t = 0;
        var previousMatch = -2;
        for (var k = 0; k < pattern.Length; k++)
        {
            var found = -1;
            var best = double.MinValue;
            for (var i = t; i <= latest[k]; i++)
            {
                if (char.ToUpperInvariant(text[i]) != pattern[k])
                    continue;
                if (found >= 0 && i > found + 8)
                    break;

                var bonus = Bonus(text, i, previousMatch);
                if (bonus > best)
                {
                    best = bonus;
                    found = i;
                    if (bonus >= 8)
                        break;
                }
            }

            score += best - Math.Min(found - t, 6) * 0.25;
            matches?.Add(found);
            previousMatch = found;
            t = found + 1;
        }

        var trimmed = query.AsSpan().Trim();
        if (text.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
            score += 12;
        else if (text.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            score += 6;
        return Math.Max(0.01, score - text.Length * 0.05);
    }

    private static double Bonus(ReadOnlySpan<char> text, int index, int previousMatch)
    {
        if (index == 0)
            return 10;
        if (index == previousMatch + 1)
            return 6;

        var before = text[index - 1];
        if (before is ' ' or '.' or '/' or '\\' or '-' or '_' or '(' or ':')
            return 8;
        if (char.IsLower(before) && char.IsUpper(text[index]))
            return 7;
        return 1;
    }
}
