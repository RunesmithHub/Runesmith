namespace Runesmith.Text;

/// <summary>One edit of a buffer: the snapshots before and after it, and the changes that lead from one to the other.</summary>
public sealed class TextChangeSet
{
    internal TextChangeSet(TextSnapshot before, TextSnapshot after, IReadOnlyList<TextChange> changes, object? tag)
    {
        Before = before;
        After = after;
        Changes = changes;
        Tag = tag;
        var inverse = new TextChange[changes.Count];
        var delta = 0;
        for (var i = 0; i < changes.Count; i++)
        {
            var change = changes[i];
            inverse[i] = new TextChange(new TextSpan(change.Span.Start + delta, change.NewText.Length), before.GetText(change.Span));
            delta += change.NewText.Length - change.Span.Length;
        }

        InverseChanges = inverse;
    }

    /// <summary>Gets the snapshot the changes applied to.</summary>
    public TextSnapshot Before { get; }

    /// <summary>Gets the snapshot the changes produced.</summary>
    public TextSnapshot After { get; }

    /// <summary>Gets the changes, in <see cref="Before"/>'s positions, sorted by start, with inserted text normalized to \n.</summary>
    public IReadOnlyList<TextChange> Changes { get; }

    /// <summary>Gets the changes, in <see cref="After"/>'s positions, that turn <see cref="After"/> back into <see cref="Before"/>.</summary>
    public IReadOnlyList<TextChange> InverseChanges { get; }

    /// <summary>Gets what the editing code passed along, such as <see cref="UndoHistory.UndoTag"/>.</summary>
    public object? Tag { get; }

    /// <summary>Gets whether the set changes nothing.</summary>
    public bool IsEmpty => Changes.Count == 0;
}

/// <summary>The arguments of <see cref="TextBuffer.Changed"/>.</summary>
public sealed class TextChangedEventArgs(TextChangeSet changeSet) : EventArgs
{
    public TextChangeSet ChangeSet { get; } = changeSet;
}
