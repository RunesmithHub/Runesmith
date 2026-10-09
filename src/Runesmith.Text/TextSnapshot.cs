namespace Runesmith.Text;

/// <summary>An immutable version of a text, stored as a piece table that shares structure with the versions before and after it.</summary>
/// <remarks>
/// The text always uses \n as its only line break: <see cref="Create(string)"/> and <see cref="Apply"/> normalize \r\n and \r. Snapshots
/// never change, so any thread may read them. Lookups by position or line take O(log n) in the number of edits.
/// </remarks>
public sealed class TextSnapshot
{
    private readonly PieceNode? _root;
    private readonly AddStore _store;

    private TextSnapshot(PieceNode? root, AddStore store, long version)
    {
        _root = root;
        _store = store;
        Version = version;
    }

    /// <summary>Gets an empty snapshot.</summary>
    public static TextSnapshot Empty { get; } = new(null, new AddStore(), 0);

    /// <summary>Gets the number of characters.</summary>
    public int Length => PieceNode.LengthOf(_root);

    /// <summary>Gets the number of lines; there is always at least one, and a text that ends with a line break ends with an empty line.</summary>
    public int LineCount => PieceNode.BreaksOf(_root) + 1;

    /// <summary>Gets the snapshot's version: 0 for a created snapshot, one more than its predecessor's after each <see cref="Apply"/>.</summary>
    public long Version { get; }

    /// <summary>Gets the character at <paramref name="position"/>.</summary>
    public char this[int position]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(position);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(position, Length);
            return PieceTree.CharAt(_root!, position);
        }
    }

    /// <summary>Creates a snapshot of <paramref name="text"/>, with its line breaks normalized to \n.</summary>
    public static TextSnapshot Create(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var normalized = LineEndings.Normalize(text);
        var root = normalized.Length == 0 ? null : new PieceNode(null, Piece.Create(TextChunk.FromText(normalized), 0, normalized.Length), null);
        return new TextSnapshot(root, new AddStore(), 0);
    }

    /// <summary>Creates a snapshot of <paramref name="text"/>, with its line breaks normalized to \n.</summary>
    public static TextSnapshot Create(ReadOnlySpan<char> text) => Create(new string(text));

    /// <summary>Gets the whole text.</summary>
    public string GetText() => GetText(new TextSpan(0, Length));

    /// <summary>Gets the text in <paramref name="span"/>.</summary>
    public string GetText(TextSpan span)
    {
        CheckSpan(span);
        if (span.Length == 0)
            return string.Empty;
        return string.Create(span.Length, (Snapshot: this, span.Start), static (destination, state) =>
            state.Snapshot.CopyTo(state.Start, destination, destination.Length));
    }

    /// <summary>Copies <paramref name="count"/> characters from <paramref name="sourceIndex"/> into <paramref name="destination"/>.</summary>
    public void CopyTo(int sourceIndex, Span<char> destination, int count)
    {
        CheckSpan(new TextSpan(sourceIndex, count));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, destination.Length);
        var written = 0;
        foreach (var chunk in PieceTree.Chunks(_root, new TextSpan(sourceIndex, count)))
        {
            chunk.Span.CopyTo(destination[written..]);
            written += chunk.Length;
        }
    }

    /// <summary>Lists the text in <paramref name="span"/> as consecutive pieces of memory, without copying it.</summary>
    public IEnumerable<ReadOnlyMemory<char>> GetChunks(TextSpan span)
    {
        CheckSpan(span);
        return PieceTree.Chunks(_root, span);
    }

    /// <summary>Gets line <paramref name="lineNumber"/>, counted from zero.</summary>
    public TextLine GetLine(int lineNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lineNumber);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(lineNumber, LineCount);
        var start = lineNumber == 0 ? 0 : PieceTree.OffsetOfBreak(_root!, lineNumber - 1) + 1;
        if (lineNumber == LineCount - 1)
            return new TextLine(lineNumber, start, Length - start, Length - start);
        var end = PieceTree.OffsetOfBreak(_root!, lineNumber);
        return new TextLine(lineNumber, start, end - start, end - start + 1);
    }

    /// <summary>Gets the line that contains <paramref name="position"/>; the end of the text belongs to the last line.</summary>
    public TextLine GetLineFromPosition(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, Length);
        return GetLine(PieceTree.BreaksBefore(_root, position));
    }

    /// <summary>Gets the text of line <paramref name="lineNumber"/> without its line break.</summary>
    public string GetLineText(int lineNumber) => GetText(GetLine(lineNumber).Span);

    /// <summary>Gets the line and column of <paramref name="offset"/>.</summary>
    public TextPosition GetPosition(int offset)
    {
        var line = GetLineFromPosition(offset);
        return new TextPosition(line.LineNumber, offset - line.Start);
    }

    /// <summary>Gets the offset of <paramref name="position"/>, moving a line or column past the end of the text or line back onto it.</summary>
    public int GetOffset(TextPosition position)
    {
        var line = GetLine(Math.Clamp(position.Line, 0, LineCount - 1));
        return line.Start + Math.Clamp(position.Column, 0, line.Length);
    }

    /// <summary>Finds <paramref name="c"/> in [<paramref name="start"/>, <paramref name="end"/>), returning its position or -1.</summary>
    public int IndexOf(char c, int start, int end)
    {
        var span = TextSpan.FromBounds(start, end);
        CheckSpan(span);
        var offset = start;
        foreach (var chunk in PieceTree.Chunks(_root, span))
        {
            var at = chunk.Span.IndexOf(c);
            if (at >= 0)
                return offset + at;
            offset += chunk.Length;
        }

        return -1;
    }

    /// <summary>Applies <paramref name="changes"/>, whose spans refer to this snapshot and must not overlap, and returns the new snapshot.</summary>
    /// <remarks>Inserted text has its line breaks normalized to \n.</remarks>
    public TextSnapshot Apply(IReadOnlyList<TextChange> changes) => ApplyChanges(changes, out _);

    internal TextSnapshot ApplyChanges(IReadOnlyList<TextChange> changes, out List<TextChange> applied)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var normalized = new List<TextChange>(changes.Count);
        foreach (var change in changes)
        {
            CheckSpan(change.Span);
            var text = LineEndings.Normalize(change.NewText ?? string.Empty);
            if (change.Span.Length > 0 || text.Length > 0)
                normalized.Add(new TextChange(change.Span, text));
        }

        applied = [.. normalized.OrderBy(change => change.Span.Start).ThenBy(change => change.Span.End)];
        for (var i = 1; i < applied.Count; i++)
        {
            if (applied[i - 1].Span.End > applied[i].Span.Start)
                throw new ArgumentException("Changes must not overlap.", nameof(changes));
        }

        if (applied.Count == 0)
            return this;

        var root = _root;
        for (var i = applied.Count - 1; i >= 0; i--)
        {
            var change = applied[i];
            root = PieceTree.Delete(root, change.Span);
            root = PieceTree.Insert(root, change.Span.Start, change.NewText, _store);
        }

        return new TextSnapshot(root, _store, Version + 1);
    }

    private void CheckSpan(TextSpan span)
    {
        if (span.Start < 0 || span.Length < 0 || span.End > Length)
            throw new ArgumentOutOfRangeException(nameof(span), $"The span {span.Start}..{span.End} is outside the text of {Length} characters.");
    }
}
