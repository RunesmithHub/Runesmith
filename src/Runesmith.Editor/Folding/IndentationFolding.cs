using Runesmith.Editor.Editing;
using Runesmith.Sdk.Languages;
using Runesmith.Text;

namespace Runesmith.Editor.Folding;

/// <summary>Finds folding ranges from indentation, for languages without a folding provider: a line folds the lines after it that are
/// indented deeper, up to the last of them that is not blank.</summary>
public static class IndentationFolding
{
    public static IReadOnlyList<FoldingRange> Compute(TextSnapshot snapshot, int tabSize, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var ranges = new List<FoldingRange>();
        var open = new Stack<(int Indent, int Line)>();
        var lastText = -1;
        for (var number = 0; number < snapshot.LineCount; number++)
        {
            if ((number & 1023) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            var text = snapshot.GetLineText(number);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var indent = EditOperations.VisualColumn(EditOperations.LeadingWhitespace(text), tabSize);
            Close(indent, lastText);
            open.Push((indent, number));
            lastText = number;
        }

        Close(-1, lastText);
        ranges.Reverse();
        return ranges;

        void Close(int indent, int end)
        {
            while (open.Count > 0 && open.Peek().Indent >= indent)
            {
                var (_, start) = open.Pop();
                if (end > start)
                    ranges.Add(new FoldingRange(start, end));
            }
        }
    }
}
