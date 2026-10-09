namespace Runesmith.Text;

/// <summary>One line of a snapshot: where it starts, its length without and with its line break.</summary>
/// <param name="LineNumber">The line's number, counted from zero.</param>
/// <param name="Start">The position of the line's first character.</param>
/// <param name="Length">The number of characters before the line break.</param>
/// <param name="LengthIncludingBreak">The number of characters including the line break; the last line has none.</param>
public readonly record struct TextLine(int LineNumber, int Start, int Length, int LengthIncludingBreak)
{
    /// <summary>Gets the position of the line break, or the end of the text on the last line.</summary>
    public int End => Start + Length;

    /// <summary>Gets the position where the next line starts.</summary>
    public int EndIncludingBreak => Start + LengthIncludingBreak;

    /// <summary>Gets the line's text without its line break.</summary>
    public TextSpan Span => new(Start, Length);

    /// <summary>Gets the line's text with its line break.</summary>
    public TextSpan SpanIncludingBreak => new(Start, LengthIncludingBreak);
}
