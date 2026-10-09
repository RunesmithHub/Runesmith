namespace Runesmith.Editor.Rendering;

/// <summary>An inlay hint inside a formatted line.</summary>
/// <param name="Column">The column the hint is at.</param>
/// <param name="IsBefore">Whether the hint belongs to the text after it, so the caret at its column is drawn after it.</param>
/// <param name="Start">Where the hint starts in the formatted line, which counts the hints before it.</param>
internal readonly record struct LineInlay(int Column, string Text, bool IsBefore, int Start)
{
    public int End => Start + Text.Length;
}

/// <summary>Maps between a line's columns and the positions of the formatted line, which also holds the line's inlay hints.</summary>
/// <remarks>At a column, hints that belong to the text before come first, then hints that belong to the text after. The caret at that column
/// is drawn after all of them when one belongs to the text after, and before all of them otherwise.</remarks>
internal sealed class LineInlays
{
    public static LineInlays None { get; } = new([]);

    private LineInlays(LineInlay[] items) => Items = items;

    public LineInlay[] Items { get; }

    public bool IsEmpty => Items.Length == 0;

    /// <summary>Lays out hints given by column, text and side, in any order.</summary>
    public static LineInlays Create(IEnumerable<(int Column, string Text, bool IsBefore)> hints)
    {
        var sorted = hints.Where(h => h.Text.Length > 0).OrderBy(h => h.Column).ThenBy(h => h.IsBefore).ToList();
        if (sorted.Count == 0)
            return None;

        var items = new LineInlay[sorted.Count];
        var shift = 0;
        for (var i = 0; i < items.Length; i++)
        {
            items[i] = new LineInlay(sorted[i].Column, sorted[i].Text, sorted[i].IsBefore, sorted[i].Column + shift);
            shift += sorted[i].Text.Length;
        }

        return new LineInlays(items);
    }

    /// <summary>Gets the position of the caret before a column.</summary>
    public int ToIndex(int column)
    {
        var shift = 0;
        var atColumn = 0;
        var before = false;
        foreach (var item in Items)
        {
            if (item.Column < column)
                shift += item.Text.Length;
            else if (item.Column == column)
            {
                atColumn += item.Text.Length;
                before |= item.IsBefore;
            }
            else
                break;
        }

        return column + shift + (before ? atColumn : 0);
    }

    /// <summary>Gets the column of a position; a position inside a hint is the hint's column.</summary>
    public int ToColumn(int index)
    {
        var shift = 0;
        foreach (var item in Items)
        {
            if (index < item.Start)
                break;
            if (index < item.End)
                return item.Column;
            shift += item.Text.Length;
        }

        return index - shift;
    }

    /// <summary>Gets the hint at a position, or null.</summary>
    public LineInlay? At(int index)
    {
        foreach (var item in Items)
        {
            if (index < item.Start)
                return null;
            if (index < item.End)
                return item;
        }

        return null;
    }

    /// <summary>Gets where the next hint starts at or after a position, or <paramref name="end"/> when none does.</summary>
    public int NextStart(int index, int end)
    {
        foreach (var item in Items)
        {
            if (item.Start >= index)
                return Math.Min(item.Start, end);
        }

        return end;
    }
}
