namespace Runesmith.Languages.Java.Syntax;

/// <summary>A range of characters in a source text.</summary>
public readonly record struct SourceSpan(int Start, int Length)
{
    public int End => Start + Length;

    public bool IsEmpty => Length == 0;

    public static SourceSpan FromBounds(int start, int end) => new(start, end - start);

    /// <summary>Whether an offset lies inside the span, counting its end too.</summary>
    public bool ContainsInclusive(int offset) => offset >= Start && offset <= End;

    /// <summary>Gets whether the span contains an offset, not counting its end.</summary>
    public bool Contains(int offset) => offset >= Start && offset < End;

    public bool Contains(SourceSpan other) => other.Start >= Start && other.End <= End;

    public override string ToString() => $"[{Start}..{End})";
}
