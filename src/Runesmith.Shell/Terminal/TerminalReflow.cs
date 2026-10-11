namespace Runesmith.Shell.Terminal;

/// <summary>Lines of a terminal rewrapped to a new width: lines that wrapped into each other because their text reached the right edge are
/// joined into one line of text and split again at the new width, and the line breaks the program wrote stay.</summary>
internal sealed class TerminalReflow
{
    private readonly int columns;
    private readonly int[] groupOf;
    private readonly int[] offsetOf;
    private readonly List<int> firstLineOf = [];
    private readonly List<int> starts = [];

    private TerminalReflow(int columns, int count)
    {
        this.columns = columns;
        groupOf = new int[count];
        offsetOf = new int[count];
    }

    /// <summary>Gets the rewrapped lines. A line that fit the new width as it was is the same object, still its old width.</summary>
    public List<TerminalLine> Lines { get; } = [];

    /// <summary>Gets the cursor's new line and column, and whether it waits at the right edge for the next character to wrap.</summary>
    public (int Line, int Column, bool WrapPending) Cursor { get; private set; }

    /// <summary>Rewraps lines to <paramref name="columns"/>, keeping the cursor on the same character of its text.</summary>
    /// <param name="cursorLine">The cursor's line among <paramref name="lines"/>.</param>
    /// <param name="wrapPending">Whether the cursor waits at the right edge, so that it stands after the character in its column.</param>
    public static TerminalReflow Rewrap(IReadOnlyList<TerminalLine> lines, int columns, int cursorLine, int cursorColumn, bool wrapPending)
    {
        var reflow = new TerminalReflow(columns, lines.Count);
        var cells = new List<TerminalCell>();
        var first = 0;
        while (first < lines.Count)
        {
            var last = first;
            while (last < lines.Count - 1 && lines[last].IsWrapped)
                last++;
            reflow.Add(lines, first, last, cells, cursorLine >= first && cursorLine <= last ? (cursorLine, cursorColumn, wrapPending) : null);
            first = last + 1;
        }

        reflow.firstLineOf.Add(reflow.Lines.Count);
        return reflow;
    }

    /// <summary>Gets where a place in the old lines is in the new ones.</summary>
    public (int Line, int Column) Map(int line, int column)
    {
        var group = groupOf[line];
        var offset = offsetOf[line] + Math.Max(0, column);
        var row = RowOf(group, offset);
        return (row, Math.Min(offset - starts[row], columns));
    }

    private int RowOf(int group, int offset)
    {
        var row = firstLineOf[group];
        var end = group + 1 < firstLineOf.Count ? firstLineOf[group + 1] : Lines.Count;
        while (row + 1 < end && starts[row + 1] <= offset)
            row++;
        return row;
    }

    // Adds the new lines of the text in lines[first..last], which wrapped into each other.
    private void Add(IReadOnlyList<TerminalLine> lines, int first, int last, List<TerminalCell> cells, (int Line, int Column, bool WrapPending)? cursor)
    {
        var group = firstLineOf.Count;
        firstLineOf.Add(Lines.Count);
        cells.Clear();
        for (var i = first; i <= last; i++)
        {
            groupOf[i] = group;
            offsetOf[i] = cells.Count;
            var row = lines[i].Cells;
            cells.AddRange(row.AsSpan(0, i < last && lines[i].EndsWithPaddingBefore(lines[i + 1]) ? row.Length - 1 : row.Length));
        }

        var lastCells = lines[last].Cells;
        var used = lastCells.Length;
        while (used > 0 && lastCells[used - 1].IsEmpty)
            used--;
        var length = offsetOf[last] + used;
        var at = -1;
        if (cursor is { } place)
        {
            at = offsetOf[place.Line] + place.Column + (place.WrapPending ? 1 : 0);
            length = Math.Max(length, at);
        }

        if (first == last && length <= columns)
        {
            lines[last].IsWrapped = false;
            starts.Add(0);
            Lines.Add(lines[last]);
        }
        else
            Split(cells, length);

        if (at >= 0)
        {
            var row = RowOf(group, at);
            var column = at - starts[row];
            Cursor = column >= columns
                ? (row, columns - 1, true)
                : cursor!.Value.WrapPending && column == 0 && row > firstLineOf[group] && starts[row] - starts[row - 1] == columns
                    ? (row - 1, columns - 1, true)
                    : (row, column, false);
        }
    }

    // Splits a text of cells into lines of the new width; a wide character that would straddle the edge starts the next line.
    private void Split(List<TerminalCell> cells, int length)
    {
        var position = 0;
        do
        {
            var line = new TerminalLine(columns, CellStyle.Default);
            starts.Add(position);
            var column = 0;
            while (column < columns && position < length)
            {
                var cell = position < cells.Count ? cells[position] : TerminalCell.Blank(CellStyle.Default);
                if (cell.Width == 2)
                {
                    if (column == columns - 1)
                    {
                        line.HasWidePadding = true;
                        break;
                    }

                    line.Cells[column++] = cell;
                    line.Cells[column++] = position + 1 < cells.Count ? cells[position + 1] : new TerminalCell { Style = cell.Style };
                    position += 2;
                }
                else
                {
                    line.Cells[column++] = cell.Width == 0 ? TerminalCell.Blank(cell.Style) : cell;
                    position++;
                }
            }

            line.IsWrapped = position < length;
            for (var i = position; !line.IsWrapped && column < columns && i < cells.Count; i++)
                line.Cells[column++] = cells[i];
            Lines.Add(line);
        }
        while (position < length);
    }
}
