namespace Runesmith.Text;

/// <summary>The editable text of a document: a current <see cref="TextSnapshot"/> that each edit replaces.</summary>
/// <remarks>Edit a buffer from one thread, normally the UI thread; any thread may read the snapshots it hands out.</remarks>
public sealed class TextBuffer
{
    private volatile TextSnapshot _current;

    public TextBuffer(TextSnapshot initial) => _current = initial ?? throw new ArgumentNullException(nameof(initial));

    public TextBuffer(string text)
        : this(TextSnapshot.Create(text))
    {
    }

    /// <summary>Gets the current text.</summary>
    public TextSnapshot Current => _current;

    /// <summary>Raised after each edit that changed the text.</summary>
    public event EventHandler<TextChangedEventArgs>? Changed;

    /// <summary>Applies <paramref name="changes"/>, which refer to <see cref="Current"/> and must not overlap, and raises <see cref="Changed"/>.</summary>
    /// <param name="changes">The changes.</param>
    /// <param name="tag">Anything listeners should know about the edit, such as <see cref="UndoHistory.UndoTag"/>.</param>
    /// <returns>The edit; when nothing changes, an empty set, and <see cref="Changed"/> is not raised.</returns>
    public TextChangeSet Apply(IReadOnlyList<TextChange> changes, object? tag = null)
    {
        var before = _current;
        var after = before.ApplyChanges(changes, out var applied);
        var set = new TextChangeSet(before, after, applied, tag);
        if (set.IsEmpty)
            return set;
        _current = after;
        Changed?.Invoke(this, new TextChangedEventArgs(set));
        return set;
    }

    /// <summary>Inserts <paramref name="text"/> at <paramref name="position"/>.</summary>
    public TextChangeSet Insert(int position, string text, object? tag = null) => Apply([new TextChange(new TextSpan(position, 0), text)], tag);

    /// <summary>Deletes the text in <paramref name="span"/>.</summary>
    public TextChangeSet Delete(TextSpan span, object? tag = null) => Apply([new TextChange(span, string.Empty)], tag);

    /// <summary>Replaces the text in <paramref name="span"/> with <paramref name="text"/>.</summary>
    public TextChangeSet Replace(TextSpan span, string text, object? tag = null) => Apply([new TextChange(span, text)], tag);

    /// <summary>Replaces the whole text with <paramref name="text"/>.</summary>
    public TextChangeSet SetText(string text, object? tag = null) => Replace(new TextSpan(0, _current.Length), text, tag);
}
