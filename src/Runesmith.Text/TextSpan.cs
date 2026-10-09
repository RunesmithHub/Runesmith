namespace Runesmith.Text;

/// <summary>A range of text: <see cref="Length"/> characters from <see cref="Start"/>.</summary>
public readonly record struct TextSpan(int Start, int Length)
{
    /// <summary>Gets the position just past the span.</summary>
    public int End => Start + Length;

    /// <summary>Gets whether the span covers no characters.</summary>
    public bool IsEmpty => Length == 0;

    /// <summary>Creates the span from <paramref name="start"/> up to, not including, <paramref name="end"/>.</summary>
    public static TextSpan FromBounds(int start, int end)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(end, start);
        return new TextSpan(start, end - start);
    }

    /// <summary>Whether <paramref name="position"/> is inside the span; the end is not.</summary>
    public bool Contains(int position) => position >= Start && position < End;

    /// <summary>Whether <paramref name="span"/> lies entirely inside this span.</summary>
    public bool Contains(TextSpan span) => span.Start >= Start && span.End <= End;

    /// <summary>Whether the spans overlap or touch, so that an empty span at either end counts.</summary>
    public bool IntersectsWith(TextSpan span) => span.Start <= End && span.End >= Start;

    /// <summary>Gets the part both spans cover, or null when they do not intersect.</summary>
    public TextSpan? Intersection(TextSpan span)
    {
        var start = Math.Max(Start, span.Start);
        var end = Math.Min(End, span.End);
        return start <= end ? FromBounds(start, end) : null;
    }
}
