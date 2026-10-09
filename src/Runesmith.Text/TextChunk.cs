namespace Runesmith.Text;

/// <summary>A run of characters that pieces refer to, with the positions of its line breaks.</summary>
/// <remarks>Characters and line-break positions, once written, never change, so readers on any thread may use the part a piece covers.
/// Writable chunks grow at the end under <see cref="AddStore"/>'s lock.</remarks>
internal sealed class TextChunk
{
    private readonly char[]? _storage;
    private volatile int[] _newLines;
    private volatile int _newLineCount;
    private volatile int _length;

    private TextChunk(ReadOnlyMemory<char> memory, char[]? storage, int[] newLines, int newLineCount, int length)
    {
        Memory = memory;
        _storage = storage;
        _newLines = newLines;
        _newLineCount = newLineCount;
        _length = length;
    }

    /// <summary>Gets the chunk's characters; a writable chunk's memory runs past what is written.</summary>
    public ReadOnlyMemory<char> Memory { get; }

    /// <summary>Gets the number of characters written.</summary>
    public int Length => _length;

    /// <summary>Gets whether characters can still be appended.</summary>
    public bool IsWritable => _storage is not null;

    public int Capacity => _storage?.Length ?? _length;

    /// <summary>Creates a read-only chunk over <paramref name="text"/>, which uses \n line breaks only.</summary>
    public static TextChunk FromText(string text)
    {
        var newLines = new List<int>();
        var at = text.IndexOf('\n', StringComparison.Ordinal);
        while (at >= 0)
        {
            newLines.Add(at);
            at = text.IndexOf('\n', at + 1);
        }

        return new TextChunk(text.AsMemory(), null, [.. newLines], newLines.Count, text.Length);
    }

    public static TextChunk CreateWritable(int capacity)
    {
        var storage = new char[capacity];
        return new TextChunk(storage, storage, new int[64], 0, 0);
    }

    /// <summary>Appends <paramref name="text"/>, which must fit, and returns where it starts; only <see cref="AddStore"/> calls this, under its lock.</summary>
    public int Append(ReadOnlySpan<char> text)
    {
        var start = _length;
        text.CopyTo(_storage.AsSpan(start));
        var lines = _newLines;
        var count = _newLineCount;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n')
                continue;
            if (count == lines.Length)
            {
                var grown = new int[lines.Length * 2];
                Array.Copy(lines, grown, count);
                lines = grown;
                _newLines = lines;
            }

            lines[count++] = start + i;
        }

        _newLineCount = count;
        _length = start + text.Length;
        return start;
    }

    /// <summary>Counts the line breaks in [<paramref name="start"/>, <paramref name="end"/>).</summary>
    public int CountNewLines(int start, int end)
    {
        var count = _newLineCount;
        var lines = _newLines;
        return LowerBound(lines, count, end) - LowerBound(lines, count, start);
    }

    /// <summary>Gets the position of the line break <paramref name="index"/> breaks after the first one at or after <paramref name="start"/>.</summary>
    public int NewLineAt(int start, int index)
    {
        var count = _newLineCount;
        var lines = _newLines;
        return lines[LowerBound(lines, count, start) + index];
    }

    private static int LowerBound(int[] values, int count, int value)
    {
        int low = 0, high = count;
        while (low < high)
        {
            var middle = (low + high) >>> 1;
            if (values[middle] < value)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }
}

/// <summary>The append-only text that edits insert, shared by every snapshot of one buffer's history.</summary>
internal sealed class AddStore
{
    private const int ChunkSize = 64 * 1024;
    private readonly Lock _lock = new();
    private TextChunk _current = TextChunk.CreateWritable(ChunkSize);

    /// <summary>Stores <paramref name="text"/> and returns the piece that refers to it.</summary>
    public Piece Append(string text)
    {
        lock (_lock)
        {
            if (text.Length > ChunkSize / 4)
                return Piece.Create(TextChunk.FromText(text), 0, text.Length);
            if (_current.Capacity - _current.Length < text.Length)
                _current = TextChunk.CreateWritable(ChunkSize);
            var start = _current.Append(text);
            return Piece.Create(_current, start, text.Length);
        }
    }

    /// <summary>Extends <paramref name="piece"/> by <paramref name="text"/> when it ends where the store does, as it does while typing.</summary>
    public bool TryExtend(Piece piece, string text, out Piece extended)
    {
        lock (_lock)
        {
            var chunk = piece.Chunk;
            if (chunk == _current && piece.Start + piece.Length == chunk.Length && chunk.Capacity - chunk.Length >= text.Length)
            {
                chunk.Append(text);
                extended = Piece.Create(chunk, piece.Start, piece.Length + text.Length);
                return true;
            }
        }

        extended = default;
        return false;
    }
}
