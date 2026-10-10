using Avalonia.Threading;
using Runesmith.Sdk.ToolWindows;
using Runesmith.Workspace.Search;

namespace Runesmith.Shell.TreeViews;

/// <summary>The state of a plugin's tree view: its loaded items, the rows they show as, the filter and the selection. It loads an item's
/// children when the item is first expanded, keeps expanded items expanded when the tree is refreshed, and keeps the plugin's failures from
/// reaching the caller.</summary>
/// <remarks>Use its own members on the UI thread; the <see cref="ITreeView"/> members can be called from any thread.</remarks>
internal sealed class TreeViewModel : ITreeView, IDisposable
{
    private const int MaximumRevealDepth = 64;

    private readonly ITreeViewProvider provider;
    private readonly Action<string> log;
    private readonly Action<string> show;
    private List<TreeNode> roots = [];
    private CancellationTokenSource? rootLoad;
    private Task? firstLoad;
    private volatile IReadOnlyList<TreeItem> selection = [];
    private IReadOnlyList<TreeNode> selectedNodes = [];
    private string? message;
    private string filter = "";

    /// <param name="log">Writes a failure of the plugin to the Plugins output, under the plugin's name.</param>
    /// <param name="show">Shows the tool window, for <see cref="RevealAsync"/>.</param>
    public TreeViewModel(ITreeViewProvider provider, TreeViewDefinition definition, Action<string> log, Action<string>? show = null)
    {
        this.provider = provider;
        this.log = log;
        this.show = show ?? (_ => { });
        Definition = definition;
    }

    public TreeViewDefinition Definition { get; }

    public string Id => Definition.Id;

    /// <summary>Gets the rows shown, flattened.</summary>
    public TreeRows Rows { get; } = new();

    /// <summary>Gets the top items, once loaded.</summary>
    public IReadOnlyList<TreeNode> Roots => roots;

    /// <summary>Gets whether the top items are loading.</summary>
    public bool IsLoading { get; private set; }

    /// <summary>Gets why the top items could not be loaded, or null.</summary>
    public string? Error { get; private set; }

    /// <summary>Gets whether the tree loaded and has no items.</summary>
    public bool IsEmpty => firstLoad is { IsCompleted: true } && !IsLoading && roots.Count == 0 && Error is null;

    public string Filter => filter;

    public IReadOnlyList<TreeNode> SelectedNodes => selectedNodes;

    public IReadOnlyList<TreeItem> Selection => selection;

