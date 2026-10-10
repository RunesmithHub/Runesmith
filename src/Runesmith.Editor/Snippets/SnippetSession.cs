using Runesmith.Sdk.Documents.Snippets;
using Runesmith.Text;

namespace Runesmith.Editor.Snippets;

/// <summary>A place of a tab stop in the text, moved with the edits around it.</summary>
internal sealed class SnippetRange(TextSpan span, SnippetTransform? transform)
{
    public TextSpan Span { get; set; } = span;

    /// <summary>Gets how this place's text is made from the tab stop's text, for a transformed mirror.</summary>
    public SnippetTransform? Transform { get; } = transform;
}

/// <summary>The places of one tab stop number; typing in one changes the others.</summary>
internal sealed class SnippetGroup(int index, IReadOnlyList<SnippetRange> ranges, IReadOnlyList<string>? choices)
{
    public int Index { get; } = index;

    public IReadOnlyList<SnippetRange> Ranges { get; } = ranges;

    public IReadOnlyList<string>? Choices { get; } = choices;

    /// <summary>Gets the place the caret goes to: the first one that is not a transformed mirror.</summary>
    public SnippetRange Primary => Ranges.FirstOrDefault(r => r.Transform is null) ?? Ranges[0];
}

/// <summary>Where the caret goes after a move between tab stops.</summary>
/// <param name="Span">The span to select.</param>
/// <param name="Choices">Choices to offer at the tab stop, if it has any.</param>
/// <param name="IsFinal">Whether this is the final position, which ends the snippet.</param>
internal readonly record struct SnippetTarget(TextSpan Span, IReadOnlyList<string>? Choices, bool IsFinal);

/// <summary>An inserted snippet while the user moves between its tab stops: keeps the tab stops in step with edits and makes typing in one
/// place of a tab stop change its other places.</summary>
internal sealed class SnippetSession
{
    private readonly List<SnippetGroup> groups;
    private readonly SnippetRange whole;
    private readonly SnippetRange? final;
    private int current = -1;

    /// <param name="start">Where the expansion's text starts in the document.</param>
    public SnippetSession(SnippetExpansion expansion, int start)
    {
        whole = new SnippetRange(new TextSpan(start, expansion.Text.Length), null);
        groups = [.. expansion.Stops
            .Where(s => s.Index != 0)
            .GroupBy(s => s.Index)
            .OrderBy(g => g.Key)
            .Select(g => new SnippetGroup(g.Key, [.. g.Select(s => new SnippetRange(new TextSpan(start + s.Span.Start, s.Span.Length), s.Transform))],
                g.Select(s => s.Choices).FirstOrDefault(c => c is not null)))];
        if (expansion.Stops.FirstOrDefault(s => s.Index == 0) is { } end)
            final = new SnippetRange(new TextSpan(start + end.Span.Start, end.Span.Length), null);
    }

    /// <summary>Gets the whole snippet's text as it is now.</summary>
    public TextSpan Whole => whole.Span;

    /// <summary>Gets the tab stop the caret is at, or null before the first move.</summary>
    public SnippetGroup? Current => current >= 0 && current < groups.Count ? groups[current] : null;

    /// <summary>Gets the tab stops in order of their numbers.</summary>
    public IReadOnlyList<SnippetGroup> Groups => groups;

    /// <summary>Whether an offset is in the snippet, its ends included.</summary>
    public bool Contains(int offset) => offset >= whole.Span.Start && offset <= whole.Span.End;

    /// <summary>Moves to the next tab stop, or the one before; past the last one it goes to the final position.</summary>
    public SnippetTarget Move(int direction)
    {
        current = Math.Clamp(current + Math.Sign(direction), 0, groups.Count);
        if (current < groups.Count)
        {
            var group = groups[current];
            return new SnippetTarget(group.Primary.Span, group.Choices, IsFinal: false);
        }

        var span = final?.Span ?? new TextSpan(whole.Span.End, 0);
        return new SnippetTarget(new TextSpan(span.End, 0), null, IsFinal: true);
    }

    /// <summary>Adds the edits that keep the current tab stop's other places the same as the one being typed in, when every change is inside
    /// one place.</summary>
    /// <param name="changes">The edit, sorted, in the coordinates of <paramref name="before"/>.</param>
    /// <returns>The changes with the other places' changes, and how far those move the edited place; or null when nothing is linked.</returns>
    public (IReadOnlyList<TextChange> Changes, int ShiftBefore)? Mirror(IReadOnlyList<TextChange> changes, TextSnapshot before)
    {
        if (Current is not { Ranges.Count: > 1 } group || changes.Count == 0)
            return null;

        var source = group.Ranges.FirstOrDefault(r => r.Transform is null && changes.All(c => c.Span.Start >= r.Span.Start && c.Span.End <= r.Span.End));
        if (source is null)
            return null;

        var text = before.GetText(source.Span);
        for (var i = changes.Count - 1; i >= 0; i--)
        {
            var change = changes[i];
            var relative = change.Span.Start - source.Span.Start;
            text = string.Concat(text.AsSpan(0, relative), change.NewText, text.AsSpan(relative + change.Span.Length));
        }

        var all = new List<TextChange>(changes);
        var shift = 0;
        foreach (var range in group.Ranges)
        {
            if (ReferenceEquals(range, source) || range.Span.IntersectsWith(source.Span) && !(range.Span.End == source.Span.Start || range.Span.Start == source.Span.End))
                continue;

            var wanted = range.Transform?.Apply(text) ?? text;
            if (before.GetText(range.Span) == wanted)
                continue;

            all.Add(new TextChange(range.Span, wanted));
            if (range.Span.End <= source.Span.Start)
                shift += wanted.Length - range.Span.Length;
        }

        all.Sort((a, b) => a.Span.Start.CompareTo(b.Span.Start));
        return (all, shift);
    }

    /// <summary>Moves the tab stops through an edit. Text typed at the edge of the current tab stop, or of a place that holds it, joins that
    /// place; at the edge of any other place it stays outside.</summary>
    public void Map(IReadOnlyList<TextChange> changes)
    {
        var active = Current?.Ranges ?? [];
        var ranges = groups.SelectMany(g => g.Ranges).Append(whole);
        if (final is not null)
            ranges = ranges.Append(final);
        var all = ranges.ToList();
        for (var i = changes.Count - 1; i >= 0; i--)
        {
            var change = changes[i];
            foreach (var range in all)
            {
                var grows = ReferenceEquals(range, whole) || active.Any(a => range.Span.Contains(a.Span) && a.Span.Start <= change.Span.Start && change.Span.End <= a.Span.End);
                range.Span = MapSpan(range.Span, change, grows);
            }
        }
    }

    private static TextSpan MapSpan(TextSpan span, TextChange change, bool grows)
    {
        var delta = change.NewText.Length - change.Span.Length;
        var inside = change.Span.Start >= span.Start && change.Span.End <= span.End;
        var atEdge = change.Span.Length == 0 && (change.Span.Start == span.Start || change.Span.Start == span.End);
        if (inside && (!atEdge || grows))
            return new TextSpan(span.Start, span.Length + delta);
        if (change.Span.End <= span.Start)
            return new TextSpan(span.Start + delta, span.Length);
        if (change.Span.Start >= span.End)
            return span;

        var replacedEnd = change.Span.Start + change.NewText.Length;
        var start = change.Span.Start <= span.Start ? replacedEnd : span.Start;
        var end = change.Span.End >= span.End ? replacedEnd : span.End + delta;
        return TextSpan.FromBounds(start, Math.Max(start, end));
    }
}
