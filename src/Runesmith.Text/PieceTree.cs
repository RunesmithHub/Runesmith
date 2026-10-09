namespace Runesmith.Text;

/// <summary>A run of one chunk's characters: <see cref="Length"/> characters from <see cref="Start"/>, with <see cref="LineBreaks"/> line breaks.</summary>
internal readonly record struct Piece(TextChunk Chunk, int Start, int Length, int LineBreaks)
{
    public static Piece Create(TextChunk chunk, int start, int length) => new(chunk, start, length, chunk.CountNewLines(start, start + length));

    public ReadOnlyMemory<char> Text => Chunk.Memory.Slice(Start, Length);
}

/// <summary>An immutable node of a persistent AVL tree of pieces in text order; every node caches its subtree's length and line breaks.</summary>
internal sealed class PieceNode
{
    public PieceNode(PieceNode? left, Piece piece, PieceNode? right)
    {
        Left = left;
        Piece = piece;
        Right = right;
        Height = 1 + Math.Max(HeightOf(left), HeightOf(right));
        TotalLength = LengthOf(left) + piece.Length + LengthOf(right);
        TotalBreaks = BreaksOf(left) + piece.LineBreaks + BreaksOf(right);
    }

    public PieceNode? Left { get; }

    public Piece Piece { get; }

    public PieceNode? Right { get; }

    public int Height { get; }

    public int TotalLength { get; }

    public int TotalBreaks { get; }

    public static int HeightOf(PieceNode? node) => node?.Height ?? 0;

    public static int LengthOf(PieceNode? node) => node?.TotalLength ?? 0;

    public static int BreaksOf(PieceNode? node) => node?.TotalBreaks ?? 0;
}

/// <summary>Operations on the persistent piece tree; every operation returns new nodes and leaves its inputs as they were.</summary>
/// <remarks>The tree is rebalanced with join-based AVL algorithms, so split, join, insert and delete all take O(log n).</remarks>
internal static class PieceTree
{
    public static PieceNode? Insert(PieceNode? root, int offset, string text, AddStore store)
    {
        if (text.Length == 0)
            return root;

        var (left, right) = Split(root, offset);
        if (left is not null && store.TryExtend(Last(left), text, out var extended))
        {
            var (rest, _) = SplitLast(left);
            return Join(rest, extended, right);
        }

        return Join(left, store.Append(text), right);
    }

    public static PieceNode? Delete(PieceNode? root, TextSpan span)
    {
        if (span.Length == 0)
            return root;
        var (left, rest) = Split(root, span.Start);
        var (_, right) = Split(rest, span.Length);
        return Join(left, right);
    }

    /// <summary>Splits the text into its first <paramref name="offset"/> characters and the rest.</summary>
    public static (PieceNode? Left, PieceNode? Right) Split(PieceNode? node, int offset)
    {
        if (node is null)
            return (null, null);

        var leftLength = PieceNode.LengthOf(node.Left);
        var piece = node.Piece;
        if (offset <= leftLength)
        {
            var (a, b) = Split(node.Left, offset);
            return (a, Join(b, piece, node.Right));
        }

        if (offset >= leftLength + piece.Length)
        {
            var (a, b) = Split(node.Right, offset - leftLength - piece.Length);
            return (Join(node.Left, piece, a), b);
        }

        var cut = offset - leftLength;
        var head = Piece.Create(piece.Chunk, piece.Start, cut);
        var tail = Piece.Create(piece.Chunk, piece.Start + cut, piece.Length - cut);
        return (Join(node.Left, head, null), Join(null, tail, node.Right));
    }

    /// <summary>Concatenates two trees.</summary>
    public static PieceNode? Join(PieceNode? left, PieceNode? right)
    {
        if (left is null)
            return right;
        if (right is null)
            return left;
        var (rest, last) = SplitLast(left);
        return Join(rest, last, right);
    }

    /// <summary>Concatenates <paramref name="left"/>, <paramref name="piece"/> and <paramref name="right"/>, rebalancing as needed.</summary>
    public static PieceNode Join(PieceNode? left, Piece piece, PieceNode? right)
    {
        var lh = PieceNode.HeightOf(left);
        var rh = PieceNode.HeightOf(right);
        if (lh > rh + 1)
            return JoinRight(left!, piece, right);
        if (rh > lh + 1)
            return JoinLeft(left, piece, right!);
        return new PieceNode(left, piece, right);
    }

    private static PieceNode JoinRight(PieceNode left, Piece piece, PieceNode? right)
    {
        var inner = left.Right;
        if (PieceNode.HeightOf(inner) <= PieceNode.HeightOf(right) + 1)
        {
            var joined = new PieceNode(inner, piece, right);
            return joined.Height <= PieceNode.HeightOf(left.Left) + 1
                ? new PieceNode(left.Left, left.Piece, joined)
                : RotateLeft(new PieceNode(left.Left, left.Piece, RotateRight(joined)));
        }

        var deeper = JoinRight(inner!, piece, right);
        var result = new PieceNode(left.Left, left.Piece, deeper);
        return deeper.Height <= PieceNode.HeightOf(left.Left) + 1 ? result : RotateLeft(result);
    }

