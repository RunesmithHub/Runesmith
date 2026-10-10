using System.Text;

namespace Runesmith.Shell.Terminal;

/// <summary>A color of terminal text: the default, one of the 256 indexed colors, or a 24-bit color.</summary>
internal readonly record struct TerminalColor(uint Value)
{
    private const uint IndexedFlag = 0x1000000;
    private const uint RgbFlag = 0x2000000;

    public static TerminalColor Default => default;

    public bool IsDefault => Value == 0;

    public bool IsIndexed => (Value & IndexedFlag) != 0;

    public bool IsRgb => (Value & RgbFlag) != 0;

    /// <summary>Gets the index of an indexed color, from 0 to 255.</summary>
    public int Index => (int)(Value & 0xFF);

    public byte R => (byte)(Value >> 16);

    public byte G => (byte)(Value >> 8);

    public byte B => (byte)Value;

    public static TerminalColor Indexed(int index) => new(IndexedFlag | (uint)Math.Clamp(index, 0, 255));

    public static TerminalColor Rgb(int r, int g, int b) => new(RgbFlag | ((uint)Math.Clamp(r, 0, 255) << 16) | ((uint)Math.Clamp(g, 0, 255) << 8) | (uint)Math.Clamp(b, 0, 255));
}

/// <summary>How a terminal cell's text is drawn, besides its colors.</summary>
[Flags]
internal enum CellFlags : ushort
{
    None = 0,
    Bold = 1,
    Dim = 2,
    Italic = 4,
    Underline = 8,
    Blink = 16,
    Inverse = 32,
    Hidden = 64,
    Strikethrough = 128,
    DoubleUnderline = 256,
}

/// <summary>The colors and flags of a terminal cell, as the program's last SGR sequence set them.</summary>
internal readonly record struct CellStyle(TerminalColor Foreground, TerminalColor Background, CellFlags Flags)
{
    public static CellStyle Default => default;

    /// <summary>Gets the style an erased cell gets: no flags and no foreground, but the background of the current style.</summary>
    public CellStyle Erased => new(TerminalColor.Default, Background, CellFlags.None);
}

/// <summary>One cell of the terminal's grid.</summary>
internal struct TerminalCell
{
    /// <summary>The character, or 0 for an empty cell.</summary>
    public int Rune;

    /// <summary>Marks that join the character, such as accents, or null.</summary>
    public string? Combining;

    /// <summary>1 for a normal cell, 2 for the first half of a wide character, 0 for its second half.</summary>
    public byte Width;

    public CellStyle Style;

    public static TerminalCell Blank(CellStyle style) => new() { Width = 1, Style = style };

    /// <summary>Gets the cell's text; an empty cell is a space and the second half of a wide character is empty.</summary>
    public readonly string Text => Width == 0 ? "" : Rune == 0 ? " " : Combining is null ? char.ConvertFromUtf32(Rune) : char.ConvertFromUtf32(Rune) + Combining;

    public readonly bool IsEmpty => Rune == 0 && Width == 1;
}

/// <summary>A line of the terminal: its cells, and whether the text goes on in the next line because it wrapped.</summary>
internal sealed class TerminalLine
{
    public TerminalLine(int columns, CellStyle style)
    {
        Cells = new TerminalCell[columns];
        Array.Fill(Cells, TerminalCell.Blank(style));
    }

    public TerminalCell[] Cells { get; private set; }

    /// <summary>Gets or sets whether the line's text goes on in the next line.</summary>
    public bool IsWrapped { get; set; }

    public int Length => Cells.Length;

    /// <summary>Gets whether every cell is empty and has the default background.</summary>
    public bool IsBlank => !IsWrapped && Array.TrueForAll(Cells, c => c.IsEmpty && c.Style.Background.IsDefault);

    /// <summary>Makes the line <paramref name="columns"/> wide, cutting or padding it; a wide character cut in half is removed.</summary>
    public void Resize(int columns, CellStyle style)
    {
        if (columns == Cells.Length)
            return;

        var cells = new TerminalCell[columns];
        var keep = Math.Min(columns, Cells.Length);
        Array.Copy(Cells, cells, keep);
        for (var i = keep; i < columns; i++)
            cells[i] = TerminalCell.Blank(style);
        if (keep > 0 && cells[keep - 1].Width == 2 && keep == columns)
            cells[keep - 1] = TerminalCell.Blank(cells[keep - 1].Style);
        Cells = cells;
    }

    /// <summary>Gets the text of the cells from <paramref name="start"/> to <paramref name="end"/>, without trailing spaces.</summary>
    public string GetText(int start = 0, int end = int.MaxValue)
    {
        var text = new StringBuilder();
        end = Math.Min(end, Cells.Length);
        for (var i = Math.Max(0, start); i < end; i++)
            text.Append(Cells[i].Text);
        return text.ToString().TrimEnd(' ');
    }

    /// <summary>Gets the column of the cell that holds the character a text offset falls in, for mapping text matches back to cells.</summary>
    public int ColumnOf(int offset)
    {
        var position = 0;
        for (var i = 0; i < Cells.Length; i++)
        {
            var length = Cells[i].Text.Length;
            if (offset < position + length)
                return i;
            position += length;
        }

        return Cells.Length;
    }
}
