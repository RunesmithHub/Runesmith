using Runesmith.Text;

namespace Runesmith.Sdk.ToolWindows;

/// <summary>Whether a tree item has children, and whether they show at first.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
public enum TreeItemCollapsibleState
{
    /// <summary>The item has no children.</summary>
    None,

    /// <summary>The item has children, which load when the user expands it.</summary>
    Collapsed,

    /// <summary>The item has children, which load and show at once.</summary>
    Expanded,
}

/// <summary>A row of a tree view.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
/// <param name="Label">The row's text.</param>
public sealed record TreeItem(string Label)
{
    /// <summary>Gets an id that stays the same when the tree is refreshed, so the item keeps its expanded state and selection. Without one,
    /// the item is known by its parent and its label.</summary>
    public string? Id { get; init; }

    /// <summary>Gets parts of the label drawn highlighted, such as the characters a search matched.</summary>
    public IReadOnlyList<TextSpan> Highlights { get; init; } = [];

    /// <summary>Gets a short text after the label, dimmed, such as a count or a path.</summary>
    public string? Description { get; init; }

    /// <summary>Gets the text shown when the pointer rests on the row, or null for none.</summary>
    public string? ToolTip { get; init; }

    /// <summary>Gets the name of the row's icon, such as <c>git-branch</c>; see HammerUI's <c>Icons.Find</c>.</summary>
    public string? Icon { get; init; }

    /// <summary>Gets a file or folder path whose icon, from the file icon theme, the row shows when it has no <see cref="Icon"/>; an item that
    /// has children gets the folder's icon.</summary>
    public string? IconPath { get; init; }

    /// <summary>Gets whether the item has children and whether they show at first.</summary>
    public TreeItemCollapsibleState CollapsibleState { get; init; }

    /// <summary>Gets a value commands read from <see cref="TreeViewTarget.ContextValue"/> to decide whether they apply to the item, such as
    /// <c>file</c> or <c>branch-remote</c>, so its context menu shows only the commands for its kind.</summary>
    public string? ContextValue { get; init; }

    /// <summary>Gets the command run when the item is opened with a double-click or Enter.</summary>
    public string? CommandId { get; init; }

    /// <summary>Gets the argument <see cref="CommandId"/> runs with; by default a <see cref="TreeViewTarget"/> for the item.</summary>
    public object? CommandArgument { get; init; }

    /// <summary>Gets whether the row has a checkbox and whether it is checked; null for no checkbox.</summary>
    public bool? IsChecked { get; init; }

    /// <summary>Gets data of the plugin's own, such as the object the item stands for.</summary>
    public object? Data { get; init; }
}

/// <summary>Describes a tree view: the tool window it shows in and how it behaves.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
/// <param name="ToolWindow">The tool window the tree shows in; its id is the tree view's id.</param>
public sealed record TreeViewDefinition(ToolWindowDefinition ToolWindow)
{
    /// <summary>Gets the tree view's id, which is its tool window's.</summary>
    public string Id => ToolWindow.Id;

    /// <summary>Gets the text shown while the tree has no items, such as "No to-dos in this folder".</summary>
    public string? EmptyMessage { get; init; }

    /// <summary>Gets whether the user can select several items with Ctrl and Shift.</summary>
    public bool CanSelectMany { get; init; }

    /// <summary>Gets whether a box above the tree filters the loaded items as the user types; on by default.</summary>
    public bool ShowFilter { get; init; } = true;
}

/// <summary>Supplies the items of a tree view, which Runesmith shows in a tool window of its own. Export it with
/// <c>[Export(typeof(ITreeViewProvider))]</c>.</summary>
/// <remarks>Runesmith asks for an item's children when the item is first expanded, on the UI thread, and draws only the rows in view, so a
/// tree can be large. An exception from the provider shows as a row that says the items could not be loaded, and goes to the
/// <b>Plugins</b> output. Added in plugin API 0.1.2.</remarks>
public interface ITreeViewProvider
{
    TreeViewDefinition Definition { get; }

