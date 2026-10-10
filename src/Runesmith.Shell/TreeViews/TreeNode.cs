using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using Runesmith.Sdk.ToolWindows;

namespace Runesmith.Shell.TreeViews;

/// <summary>What a row of a tree view is.</summary>
internal enum TreeNodeKind
{
    Item,

    /// <summary>A row that says the children are loading.</summary>
    Loading,

    /// <summary>A row that says the children could not be loaded.</summary>
    Error,
}

/// <summary>An item of a tree view as Runesmith keeps it: the plugin's item, its place in the tree and its loaded children.</summary>
internal sealed class TreeNode(TreeItem item, TreeNode? parent, string key, TreeNodeKind kind = TreeNodeKind.Item) : INotifyPropertyChanged
{
    private TreeItem item = item;
    private bool isExpanded;

    public TreeItem Item
    {
        get => item;
        set
        {
            item = value;
            Changed(nameof(Item));
        }
    }

    public TreeNode? Parent { get; } = parent;

    /// <summary>Gets the id the node is known by across refreshes: the item's id, or its parent's key and its label.</summary>
    public string Key { get; } = key;

    public TreeNodeKind Kind { get; } = kind;

    public int Depth { get; } = parent is null ? 0 : parent.Depth + 1;

    /// <summary>Gets the loaded children, or null before they are loaded.</summary>
    public List<TreeNode>? Children { get; set; }

    /// <summary>Gets the indexes of the label's characters the filter matched, drawn instead of the item's highlights while filtering.</summary>
    public IReadOnlyList<int>? FilterMatches { get; set; }

    public bool HasChildren => Kind == TreeNodeKind.Item && item.CollapsibleState != TreeItemCollapsibleState.None;

    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (isExpanded == value)
                return;

            isExpanded = value;
            Changed(nameof(IsExpanded));
        }
    }

    /// <summary>Gets the load of the children in progress, cancelled when another starts.</summary>
    public CancellationTokenSource? Loading { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static string KeyOf(TreeItem item, TreeNode? parent) => item.Id ?? (parent is null ? "/" : parent.Key + "/") + item.Label;

    /// <summary>Raises a change of the row's look, such as after the filter changed its highlights.</summary>
    public void Changed(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}

/// <summary>The rows a tree view shows, flattened, that grows and shrinks by ranges so a large tree keeps its scroll position and only the
/// rows in view are drawn.</summary>
internal sealed class TreeRows : IReadOnlyList<TreeNode>, IList, INotifyCollectionChanged
{
    private readonly List<TreeNode> rows = [];

    public int Count => rows.Count;

    public TreeNode this[int index] => rows[index];

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public int IndexOf(TreeNode node) => rows.IndexOf(node);

    public void InsertRange(int index, IReadOnlyList<TreeNode> nodes)
    {
        if (nodes.Count == 0)
            return;

        rows.InsertRange(index, nodes);
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, nodes.ToList(), index));
    }

    public void RemoveRange(int index, int count)
    {
        if (count <= 0)
            return;

        var removed = rows.GetRange(index, count);
        rows.RemoveRange(index, count);
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removed, index));
    }

    public void Replace(int index, TreeNode node)
    {
        var old = rows[index];
        rows[index] = node;
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, node, old, index));
    }

    public void Reset(IEnumerable<TreeNode> nodes)
    {
        rows.Clear();
        rows.AddRange(nodes);
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public IEnumerator<TreeNode> GetEnumerator() => rows.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => rows.GetEnumerator();

    bool IList.IsFixedSize => false;

    bool IList.IsReadOnly => true;

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => rows;

    object? IList.this[int index]
    {
        get => rows[index];
        set => throw new NotSupportedException();
    }

    int IList.Add(object? value) => throw new NotSupportedException();

    void IList.Clear() => throw new NotSupportedException();

    bool IList.Contains(object? value) => value is TreeNode node && rows.Contains(node);

    int IList.IndexOf(object? value) => value is TreeNode node ? rows.IndexOf(node) : -1;

    void IList.Insert(int index, object? value) => throw new NotSupportedException();

    void IList.Remove(object? value) => throw new NotSupportedException();

    void IList.RemoveAt(int index) => throw new NotSupportedException();

    void ICollection.CopyTo(Array array, int index) => ((ICollection)rows).CopyTo(array, index);
}
