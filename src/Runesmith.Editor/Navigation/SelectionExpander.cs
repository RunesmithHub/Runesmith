using Runesmith.Sdk.Languages;
using Runesmith.Text;

namespace Runesmith.Editor.Navigation;

/// <summary>Expand Selection and Shrink Selection: grows the selection through the spans the selection range providers give, or through
/// words, brackets and lines without one, and shrinks it back the same way.</summary>
internal sealed class SelectionExpander(TextArea area, ISyntaxFeatures features)
{
    private readonly Stack<EditorSelection> history = new();
    private IReadOnlyList<TextSpan>? ranges;
    private EditorSelection? expanded;
    private TextSnapshot? rangesSnapshot;

    /// <summary>Selects the smallest span around the selection that is larger than it.</summary>
    public async Task ExpandAsync(CancellationToken cancellationToken = default)
    {
        var selection = area.Selection;
        var snapshot = area.Snapshot;
        if (expanded != selection || !ReferenceEquals(rangesSnapshot, snapshot))
        {
            history.Clear();
            ranges = null;
        }

        if (ranges is null)
        {
            var offset = selection.Start;
            IReadOnlyList<TextSpan>? found;
            try
            {
                found = await Task.Run(() => features.GetSelectionRangesAsync(area.Document, snapshot, offset, cancellationToken), cancellationToken);
            }
            catch (Exception exception) when (exception is OperationCanceledException or LanguageFeatureException)
            {
                return;
            }

            if (!ReferenceEquals(snapshot, area.Snapshot) || area.Selection != selection)
                return;

            ranges = [.. (found ?? Fallback(snapshot, selection.Span)).Where(span => span.End <= snapshot.Length).OrderBy(span => span.Length)];
            rangesSnapshot = snapshot;
        }

        var current = selection.Span;
        if (ranges.FirstOrDefault(span => span.Contains(current) && span.Length > current.Length) is not { Length: > 0 } next)
            return;

        history.Push(selection);
        expanded = new EditorSelection(next.Start, next.End);
        area.Select(expanded.Value);
    }

    /// <summary>Goes back to the selection before the last expand; returns whether there was one.</summary>
    public bool Shrink()
    {
        if (history.Count == 0 || expanded != area.Selection)
            return false;

        var previous = history.Pop();
        expanded = history.Count > 0 ? previous : null;
        area.Select(previous);
        return true;
    }

    /// <summary>The spans to grow through without a provider: the word, the text inside each pair of brackets around it and the pair itself,
    /// the line's text, the whole line and the whole document.</summary>
    internal static IReadOnlyList<TextSpan> Fallback(TextSnapshot snapshot, TextSpan span)
    {
        var spans = new List<TextSpan>();
        var word = WordBoundaries.GetWordAt(snapshot, span.Start);
        if (!word.IsEmpty)
            spans.Add(word);

        var line = snapshot.GetLineFromPosition(span.Start);
        var text = snapshot.GetLineText(line.LineNumber);
        var indent = text.Length - text.TrimStart().Length;
        var trimmed = TextSpan.FromBounds(line.Start + indent, line.Start + text.TrimEnd().Length);
        if (trimmed.Start <= trimmed.End)
            spans.Add(trimmed);
        spans.Add(TextSpan.FromBounds(line.Start, line.EndIncludingBreak));

        foreach (var (open, close) in EnclosingBrackets(snapshot, span))
        {
            spans.Add(TextSpan.FromBounds(open + 1, close));
            spans.Add(TextSpan.FromBounds(open, close + 1));
        }

        spans.Add(new TextSpan(0, snapshot.Length));
        return [.. spans.Distinct()];
    }

    private static IEnumerable<(int Open, int Close)> EnclosingBrackets(TextSnapshot snapshot, TextSpan span)
    {
        const int Limit = 200_000;
        var from = span.Start;
        var to = span.End;
        for (var level = 0; level < 32; level++)
        {
            var open = FindOpen(snapshot, from - 1, Math.Max(0, from - Limit));
            if (open < 0)
                yield break;

            var close = FindClose(snapshot, open, Math.Min(snapshot.Length, open + Limit));
            if (close < 0)
                yield break;

            if (close >= to)
                yield return (open, close);
            from = open;
            to = Math.Max(to, close + 1);
        }
    }

    private static int FindOpen(TextSnapshot snapshot, int start, int stop)
    {
        var depth = 0;
        for (var i = start; i >= stop; i--)
        {
            var c = snapshot[i];
            if (c is ')' or ']' or '}')
                depth++;
            else if (c is '(' or '[' or '{' && depth-- == 0)
                return i;
        }

        return -1;
    }

    private static int FindClose(TextSnapshot snapshot, int open, int stop)
    {
        var depth = 0;
        for (var i = open; i < stop; i++)
        {
            var c = snapshot[i];
            if (c is '(' or '[' or '{')
                depth++;
            else if (c is ')' or ']' or '}' && --depth == 0)
                return i;
        }

        return -1;
    }
}