    /// <summary>Gets the children of an item, or the top items when <paramref name="parent"/> is null.</summary>
    Task<IReadOnlyList<TreeItem>> GetChildrenAsync(TreeItem? parent, CancellationToken cancellationToken);

    /// <summary>Gets an item's parent, or null for a top item; <see cref="ITreeView.RevealAsync"/> uses it to find an item that has not been
    /// loaded yet. Without it, only loaded items can be revealed.</summary>
    Task<TreeItem?> GetParentAsync(TreeItem item, CancellationToken cancellationToken) => Task.FromResult<TreeItem?>(null);
}

/// <summary>A tree view as it shows: its selection, and what refreshes and reveals its items. Get it from
/// <see cref="ITreeViewService.GetTreeView"/>.</summary>
/// <remarks>Its members can be called from any thread; its events are raised on the UI thread. Added in plugin API 0.1.2.</remarks>
public interface ITreeView
{
    /// <summary>Gets the tree view's id.</summary>
    string Id { get; }

    /// <summary>Gets the selected items.</summary>
    IReadOnlyList<TreeItem> Selection { get; }

    /// <summary>Gets or sets a message shown above the items, such as "Showing the first 500 results", or null for none.</summary>
    string? Message { get; set; }

    /// <summary>Loads the whole tree again, keeping expanded items expanded; with an item, replaces that item, matched by its id, with the one
    /// given and loads its children again.</summary>
    void Refresh(TreeItem? item = null);

    /// <summary>Shows an item: expands its parents, scrolls to it and, as asked, selects it, focuses the tree and expands the item. The
    /// tool window is shown when it is hidden.</summary>
    /// <returns>Whether the item was found.</returns>
    Task<bool> RevealAsync(TreeItem item, bool select = true, bool focus = false, bool expand = false);

    /// <summary>Raised when the selection changes.</summary>
    event EventHandler? SelectionChanged;

    /// <summary>Raised when the user checks or unchecks an item's checkbox.</summary>
    event EventHandler<TreeItemCheckedEventArgs>? ItemChecked;
}

/// <summary>An item whose checkbox the user changed.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
public sealed class TreeItemCheckedEventArgs(TreeItem item, bool isChecked) : EventArgs
{
    /// <summary>Gets the item as it is now, with <see cref="TreeItem.IsChecked"/> set.</summary>
    public TreeItem Item { get; } = item;

    public bool IsChecked { get; } = isChecked;
}

/// <summary>Gives a plugin the tree views it exports. Import it.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
public interface ITreeViewService
{
    /// <summary>Gets a tree view by id; a plugin can get only the tree views it exports.</summary>
    /// <exception cref="KeyNotFoundException">There is no tree view with this id.</exception>
    /// <exception cref="UnauthorizedAccessException">The tree view belongs to another plugin.</exception>
    ITreeView GetTreeView(string id);
}

/// <summary>What a tree view's command was run on: an item, from its context menu or when it is opened, or the view itself, from its
/// title.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
/// <param name="ViewId">The tree view's id.</param>
/// <param name="Item">The item, or null for a command from the view's title.</param>
public sealed record TreeViewTarget(string ViewId, TreeItem? Item)
{
    /// <summary>Gets the selected items; the item is one of them when the menu opened on the selection.</summary>
    public IReadOnlyList<TreeItem> Selection { get; init; } = [];

    /// <summary>Gets the item's <see cref="TreeItem.ContextValue"/>, or null.</summary>
    public string? ContextValue => Item?.ContextValue;
}

/// <summary>The menus of tree views, to place commands in with a <c>MenuItemDefinition</c>. Their commands run with a
/// <see cref="TreeViewTarget"/>, and show only while they can run with it.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
public static class TreeViewMenus
{
    /// <summary>Gets the menu of buttons in a tree view's title, shown with their commands' icons.</summary>
    public static string Title(string viewId) => "#tree-title:" + viewId;

    /// <summary>Gets the context menu of a tree view's items.</summary>
    public static string Item(string viewId) => "#tree-item:" + viewId;
}
