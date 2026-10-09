using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Runesmith.Editor.Rendering;

/// <summary>A formatted line of the document, ready to draw and to hit test.</summary>
internal sealed class VisualLine(string text, TextLine line, LineInlays inlays) : IDisposable
{
    public VisualLine(string text, TextLine line)
        : this(text, line, LineInlays.None)
    {
    }

    public string Text { get; } = text;

    public TextLine Line { get; } = line;

    /// <summary>Gets the inlay hints drawn inside the line.</summary>
    public LineInlays Inlays { get; } = inlays;

    public double Width => Line.WidthIncludingTrailingWhitespace;

    /// <summary>Gets the distance from the line's start to the caret position before a column.</summary>
    public double GetX(int column)
    {
        column = Math.Clamp(column, 0, Text.Length);
        return Line.GetDistanceFromCharacterHit(new CharacterHit(Inlays.IsEmpty ? column : Inlays.ToIndex(column)));
    }

    /// <summary>Gets the column whose caret position is closest to a distance from the line's start.</summary>
    public int GetColumn(double x)
    {
        if (x <= 0)
            return 0;

        var hit = Line.GetCharacterHitFromDistance(x);
        var index = hit.FirstCharacterIndex + hit.TrailingLength;
        return Math.Clamp(Inlays.IsEmpty ? index : Inlays.ToColumn(index), 0, Text.Length);
    }

    /// <summary>Gets the horizontal extent of each inlay hint's text without the spaces around it, from the line's start.</summary>
    public IEnumerable<(double Left, double Right, LineInlay Inlay)> InlayBounds() =>
        Inlays.Items.Select(inlay =>
        {
            var text = inlay.Text.AsSpan();
            var start = inlay.Start + (text.Length - text.TrimStart().Length);
            var end = Math.Max(start, inlay.End - (text.Length - text.TrimEnd().Length));
            return (Line.GetDistanceFromCharacterHit(new CharacterHit(start)), Line.GetDistanceFromCharacterHit(new CharacterHit(end)), inlay);
        });

    public void Dispose() => Line.Dispose();
}

/// <summary>Keeps the formatted lines that were drawn recently, so scrolling and blinking the caret do not format them again.</summary>
internal sealed class VisualLineCache
{
    private const int Capacity = 600;
    private Dictionary<int, VisualLine> lines = [];

    public bool TryGet(int lineNumber, out VisualLine line) => lines.TryGetValue(lineNumber, out line!);

    public void Add(int lineNumber, VisualLine line)
    {
        if (lines.Count >= Capacity)
            Clear();
        lines[lineNumber] = line;
    }

    /// <summary>Forgets the lines in a range, inclusive.</summary>
    public void Invalidate(int first, int last)
    {
        foreach (var number in lines.Keys.Where(n => n >= first && n <= last).ToList())
        {
            lines[number].Dispose();
            lines.Remove(number);
        }
    }

    /// <summary>Forgets the lines an edit touched and renumbers the lines after it.</summary>
    /// <param name="first">The first line the edit touched.</param>
    /// <param name="lastBefore">The last line the edit touched, numbered as before the edit.</param>
    /// <param name="delta">How many lines the edit added, or removed when negative.</param>
    public void ApplyEdit(int first, int lastBefore, int delta)
    {
        if (delta == 0)
        {
            Invalidate(first, lastBefore);
            return;
        }

        var shifted = new Dictionary<int, VisualLine>(lines.Count);
        foreach (var (number, line) in lines)
        {
            if (number < first)
                shifted[number] = line;
            else if (number > lastBefore)
                shifted[number + delta] = line;
            else
                line.Dispose();
        }

        lines = shifted;
    }

    public void Clear()
    {
        foreach (var line in lines.Values)
            line.Dispose();
        lines.Clear();
    }
}
