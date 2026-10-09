namespace Runesmith.Text;

/// <summary>One step of an <see cref="UndoHistory"/>: the edits it undoes at once and the editor state around them.</summary>
public sealed class UndoEntry
{
    private readonly List<TextChangeSet> _changeSets;

    internal UndoEntry(TextChangeSet changeSet, object? stateBefore, object? stateAfter)
    {
        _changeSets = [changeSet];
        StateBefore = stateBefore;
        StateAfter = stateAfter;
    }

    /// <summary>Gets the editor state, such as the selections, before the first edit; restore it after undoing.</summary>
    public object? StateBefore { get; }

    /// <summary>Gets the editor state after the last edit; restore it after redoing.</summary>
    public object? StateAfter { get; private set; }

    /// <summary>Gets the edits, oldest first.</summary>
    public IReadOnlyList<TextChangeSet> ChangeSets => _changeSets;

    internal void Merge(TextChangeSet changeSet, object? stateAfter)
    {
        _changeSets.Add(changeSet);
        StateAfter = stateAfter;
    }
}

/// <summary>The undo and redo stacks of one buffer, with a save point that tells whether the text matches the file.</summary>
/// <remarks>
/// Undo and redo edit the buffer with <see cref="UndoTag"/> or <see cref="RedoTag"/>; code that pushes edits as they happen
/// should skip change sets with those tags.
/// </remarks>
public sealed class UndoHistory
{
    private readonly List<UndoEntry> _entries = [];
    private int _index;
    private int? _savePoint = 0;

    /// <summary>Creates a history that keeps the last <paramref name="capacity"/> steps.</summary>
    public UndoHistory(int capacity = 1000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
    }

    /// <summary>Gets the tag of the edits <see cref="Undo"/> makes.</summary>
    public static object UndoTag { get; } = new TagName("Undo");

    /// <summary>Gets the tag of the edits <see cref="Redo"/> makes.</summary>
    public static object RedoTag { get; } = new TagName("Redo");

    /// <summary>Gets the number of steps kept; the oldest go first.</summary>
    public int Capacity { get; }

    /// <summary>Gets whether there is a step to undo.</summary>
    public bool CanUndo => _index > 0;

    /// <summary>Gets whether there is a step to redo.</summary>
    public bool CanRedo => _index < _entries.Count;

    /// <summary>Gets whether the text is as it was at the last <see cref="MarkSavePoint"/>.</summary>
    public bool IsAtSavePoint => _savePoint == _index;

    /// <summary>Raised whenever <see cref="CanUndo"/>, <see cref="CanRedo"/> or <see cref="IsAtSavePoint"/> may have changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Records an edit and drops everything that could be redone.</summary>
    /// <param name="changeSet">The edit.</param>
    /// <param name="stateBefore">The editor state before the edit.</param>
    /// <param name="stateAfter">The editor state after the edit.</param>
    /// <param name="mergeWithPrevious">Whether to add the edit to the last step, as typing a word does; ignored right after a save point.</param>
    public void Push(TextChangeSet changeSet, object? stateBefore, object? stateAfter, bool mergeWithPrevious = false)
    {
        ArgumentNullException.ThrowIfNull(changeSet);
        if (changeSet.IsEmpty)
            return;

        if (CanRedo)
        {
            _entries.RemoveRange(_index, _entries.Count - _index);
            if (_savePoint > _index)
                _savePoint = null;
        }

        if (mergeWithPrevious && _index > 0 && _savePoint != _index)
        {
            _entries[_index - 1].Merge(changeSet, stateAfter);
        }
        else
        {
            _entries.Add(new UndoEntry(changeSet, stateBefore, stateAfter));
            _index++;
            if (_entries.Count > Capacity)
            {
                _entries.RemoveAt(0);
                _index--;
                _savePoint = _savePoint is > 0 ? _savePoint - 1 : null;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Undoes the last step in <paramref name="buffer"/> and returns it, or returns null when there is nothing to undo.</summary>
    public UndoEntry? Undo(TextBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (!CanUndo)
            return null;

        var entry = _entries[--_index];
        for (var i = entry.ChangeSets.Count - 1; i >= 0; i--)
            buffer.Apply(entry.ChangeSets[i].InverseChanges, UndoTag);
        Changed?.Invoke(this, EventArgs.Empty);
        return entry;
    }

    /// <summary>Redoes the last undone step in <paramref name="buffer"/> and returns it, or returns null when there is nothing to redo.</summary>
    public UndoEntry? Redo(TextBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (!CanRedo)
            return null;

        var entry = _entries[_index++];
        foreach (var changeSet in entry.ChangeSets)
            buffer.Apply(changeSet.Changes, RedoTag);
        Changed?.Invoke(this, EventArgs.Empty);
        return entry;
    }

    /// <summary>Forgets every step; the current text becomes the save point only if it already was.</summary>
    public void Clear()
    {
        var atSavePoint = IsAtSavePoint;
        _entries.Clear();
        _index = 0;
        _savePoint = atSavePoint ? 0 : null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Marks the current text as saved.</summary>
    public void MarkSavePoint()
    {
        _savePoint = _index;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed record TagName(string Name)
    {
        public override string ToString() => Name;
    }
}
