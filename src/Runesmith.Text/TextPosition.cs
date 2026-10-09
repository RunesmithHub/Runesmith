namespace Runesmith.Text;

/// <summary>A place in text by line and column, both counted from zero; columns count UTF-16 characters.</summary>
public readonly record struct TextPosition(int Line, int Column) : IComparable<TextPosition>
{
    /// <summary>Orders positions by line, then by column.</summary>
    public int CompareTo(TextPosition other) => Line != other.Line ? Line.CompareTo(other.Line) : Column.CompareTo(other.Column);

    public static bool operator <(TextPosition left, TextPosition right) => left.CompareTo(right) < 0;

    public static bool operator >(TextPosition left, TextPosition right) => left.CompareTo(right) > 0;

    public static bool operator <=(TextPosition left, TextPosition right) => left.CompareTo(right) <= 0;

    public static bool operator >=(TextPosition left, TextPosition right) => left.CompareTo(right) >= 0;
}