    public string? Message
    {
        get => message;
        set => UiThread.Run(() =>
        {
            message = value;
            StateChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public event EventHandler? SelectionChanged;

    public event EventHandler<TreeItemCheckedEventArgs>? ItemChecked;

    /// <summary>Raised when loading starts or ends, or the message changes.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Raised when a node should be scrolled to and selected, and with true when the tree should take the focus.</summary>
    public event EventHandler<(TreeNode Node, bool Focus)>? RevealRequested;

    public void Dispose()
    {
        rootLoad?.Cancel();
        rootLoad?.Dispose();
        rootLoad = null;
        foreach (var node in Loaded(roots))
            node.Loading?.Cancel();
    }

    /// <summary>Loads the top items the first time; later calls wait for that load.</summary>
    public Task LoadAsync() => firstLoad ??= ReloadAsync(null);

    public void Refresh(TreeItem? item = null) => UiThread.Run(() => _ = RefreshAsync(item));

    /// <summary>Loads the tree or one item's children again; see <see cref="Refresh"/>.</summary>
    public async Task RefreshAsync(TreeItem? item)
    {
        if (item is null)
        {
            if (firstLoad is null)
                await LoadAsync();
            else
                await ReloadAsync(null);
            return;
        }

        if (Find(item) is not { } node)
            return;

        node.Item = item;
        if (!node.HasChildren)
        {
            Collapse(node);
            node.Children = null;
            return;
        }

        if (node.IsExpanded)
            await ReloadAsync(node);
        else
            node.Children = null;
    }

    public Task<bool> RevealAsync(TreeItem item, bool select = true, bool focus = false, bool expand = false)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Dispatcher.UIThread.CheckAccess() ? RevealCoreAsync(item, select, focus, expand) : Dispatcher.UIThread.InvokeAsync(() => RevealCoreAsync(item, select, focus, expand));
    }

    /// <summary>Expands a node, loading its children the first time.</summary>
    public Task ExpandAsync(TreeNode node) => ExpandAsync(node, null);

    /// <summary>Collapses a node and removes the rows of its descendants.</summary>
    public void Collapse(TreeNode node)
    {
        if (!node.IsExpanded)
            return;

        node.IsExpanded = false;
        node.Loading?.Cancel();
        if (filter.Length > 0)
        {
            ApplyFilter();
            return;
        }

        var index = Rows.IndexOf(node);
        if (index < 0)
            return;

        var end = index + 1;
        while (end < Rows.Count && Rows[end].Depth > node.Depth)
            end++;
        Rows.RemoveRange(index + 1, end - index - 1);
    }

    public Task ToggleAsync(TreeNode node)
    {
        if (node.IsExpanded)
        {
            Collapse(node);
            return Task.CompletedTask;
        }

        return ExpandAsync(node);
    }

    /// <summary>Collapses every item.</summary>
    public void CollapseAll()
    {
        foreach (var node in Loaded(roots))
            node.IsExpanded = false;
        ApplyFilter();
    }

    /// <summary>Shows only the loaded items whose labels match a text, with the items that hold them.</summary>
    public void SetFilter(string text)
    {
        text = text.Trim();
        if (text == filter)
            return;

        filter = text;
        ApplyFilter();
    }

    /// <summary>Takes the selection the user made.</summary>
    public void SetSelection(IReadOnlyList<TreeNode> nodes)
    {
        var items = nodes.Where(n => n.Kind == TreeNodeKind.Item).ToList();
        if (items.SequenceEqual(selectedNodes))
            return;

        selectedNodes = items;
        selection = [.. items.Select(n => n.Item)];
        Raise(SelectionChanged, h => h(this, EventArgs.Empty));
    }

    /// <summary>Checks or unchecks an item's checkbox, as the user did.</summary>
    public void SetChecked(TreeNode node, bool value)
    {
        if (node.Item.IsChecked is null || node.Item.IsChecked == value)
            return;

        node.Item = node.Item with { IsChecked = value };
        var args = new TreeItemCheckedEventArgs(node.Item, value);
        Raise(ItemChecked, h => h(this, args));
    }

    /// <summary>Finds a loaded node for an item: by its id, or else by its key under its parent, or else as an equal item.</summary>
    public TreeNode? Find(TreeItem item)
    {
        var all = Loaded(roots).ToList();
        return item.Id is { } id
            ? all.FirstOrDefault(n => n.Item.Id == id)
            : all.FirstOrDefault(n => Equals(n.Item, item)) ?? all.FirstOrDefault(n => n.Key == TreeNode.KeyOf(item, n.Parent));
    }

    /// <summary>Gets the nodes whose children are loaded, and theirs, depth first.</summary>
    internal static IEnumerable<TreeNode> Loaded(IEnumerable<TreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.Kind != TreeNodeKind.Item)
                continue;

            yield return node;
            if (node.Children is not null)
            {
                foreach (var child in Loaded(node.Children))
                    yield return child;
            }
        }
    }

