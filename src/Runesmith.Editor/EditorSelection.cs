using Runesmith.Text;

namespace Runesmith.Editor;

/// <summary>The selection: where it started and where the caret is. Both are offsets in the text, and they are equal when nothing is
/// selected.</summary>
public readonly record struct EditorSelection(int Anchor, int Caret)
{
    public int Start => Math.Min(Anchor, Caret);

    public int End => Math.Max(Anchor, Caret);

    public bool IsEmpty => Anchor == Caret;

    public TextSpan Span => TextSpan.FromBounds(Start, End);

    /// <summary>An empty selection at an offset.</summary>
    public static EditorSelection At(int offset) => new(offset, offset);

    /// <summary>Moves the selection through the changes of an edit, as if the edit was made by someone else.</summary>
    public EditorSelection Through(TextChangeSet changeSet) =>
        new(OffsetMapping.Map(changeSet.Changes, Anchor), OffsetMapping.Map(changeSet.Changes, Caret));

    /// <summary>Keeps the selection within a text's length.</summary>
    public EditorSelection Clamp(int length) => new(Math.Clamp(Anchor, 0, length), Math.Clamp(Caret, 0, length));
}

/// <summary>Moves offsets through the changes of an edit.</summary>
public static class OffsetMapping
{
    /// <summary>Gets where an offset of the text before the changes ends up after them; an offset inside replaced text, or where text is
    /// inserted, moves to the end of the new text.</summary>
    /// <param name="changes">The changes, sorted by start, in the coordinates of the text before them.</param>
    public static int Map(IReadOnlyList<TextChange> changes, int offset)
    {
        var delta = 0;
        foreach (var change in changes)
        {
            if (offset < change.Span.Start)
                break;

            if (offset < change.Span.End)
                return change.Span.Start + delta + change.NewText.Length;

            delta += change.NewText.Length - change.Span.Length;
        }

        return offset + delta;
    }

    /// <summary>Moves a span through the changes; a span that was replaced entirely collapses to the end of the new text.</summary>
    public static TextSpan Map(IReadOnlyList<TextChange> changes, TextSpan span)
    {
        var start = Map(changes, span.Start);
        var end = Math.Max(start, Map(changes, span.End));
        return TextSpan.FromBounds(start, end);
    }
}
