using Runesmith.Sdk.Languages;
using Runesmith.Text;

namespace Runesmith.Editor.Folding;

/// <summary>Finds folding ranges from indentation, for languages without a folding provider: a line folds the lines after it that are
/// indented deeper, up to the last of them that is not blank.</summary>
public static class IndentationFolding
{
    /// <summary>Gets the ranges, sorted by their first line.</summary>
    public static IReadOnlyList<FoldingRange> Compute(TextSnapshot snapshot, int tabSize, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var indents = Indents(snapshot, tabSize, cancellationToken);
        var ends = new int[indents.Length];
        var open = new Stack<(int Indent, int Line)>();
        var lastText = -1;
        for (var number = 0; number < indents.Length; number++)
        {
            if (indents[number] < 0)
                continue;

            Close(indents[number], lastText);
            open.Push((indents[number], number));
            lastText = number;
        }

        Close(-1, lastText);
        var ranges = new List<FoldingRange>();
        for (var number = 0; number < ends.Length; number++)
        {
            if (ends[number] > number)
                ranges.Add(new FoldingRange(number, ends[number]));
        }

        return ranges;

        void Close(int indent, int end)
        {
            while (open.Count > 0 && open.Peek().Indent >= indent)
                ends[open.Pop().Line] = end;
        }
    }

    // Each line's indentation as a visual column, or -1 for a line of only white space, read in one pass that copies no text.
    private static int[] Indents(TextSnapshot snapshot, int tabSize, CancellationToken cancellationToken)
    {
        const int Indenting = 0, OnlyWhiteSpace = 1, HasText = 2;
        var indents = new int[snapshot.LineCount];
        var line = 0;
        var column = 0;
        var state = Indenting;
        foreach (var memory in snapshot.GetChunks(new TextSpan(0, snapshot.Length)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = memory.Span;
            for (var i = 0; i < chunk.Length; i++)
            {
                var c = chunk[i];
                if (c == '\n')
                {
                    indents[line++] = state == HasText ? column : -1;
                    column = 0;
                    state = Indenting;
                }
                else if (state == HasText)
                {
                    var next = chunk[i..].IndexOf('\n');
                    if (next < 0)
                        break;
                    i += next - 1;
                }
                else if (state == Indenting && c is ' ' or '\t')
                {
                    column = c == '\t' ? column + tabSize - column % tabSize : column + 1;
                }
                else
                {
                    state = char.IsWhiteSpace(c) ? OnlyWhiteSpace : HasText;
                }
            }
        }

        indents[line] = state == HasText ? column : -1;
        return indents;
    }
}