    private async Task ReloadAsync(TreeNode? parent)
    {
        var before = parent is null ? Loaded(roots).ToList() : Loaded(parent.Children ?? []).ToList();
        var known = before.Select(n => n.Key).ToHashSet(StringComparer.Ordinal);
        var expanded = before.Where(n => n.IsExpanded).Select(n => n.Key).ToHashSet(StringComparer.Ordinal);
        var restore = new Restore(known, expanded);
        var selectedKeys = selectedNodes.Select(n => n.Key).ToHashSet(StringComparer.Ordinal);
        if (parent is not null)
        {
            Collapse(parent);
            parent.Children = null;
            await ExpandAsync(parent, restore);
        }
        else
        {
            await ReloadRootsAsync(restore);
        }

        if (selectedKeys.Count > 0)
        {
            var reselected = Loaded(roots).Where(n => selectedKeys.Contains(n.Key)).ToList();
            if (!reselected.Select(n => n.Key).SequenceEqual(selectedNodes.Select(n => n.Key)))
                SetSelection(reselected);
            else
                selectedNodes = reselected;
        }
    }

    private async Task ReloadRootsAsync(Restore restore)
    {
        rootLoad?.Cancel();
        rootLoad?.Dispose();
        rootLoad = new CancellationTokenSource();
        var token = rootLoad.Token;
        IsLoading = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
        var loaded = await LoadChildrenAsync(null, token);
        if (token.IsCancellationRequested)
            return;

        foreach (var node in Loaded(roots))
            node.Loading?.Cancel();
        roots = loaded.Nodes;
        Error = loaded.Error;
        IsLoading = false;
        if (filter.Length > 0)
            ApplyFilter();
        else
            Rows.Reset(roots);
        StateChanged?.Invoke(this, EventArgs.Empty);
        await Task.WhenAll(roots.Where(restore.ShouldExpand).Select(n => ExpandAsync(n, restore)).ToList());
    }

    private async Task ExpandAsync(TreeNode node, Restore? restore)
    {
        if (!node.HasChildren)
            return;

        if (node.IsExpanded && node.Children is not null)
            return;

        node.IsExpanded = true;
        if (node.Children is null)
        {
            node.Loading?.Cancel();
            node.Loading?.Dispose();
            node.Loading = new CancellationTokenSource();
            var token = node.Loading.Token;
            var placeholder = new TreeNode(new TreeItem("Loading…"), node, node.Key + "/…", TreeNodeKind.Loading);
            ShowChildren(node, [placeholder]);
            var loaded = await LoadChildrenAsync(node, token);
            HideChildren(node);
            if (token.IsCancellationRequested || !node.IsExpanded)
            {
                node.IsExpanded = false;
                return;
            }

            node.Children = loaded.Error is { } problem
                ? [new TreeNode(new TreeItem($"Could not load the items: {problem}"), node, node.Key + "/!", TreeNodeKind.Error)]
                : loaded.Nodes;
        }

        ShowChildren(node, Visible(node.Children));
        restore ??= Restore.FromItems;
        await Task.WhenAll(node.Children.Where(restore.ShouldExpand).Select(n => ExpandAsync(n, restore)).ToList());
    }

    private async Task<(List<TreeNode> Nodes, string? Error)> LoadChildrenAsync(TreeNode? parent, CancellationToken token)
    {
        try
        {
            var items = await provider.GetChildrenAsync(parent?.Item, token) ?? [];
            var nodes = new List<TreeNode>(items.Count);
            foreach (var item in items)
            {
                if (item is not null)
                    nodes.Add(new TreeNode(item, parent, TreeNode.KeyOf(item, parent)));
            }

            return (nodes, null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return ([], null);
        }
        catch (Exception exception)
        {
            log($"the {Definition.ToolWindow.Title} tree could not load the {(parent is null ? "top items" : $"children of {parent.Item.Label}")}: {exception}");
            return ([], exception.Message);
        }
    }

    private async Task<bool> RevealCoreAsync(TreeItem item, bool select, bool focus, bool expand)
    {
        show(Id);
        await LoadAsync();
        var node = Find(item);
        if (node is null)
        {
            var chain = new List<TreeItem> { item };
            try
            {
                for (var parent = await provider.GetParentAsync(item, CancellationToken.None); parent is not null && chain.Count < MaximumRevealDepth;
                    parent = await provider.GetParentAsync(parent, CancellationToken.None))
                    chain.Add(parent);
            }
            catch (Exception exception)
            {
                log($"the {Definition.ToolWindow.Title} tree could not find the parent of {item.Label}: {exception}");
            }

            IReadOnlyList<TreeNode> level = roots;
            TreeNode? holder = null;
            for (var i = chain.Count - 1; i >= 0; i--)
            {
                var wanted = chain[i];
                var found = level.FirstOrDefault(n => wanted.Id is { } id ? n.Item.Id == id : n.Key == TreeNode.KeyOf(wanted, holder) || Equals(n.Item, wanted));
                if (found is null)
                    return false;

                holder = found;
                if (i > 0)
                {
                    await ExpandAsync(found);
                    level = found.Children ?? [];
                }
            }

            node = holder;
        }

        if (node is null)
            return false;

        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (!parent.IsExpanded)
                await ExpandAsync(parent);
        }

        if (expand)
            await ExpandAsync(node);
        if (select)
            SetSelection([node]);
        RevealRequested?.Invoke(this, (node, focus));
        return true;
    }

