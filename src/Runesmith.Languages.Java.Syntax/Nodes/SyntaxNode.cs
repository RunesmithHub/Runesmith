using System.Collections;

namespace Runesmith.Languages.Java.Syntax;

/// <summary>A node of a Java syntax tree: immutable, with its span in the source and its children in source order.</summary>
/// <remarks>Children are kept in slots, which typed properties of each node read, so walking, searching and moving trees after an edit work
/// the same for every kind of node. An absent optional child is an empty slot, and an empty list is an empty slot too.</remarks>
public abstract class SyntaxNode
{
    private static readonly SyntaxNode?[] NoSlots = [];

    private SyntaxNode?[] slots;
    private int start;
    private int length;

    // Set by a shallow shift: the children have not moved yet and move by this much the first time they are read.
    private int pendingShift;

    private protected SyntaxNode(SourceSpan span, SyntaxNode?[]? slots = null)
    {
        this.slots = slots is { Length: > 0 } ? slots : NoSlots;
        var end = span.End;
        start = span.Start;

        // A node covers its children, also a missing child that sits before the token the node starts at.
        foreach (var slot in this.slots)
        {
            if (slot is null)
                continue;
            start = Math.Min(start, slot.start);
            end = Math.Max(end, slot.start + slot.length);
        }

        length = end - start;
    }

    public SourceSpan Span => new(start, length);

    public int Start => start;

    public int End => start + length;

    /// <summary>Gets whether the node stands for something missing from the source, which the parser assumed so it could go on.</summary>
    public virtual bool IsMissing => false;

    internal int SlotCount => slots.Length;

    internal SyntaxNode? GetSlot(int index) => Slots[index];

    private SyntaxNode?[] Slots => Volatile.Read(ref pendingShift) == 0 ? slots : ApplyPendingShift();

    private SyntaxNode?[] ApplyPendingShift()
    {
        lock (slots)
        {
            if (pendingShift == 0)
                return slots;

            var shifted = new SyntaxNode?[slots.Length];
            for (var i = 0; i < shifted.Length; i++)
                shifted[i] = slots[i]?.Shifted(pendingShift);
            slots = shifted;
            Volatile.Write(ref pendingShift, 0);
            return shifted;
        }
    }

    /// <summary>Gets the node's children in source order; the items of lists are children of the node itself.</summary>
    public IEnumerable<SyntaxNode> ChildNodes()
    {
        foreach (var slot in Slots)
        {
            if (slot is null)
                continue;

            if (slot is ISyntaxList list)
            {
                foreach (var item in list.Nodes)
                    yield return item;
            }
            else
            {
                yield return slot;
            }
        }
    }

    /// <summary>Gets the node and everything below it, in source order.</summary>
    public IEnumerable<SyntaxNode> DescendantNodesAndSelf()
    {
        var stack = new Stack<SyntaxNode>();
        stack.Push(this);
        var children = new List<SyntaxNode>();
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            children.Clear();
            children.AddRange(node.ChildNodes());
            for (var i = children.Count - 1; i >= 0; i--)
                stack.Push(children[i]);
        }
    }

    public override string ToString() => $"{GetType().Name} {Span}";

    private protected T Slot<T>(int index)
        where T : SyntaxNode => (T)Slots[index]!;

    private protected T? OptionalSlot<T>(int index)
        where T : SyntaxNode => Slots[index] as T;

    private protected SyntaxList<T> ListSlot<T>(int index)
        where T : SyntaxNode => Slots[index] as SyntaxList<T> ?? SyntaxList<T>.Empty;

    /// <summary>Gets the node moved by a number of characters; its children move when they are first read, so this costs one copy.</summary>
    internal SyntaxNode Shifted(int delta)
    {
        if (delta == 0 || (this is ISyntaxList && slots.Length == 0))
            return this;

        var current = Slots;
        var copy = (SyntaxNode)MemberwiseClone();
        copy.start += delta;
        copy.slots = current;
        copy.pendingShift = current.Length > 0 ? delta : 0;
        return copy;
    }

    /// <summary>Copies the spine from the node down to <paramref name="old"/>, putting <paramref name="replacement"/> in its place: nodes
    /// before it are kept, nodes after it are moved by the difference in length, and the nodes around it grow by it.</summary>
    internal SyntaxNode Replace(SyntaxNode old, SyntaxNode replacement)
    {
        if (ReferenceEquals(this, old))
            return replacement;

        if (End <= old.Start)
            return this;

        var delta = replacement.length - old.length;
        if (Start >= old.End)
            return Shifted(delta);

        var current = Slots;
        var copy = (SyntaxNode)MemberwiseClone();
        copy.length += delta;
        var replaced = new SyntaxNode?[current.Length];
        for (var i = 0; i < current.Length; i++)
            replaced[i] = current[i]?.Replace(old, replacement);
        copy.slots = replaced;
        return copy;
    }
}

/// <summary>A list of nodes, such as the statements of a block.</summary>
internal interface ISyntaxList
{
    IEnumerable<SyntaxNode> Nodes { get; }
}

/// <summary>A list of nodes of one kind, spanning from its first item to its last.</summary>
public sealed class SyntaxList<T> : SyntaxNode, IReadOnlyList<T>, ISyntaxList
    where T : SyntaxNode
{
    internal SyntaxList(IReadOnlyList<T> items)
        : base(items.Count == 0 ? default : SourceSpan.FromBounds(items[0].Start, items[^1].End), [.. items])
    {
    }

    private SyntaxList()
        : base(default)
    {
    }

    /// <summary>Gets the empty list.</summary>
    internal static SyntaxList<T> Empty { get; } = new();

    public int Count => SlotCount;

    public T this[int index] => (T)GetSlot(index)!;

    IEnumerable<SyntaxNode> ISyntaxList.Nodes => this;

    public IEnumerator<T> GetEnumerator()
    {
        for (var i = 0; i < Count; i++)
            yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