    private static PieceNode JoinLeft(PieceNode? left, Piece piece, PieceNode right)
    {
        var inner = right.Left;
        if (PieceNode.HeightOf(inner) <= PieceNode.HeightOf(left) + 1)
        {
            var joined = new PieceNode(left, piece, inner);
            return joined.Height <= PieceNode.HeightOf(right.Right) + 1
                ? new PieceNode(joined, right.Piece, right.Right)
                : RotateRight(new PieceNode(RotateLeft(joined), right.Piece, right.Right));
        }

        var deeper = JoinLeft(left, piece, inner!);
        var result = new PieceNode(deeper, right.Piece, right.Right);
        return deeper.Height <= PieceNode.HeightOf(right.Right) + 1 ? result : RotateRight(result);
    }

    private static PieceNode RotateLeft(PieceNode node)
    {
        var right = node.Right!;
        return new PieceNode(new PieceNode(node.Left, node.Piece, right.Left), right.Piece, right.Right);
    }

    private static PieceNode RotateRight(PieceNode node)
    {
        var left = node.Left!;
        return new PieceNode(left.Left, left.Piece, new PieceNode(left.Right, node.Piece, node.Right));
    }

    private static (PieceNode? Remainder, Piece Last) SplitLast(PieceNode node)
    {
        if (node.Right is null)
            return (node.Left, node.Piece);
        var (rest, last) = SplitLast(node.Right);
        return (Join(node.Left, node.Piece, rest), last);
    }

    private static Piece Last(PieceNode node)
    {
        while (node.Right is not null)
            node = node.Right;
        return node.Piece;
    }

    /// <summary>Gets the character at <paramref name="offset"/>.</summary>
    public static char CharAt(PieceNode root, int offset)
    {
        var node = root;
        while (true)
        {
            var leftLength = PieceNode.LengthOf(node.Left);
            if (offset < leftLength)
            {
                node = node.Left!;
                continue;
            }

            offset -= leftLength;
            if (offset < node.Piece.Length)
                return node.Piece.Chunk.Memory.Span[node.Piece.Start + offset];
            offset -= node.Piece.Length;
            node = node.Right!;
        }
    }

    /// <summary>Counts the line breaks before <paramref name="offset"/>.</summary>
    public static int BreaksBefore(PieceNode? node, int offset)
    {
        var breaks = 0;
        while (node is not null)
        {
            var leftLength = PieceNode.LengthOf(node.Left);
            if (offset <= leftLength)
            {
                node = node.Left;
                continue;
            }

            breaks += PieceNode.BreaksOf(node.Left);
            offset -= leftLength;
            var piece = node.Piece;
            if (offset <= piece.Length)
                return breaks + piece.Chunk.CountNewLines(piece.Start, piece.Start + offset);
            breaks += piece.LineBreaks;
            offset -= piece.Length;
            node = node.Right;
        }

        return breaks;
    }

    /// <summary>Gets the position of line break number <paramref name="index"/>, counted from zero.</summary>
    public static int OffsetOfBreak(PieceNode root, int index)
    {
        var node = root;
        var offset = 0;
        while (true)
        {
            var leftBreaks = PieceNode.BreaksOf(node.Left);
            if (index < leftBreaks)
            {
                node = node.Left!;
                continue;
            }

            index -= leftBreaks;
            offset += PieceNode.LengthOf(node.Left);
            var piece = node.Piece;
            if (index < piece.LineBreaks)
                return offset + piece.Chunk.NewLineAt(piece.Start, index) - piece.Start;
            index -= piece.LineBreaks;
            offset += piece.Length;
            node = node.Right!;
        }
    }

    /// <summary>Lists the pieces' text that falls in <paramref name="span"/>, in order.</summary>
    public static IEnumerable<ReadOnlyMemory<char>> Chunks(PieceNode? root, TextSpan span)
    {
        if (root is null || span.Length == 0)
            yield break;

        var stack = new Stack<(PieceNode Node, int Offset)>();
        var node = root;
        var offset = 0;
        while (stack.Count > 0 || node is not null)
        {
            while (node is not null)
            {
                var leftLength = PieceNode.LengthOf(node.Left);
                if (offset + leftLength + node.Piece.Length <= span.Start)
                {
                    offset += leftLength + node.Piece.Length;
                    node = node.Right;
                    continue;
                }

                stack.Push((node, offset));
                if (offset + leftLength <= span.Start)
                {
                    node = null;
                    break;
                }

                node = node.Left;
            }

            if (stack.Count == 0)
                yield break;

            var (current, currentOffset) = stack.Pop();
            var pieceStart = currentOffset + PieceNode.LengthOf(current.Left);
            if (pieceStart >= span.End)
                yield break;

            var from = Math.Max(span.Start, pieceStart) - pieceStart;
            var to = Math.Min(span.End, pieceStart + current.Piece.Length) - pieceStart;
            if (to > from)
                yield return current.Piece.Text[from..to];
            node = current.Right;
            offset = pieceStart + current.Piece.Length;
        }
    }
}