    private void ShowChildren(TreeNode node, IReadOnlyList<TreeNode> rows)
    {
        if (filter.Length > 0)
        {
            ApplyFilter();
            return;
        }

        var index = Rows.IndexOf(node);
        if (index >= 0)
            Rows.InsertRange(index + 1, rows);
    }

    private void HideChildren(TreeNode node)
    {
        var index = Rows.IndexOf(node);
        if (index < 0)
            return;

        var end = index + 1;
        while (end < Rows.Count && Rows[end].Depth > node.Depth)
            end++;
        Rows.RemoveRange(index + 1, end - index - 1);
    }

    private static List<TreeNode> Visible(IEnumerable<TreeNode> nodes)
    {
        var rows = new List<TreeNode>();
        foreach (var node in nodes)
        {
            rows.Add(node);
            if (node.IsExpanded && node.Children is not null)
                rows.AddRange(Visible(node.Children));
        }

        return rows;
    }

    private void ApplyFilter()
    {
        if (filter.Length == 0)
        {
            foreach (var node in Loaded(roots).Where(n => n.FilterMatches is not null))
            {
                node.FilterMatches = null;
                node.Changed(nameof(TreeNode.FilterMatches));
            }

            Rows.Reset(Visible(roots));
            return;
        }

        var shown = new List<TreeNode>();
        foreach (var root in roots)
            Collect(root, shown);
        Rows.Reset(shown);
    }

    private bool Collect(TreeNode node, List<TreeNode> shown)
    {
        if (node.Kind != TreeNodeKind.Item)
            return false;

        var score = FuzzyMatcher.Score(node.Item.Label, filter, out var matches);
        node.FilterMatches = score > 0 ? matches : [];
        node.Changed(nameof(TreeNode.FilterMatches));
        var start = shown.Count;
        shown.Add(node);
        var hasMatch = false;
        foreach (var child in node.Children ?? [])
            hasMatch |= Collect(child, shown);
        if (score > 0 || hasMatch)
            return true;

        shown.RemoveRange(start, shown.Count - start);
        return false;
    }

    private void Raise<T>(T? handlers, Action<T> invoke) where T : Delegate
    {
        if (handlers is null)
            return;

        foreach (var handler in handlers.GetInvocationList().Cast<T>())
        {
            try
            {
                invoke(handler);
            }
            catch (Exception exception)
            {
                log($"a handler of the {Definition.ToolWindow.Title} tree's events failed: {exception}");
            }
        }
    }

    /// <summary>Which items to expand after loading: the ones that were expanded before, and new ones the plugin says to expand.</summary>
    private sealed record Restore(HashSet<string> Known, HashSet<string> Expanded)
    {
        public static Restore FromItems { get; } = new([], []);

        public bool ShouldExpand(TreeNode node) =>
            node.HasChildren && (Expanded.Contains(node.Key) || !Known.Contains(node.Key) && node.Item.CollapsibleState == TreeItemCollapsibleState.Expanded);
    }
}
