namespace Runesmith.Text;

/// <summary>Finds words for double-click selection and word-wise caret movement; a word is a run of letters, digits and underscores.</summary>
public static class WordBoundaries
{
    /// <summary>Gets whether <paramref name="c"/> can be part of an identifier.</summary>
    public static bool IsWordCharacter(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>Gets the word at or right before <paramref name="position"/>, or an empty span at it when there is none.</summary>
    public static TextSpan GetWordAt(TextSnapshot snapshot, int position)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, snapshot.Length);

        var anchor = position < snapshot.Length && IsWordCharacter(snapshot[position]) ? position
            : position > 0 && IsWordCharacter(snapshot[position - 1]) ? position - 1
            : -1;
        if (anchor < 0)
            return new TextSpan(position, 0);

        var start = anchor;
        while (start > 0 && IsWordCharacter(snapshot[start - 1]))
            start--;
        var end = anchor + 1;
        while (end < snapshot.Length && IsWordCharacter(snapshot[end]))
            end++;
        return TextSpan.FromBounds(start, end);
    }

    /// <summary>Gets where Ctrl+Right moves to: past the white space, then past one word or one run of punctuation, stopping at line breaks.</summary>
    public static int NextWordBoundary(TextSnapshot snapshot, int position)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var length = snapshot.Length;
        if (position >= length)
            return length;
        if (snapshot[position] == '\n')
            return position + 1;

        while (position < length && IsSpace(snapshot[position]))
            position++;
        if (position >= length || snapshot[position] == '\n')
            return position;

        var kind = IsWordCharacter(snapshot[position]);
        while (position < length && snapshot[position] != '\n' && !IsSpace(snapshot[position]) && IsWordCharacter(snapshot[position]) == kind)
            position++;
        return position;
    }

    /// <summary>Gets where Ctrl+Left moves to: back past the white space, then back past one word or one run of punctuation, stopping at line breaks.</summary>
    public static int PreviousWordBoundary(TextSnapshot snapshot, int position)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (position <= 0)
            return 0;
        if (snapshot[position - 1] == '\n')
            return position - 1;

        while (position > 0 && IsSpace(snapshot[position - 1]))
            position--;
        if (position <= 0 || snapshot[position - 1] == '\n')
            return position;

        var kind = IsWordCharacter(snapshot[position - 1]);
        while (position > 0 && snapshot[position - 1] != '\n' && !IsSpace(snapshot[position - 1]) && IsWordCharacter(snapshot[position - 1]) == kind)
            position--;
        return position;
    }

    private static bool IsSpace(char c) => c != '\n' && char.IsWhiteSpace(c);
}
