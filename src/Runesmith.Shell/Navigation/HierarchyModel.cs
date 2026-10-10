using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Runesmith.Sdk.Languages;

namespace Runesmith.Shell.Navigation;

/// <summary>Which way a hierarchy grows from its root.</summary>
public enum HierarchyDirection
{
    /// <summary>The functions that call the root.</summary>
    Callers,

    /// <summary>The functions the root calls.</summary>
    Callees,

    /// <summary>The types the root derives from or implements.</summary>
    Supertypes,

    /// <summary>The types that derive from or implement the root.</summary>
    Subtypes,
}

/// <summary>A row of the Hierarchy panel: a function or type, a row that says the level is loading or empty, or why it failed. Its children
/// load when it first opens.</summary>
public sealed class HierarchyNode : INotifyPropertyChanged
{
    private readonly HierarchyModel? model;
    private bool isExpanded;
    private Task? loading;

    internal HierarchyNode(HierarchyModel? model, HierarchyItem? item, IReadOnlyList<DocumentLocation> callSites, string? message = null, int depth = 0)
    {
        this.model = model;
        Item = item;
        CallSites = callSites;
        Message = message;
        Depth = depth;
        if (item is not null && model is not null)
            Children.Add(new HierarchyNode(null, null, [], "Loading..."));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the function or type, or null for a row that only shows <see cref="Message"/>.</summary>
    public HierarchyItem? Item { get; }

    /// <summary>Gets where the calls between this row and its parent are, for call hierarchies.</summary>
    public IReadOnlyList<DocumentLocation> CallSites { get; }

    public string? Message { get; }

    public int Depth { get; }

    public ObservableCollection<HierarchyNode> Children { get; } = [];

    /// <summary>Gets where opening the row goes: the first call site, or the item's name.</summary>
    public DocumentLocation? Target => CallSites.Count > 0 ? CallSites[0] : Item is { } item ? item.SelectionLocation ?? item.Location : null;

    /// <summary>Gets or sets whether the row is open; opening it the first time loads its children.</summary>
    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (isExpanded == value)
                return;

            isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            if (value)
                _ = LoadChildrenAsync();
        }
    }

    /// <summary>Loads the children once.</summary>
    public Task LoadChildrenAsync() => model is null || Item is null ? Task.CompletedTask : loading ??= model.LoadAsync(this);

    public override string ToString() => Item?.Name ?? Message ?? "";
}

/// <summary>The Hierarchy panel's tree: a function's callers or callees, or a type's base or derived types, each level asked for when it
/// opens.</summary>
public sealed class HierarchyModel(INavigationFeatures features)
{
    /// <summary>Rows deeper than this do not open, so a recursive call cannot grow the tree for ever.</summary>
    public const int MaxDepth = 32;

    private IReadOnlyList<HierarchyItem> roots = [];

    public ObservableCollection<HierarchyNode> Roots { get; } = [];

    /// <summary>Gets whether the tree shows calls rather than types.</summary>
    public bool IsCallHierarchy { get; private set; }

    public HierarchyDirection Direction { get; private set; }

    /// <summary>Gets what the tree is about, such as "Callers of Main".</summary>
    public string Title => roots.Count == 0 ? "" : $"{Direction switch
    {
        HierarchyDirection.Callers => "Callers of",
        HierarchyDirection.Callees => "Calls from",
        HierarchyDirection.Supertypes => "Supertypes of",
        _ => "Subtypes of",
    }} {roots[0].Name}";

    /// <summary>Raised when the tree starts over, such as for another root or direction.</summary>
    public event EventHandler? Changed;

    /// <summary>Shows a call hierarchy from the items a provider prepared.</summary>
    public void ShowCalls(IReadOnlyList<HierarchyItem> items, HierarchyDirection direction = HierarchyDirection.Callers) => Show(items, true, direction);

    /// <summary>Shows a type hierarchy from the items a provider prepared.</summary>
    public void ShowTypes(IReadOnlyList<HierarchyItem> items, HierarchyDirection direction = HierarchyDirection.Subtypes) => Show(items, false, direction);

    /// <summary>Grows the tree the other way from the same root.</summary>
    public void SetDirection(HierarchyDirection direction)
    {
        if (direction != Direction)
            Show(roots, IsCallHierarchy, direction);
    }

    internal async Task LoadAsync(HierarchyNode node)
    {
        var item = node.Item!;
        List<HierarchyNode> children;
        try
        {
            children = node.Depth >= MaxDepth
                ? [new HierarchyNode(null, null, [], "The hierarchy is too deep to show more.")]
                : await ChildrenAsync(item, node.Depth + 1);
        }
        catch (LanguageFeatureException exception)
        {
            children = [new HierarchyNode(null, null, [], exception.Message)];
        }
        catch (OperationCanceledException)
        {
            children = [];
        }

        if (children.Count == 0)
            children.Add(new HierarchyNode(null, null, [], Empty()));

        node.Children.Clear();
        foreach (var child in children)
            node.Children.Add(child);
    }

    private void Show(IReadOnlyList<HierarchyItem> items, bool calls, HierarchyDirection direction)
    {
        roots = items;
        IsCallHierarchy = calls;
        Direction = direction;
        Roots.Clear();
        foreach (var item in items)
            Roots.Add(new HierarchyNode(this, item, []));
        Changed?.Invoke(this, EventArgs.Empty);
        foreach (var root in Roots)
            root.IsExpanded = true;
    }

    private async Task<List<HierarchyNode>> ChildrenAsync(HierarchyItem item, int depth)
    {
        switch (Direction)
        {
            case HierarchyDirection.Callers or HierarchyDirection.Callees:
                var calls = await Task.Run(() => Direction == HierarchyDirection.Callers
                    ? features.GetIncomingCallsAsync(item, CancellationToken.None)
                    : features.GetOutgoingCallsAsync(item, CancellationToken.None));
                return [.. calls.Select(call => new HierarchyNode(this, call.Item, call.CallSites, depth: depth))];
            default:
                var types = await Task.Run(() => Direction == HierarchyDirection.Supertypes
                    ? features.GetSupertypesAsync(item, CancellationToken.None)
                    : features.GetSubtypesAsync(item, CancellationToken.None));
                return [.. types.Select(type => new HierarchyNode(this, type, [], depth: depth))];
        }
    }

    private string Empty() => Direction switch
    {
        HierarchyDirection.Callers => "No callers",
        HierarchyDirection.Callees => "No calls",
        HierarchyDirection.Supertypes => "No supertypes",
        _ => "No subtypes",
    };
}
