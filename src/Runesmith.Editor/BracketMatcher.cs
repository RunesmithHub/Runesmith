using Runesmith.Text;

namespace Runesmith.Editor;

/// <summary>Finds the bracket that matches the one at the caret.</summary>
internal static class BracketMatcher
{
    private const int ScanLimit = 20_000;

    /// <summary>Finds the pair of the bracket just after or just before the caret, or returns null.</summary>
    public static (int Open, int Close)? Find(TextSnapshot snapshot, int caret, IReadOnlyList<(char Open, char Close)> brackets)
    {
        foreach (var at in (ReadOnlySpan<int>)[caret, caret - 1])
        {
            if (at < 0 || at >= snapshot.Length)
                continue;

            var character = snapshot[at];
            foreach (var (open, close) in brackets)
            {
                if (character == open && Scan(snapshot, at, open, close, forward: true) is { } closeAt)
                    return (at, closeAt);
                if (character == close && Scan(snapshot, at, open, close, forward: false) is { } openAt)
                    return (openAt, at);
            }
        }

        return null;
    }

    private static int? Scan(TextSnapshot snapshot, int from, char open, char close, bool forward)
    {
        var depth = 0;
        var step = forward ? 1 : -1;
        var end = forward ? Math.Min(snapshot.Length, from + ScanLimit) : Math.Max(-1, from - ScanLimit);
        for (var i = from; i != end; i += step)
        {
            var character = snapshot[i];
            if (character == (forward ? open : close))
                depth++;
            else if (character == (forward ? close : open) && --depth == 0)
                return i;
        }

        return null;
    }
}
