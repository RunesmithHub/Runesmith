namespace Runesmith.Text;

/// <summary>Compares two versions of a text: which lines differ, and within changed lines, which words.</summary>
/// <remarks>Both use Myers' algorithm, which finds the fewest lines or words to add and delete; when the versions have little in common it
/// settles for a few more to stay fast. Any thread may call them.</remarks>
public static class TextDiff
{
    private const int MaximumWordDiffLength = 20_000;

    /// <summary>Finds the lines that differ between two texts, in order.</summary>
    /// <remarks>Lines are split at \n, with a \r before it ignored, and counted as <see cref="TextSnapshot"/> counts them: a text that ends with
    /// a line break ends with an empty line.</remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static IReadOnlyList<DiffHunk> Lines(string oldText, string newText, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(oldText);
        ArgumentNullException.ThrowIfNull(newText);
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        var a = LineIds(oldText, ids, out var blanksA);
        var b = LineIds(newText, ids, out var blanksB);
        cancellationToken.ThrowIfCancellationRequested();
        return MyersDiff.Compute(a, b, blanksA, blanksB, cancellationToken);
    }

    /// <summary>Finds the characters that differ between two short texts, such as the lines of a changed run, a word at a time, so a renamed
    /// identifier counts as one change rather than as scattered letters.</summary>
    /// <remarks>Words are runs of letters, digits and underscores, runs of whitespace, or single other characters. Texts longer than
    /// 20,000 characters are not compared and come back as one change.</remarks>
    public static IReadOnlyList<DiffHunk> Words(string oldText, string newText)
    {
        ArgumentNullException.ThrowIfNull(oldText);
        ArgumentNullException.ThrowIfNull(newText);
        if (oldText.Length + newText.Length > MaximumWordDiffLength)
            return oldText == newText ? [] : [new DiffHunk(0, oldText.Length, 0, newText.Length)];

        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        var (a, startsA) = WordIds(oldText, ids);
        var (b, startsB) = WordIds(newText, ids);
        var hunks = MyersDiff.Compute(a, b, null, null, CancellationToken.None);
        var result = new List<DiffHunk>(hunks.Count);
        foreach (var hunk in hunks)
        {
            var oldStart = startsA[hunk.OldStart];
            var newStart = startsB[hunk.NewStart];
            result.Add(new DiffHunk(oldStart, startsA[hunk.OldEnd] - oldStart, newStart, startsB[hunk.NewEnd] - newStart));
        }

        return result;
    }

    private static int[] LineIds(string text, Dictionary<string, int> ids, out bool[] blanks)
    {
        var lookup = ids.GetAlternateLookup<ReadOnlySpan<char>>();
        var count = text.AsSpan().Count('\n') + 1;
        var result = new int[count];
        blanks = new bool[count];
        var start = 0;
        for (var line = 0; line < count; line++)
        {
            var end = line == count - 1 ? text.Length : text.IndexOf('\n', start);
            var span = text.AsSpan(start, end - start);
            if (span.Length > 0 && span[^1] == '\r')
                span = span[..^1];
            if (!lookup.TryGetValue(span, out var id))
            {
                id = ids.Count;
                lookup[span] = id;
            }

            result[line] = id;
            blanks[line] = span.IsWhiteSpace();
            start = end + 1;
        }

        return result;
    }

    // The starts of the words, with the text's length after the last one, so a run of words maps back to characters.
    private static (int[] Ids, int[] Starts) WordIds(string text, Dictionary<string, int> ids)
    {
        var lookup = ids.GetAlternateLookup<ReadOnlySpan<char>>();
        var words = new List<int>();
        var starts = new List<int>();
        var position = 0;
        while (position < text.Length)
        {
            var end = position + 1;
            var c = text[position];
            if (WordBoundaries.IsWordCharacter(c))
            {
                while (end < text.Length && WordBoundaries.IsWordCharacter(text[end]))
                    end++;
            }
            else if (char.IsWhiteSpace(c) && c != '\n')
            {
                while (end < text.Length && char.IsWhiteSpace(text[end]) && text[end] != '\n')
                    end++;
            }

            var span = text.AsSpan(position, end - position);
            if (!lookup.TryGetValue(span, out var id))
            {
                id = ids.Count;
                lookup[span] = id;
            }

            words.Add(id);
            starts.Add(position);
            position = end;
        }

        starts.Add(text.Length);
        return ([.. words], [.. starts]);
    }
}
