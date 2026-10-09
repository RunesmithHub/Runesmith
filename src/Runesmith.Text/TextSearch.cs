using System.Text.RegularExpressions;

namespace Runesmith.Text;

/// <summary>How <see cref="TextSearch"/> matches its query.</summary>
[Flags]
public enum TextSearchOptions
{
    /// <summary>Plain text, ignoring case.</summary>
    None = 0,

    /// <summary>Upper and lower case must match.</summary>
    MatchCase = 1,

    /// <summary>Matches must not have identifier characters right before or after them.</summary>
    WholeWord = 2,

    /// <summary>The query is a .NET regular expression.</summary>
    Regex = 4,
}

/// <summary>Finds text in snapshots, as find and replace does.</summary>
public static class TextSearch
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    /// <summary>Builds the regular expression that matches <paramref name="query"/>, or gives the reason the query is not valid.</summary>
    public static bool TryCreateRegex(string query, TextSearchOptions options, out Regex? regex, out string? error)
    {
        ArgumentNullException.ThrowIfNull(query);
        regex = null;
        error = null;
        if (query.Length == 0)
            return false;

        var pattern = options.HasFlag(TextSearchOptions.Regex) ? query : Regex.Escape(query);
        if (options.HasFlag(TextSearchOptions.WholeWord))
            pattern = $@"(?<![\p{{L}}\p{{Nd}}_])(?:{pattern})(?![\p{{L}}\p{{Nd}}_])";
        var regexOptions = RegexOptions.Multiline | RegexOptions.CultureInvariant;
        if (!options.HasFlag(TextSearchOptions.MatchCase))
            regexOptions |= RegexOptions.IgnoreCase;

        try
        {
            regex = new Regex(pattern, regexOptions, Timeout);
            return true;
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    /// <summary>Finds every non-empty match of <paramref name="query"/>, within <paramref name="within"/> when given; an empty or invalid query finds nothing.</summary>
    /// <exception cref="RegexMatchTimeoutException">The regular expression took too long.</exception>
    public static IReadOnlyList<TextSpan> FindAll(TextSnapshot snapshot, string query, TextSearchOptions options, TextSpan? within = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!TryCreateRegex(query, options, out var regex, out _))
            return [];

        var range = within ?? new TextSpan(0, snapshot.Length);
        var text = snapshot.GetText();
        var matches = new List<TextSpan>();
        for (var match = regex!.Match(text, range.Start, range.Length); match.Success; match = match.NextMatch())
        {
            if (match.Length > 0)
                matches.Add(new TextSpan(match.Index, match.Length));
        }

        return matches;
    }

    /// <summary>Finds the first match at or after <paramref name="start"/>, or the last one ending at or before it when searching backwards.</summary>
    /// <param name="snapshot">The text to search.</param>
    /// <param name="query">The text or pattern to find.</param>
    /// <param name="options">How to match.</param>
    /// <param name="start">Where the search begins.</param>
    /// <param name="backwards">Whether to search towards the start of the text.</param>
    /// <param name="wrap">Whether to continue from the other end when nothing is found.</param>
    /// <returns>The match, or null when there is none.</returns>
    public static TextSpan? FindNext(TextSnapshot snapshot, string query, TextSearchOptions options, int start, bool backwards = false, bool wrap = true)
    {
        var matches = FindAll(snapshot, query, options);
        if (matches.Count == 0)
            return null;

        if (backwards)
        {
            for (var i = matches.Count - 1; i >= 0; i--)
            {
                if (matches[i].End <= start)
                    return matches[i];
            }

            return wrap ? matches[^1] : null;
        }

        foreach (var match in matches)
        {
            if (match.Start >= start)
                return match;
        }

        return wrap ? matches[0] : null;
    }
}
