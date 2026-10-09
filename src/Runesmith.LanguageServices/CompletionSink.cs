using Runesmith.Text;

namespace Runesmith.LanguageServices;

/// <summary>Collects an analyzer's suggestions, keeping only those that match the typed word, and ranks and caps them at the end.</summary>
/// <remarks>An analyzer first sets the span of the word being completed with <see cref="SetWordSpan"/>, then adds candidates as it finds them.
/// Adding is cheap for candidates that do not match: most are rejected by a character mask before any scoring.</remarks>
public sealed class CompletionSink
{
    /// <summary>The number of suggestions a list is cut at; a cut list is marked incomplete.</summary>
    public const int DefaultCapacity = 300;

    private readonly TextSnapshot snapshot;
    private readonly int offset;
    private readonly List<(CompletionCandidate Candidate, int Score)> matches = [];
    private CompletionMatcher matcher;
    private int added;

    public CompletionSink(TextSnapshot snapshot, int offset)
    {
        this.snapshot = snapshot;
        this.offset = offset;
        WordSpan = DefaultWordSpan(snapshot, offset);
        matcher = new CompletionMatcher(snapshot.GetText(WordSpan));
    }

    /// <summary>Gets the span of the word the suggestions replace.</summary>
    public TextSpan WordSpan { get; private set; }

    /// <summary>Gets the typed part of the word, which candidates are matched against.</summary>
    public string Typed => matcher.Typed;

    /// <summary>Gets how many candidates were added, matching or not.</summary>
    public int Added => added;

    /// <summary>Gets how many candidates matched.</summary>
    public int Matched => matches.Count;

    /// <summary>Sets the span of the word being completed, when it differs from the identifier before the offset, such as for a
    /// keyword with a symbol in it.</summary>
    public void SetWordSpan(TextSpan span)
    {
        if (span.Start > offset || span.End > snapshot.Length)
            throw new ArgumentOutOfRangeException(nameof(span), "The word must start at or before the offset and lie inside the text.");

        WordSpan = span;
        matcher = new CompletionMatcher(snapshot.GetText(TextSpan.FromBounds(span.Start, Math.Max(span.Start, offset))));
    }

    /// <summary>Adds a candidate; it is kept only when it matches the typed word.</summary>
    public void Add(in CompletionCandidate candidate)
    {
        added++;
        if (matcher.Score(candidate.FilterText ?? candidate.Label) is { } score)
            matches.Add((candidate, score));
    }

    /// <summary>Ranks the matches and returns the best ones: by score, then sort group, then label.</summary>
    internal CompletionResult ToResult(int version, int capacity = DefaultCapacity)
    {
        matches.Sort(static (a, b) =>
        {
            var byScore = b.Score.CompareTo(a.Score);
            if (byScore != 0)
                return byScore;
            var byGroup = a.Candidate.SortGroup.CompareTo(b.Candidate.SortGroup);
            return byGroup != 0 ? byGroup : string.CompareOrdinal(a.Candidate.Label, b.Candidate.Label);
        });

        var count = Math.Min(capacity, matches.Count);
        var items = new CompletionEntry[count];
        for (var i = 0; i < count; i++)
            items[i] = new CompletionEntry(matches[i].Candidate, matches[i].Score);
        return new CompletionResult(items, WordSpan, matches.Count > capacity, version);
    }

    /// <summary>Gets the identifier that ends at an offset: letters, digits and underscores.</summary>
    public static TextSpan DefaultWordSpan(TextSnapshot snapshot, int offset)
    {
        var start = offset;
        while (start > 0 && (char.IsLetterOrDigit(snapshot[start - 1]) || snapshot[start - 1] == '_'))
            start--;
        return TextSpan.FromBounds(start, offset);
    }
}
