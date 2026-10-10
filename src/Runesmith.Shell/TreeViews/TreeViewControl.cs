using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.ToolWindows;
using Runesmith.Shell.Appearance;
using Runesmith.Shell.Commands;
using Runesmith.Shell.Palette;
using Runesmith.Shell.ToolWindows;

namespace Runesmith.Shell.TreeViews;

/// <summary>Shows a plugin's tree view: a filter box, the rows of the loaded items in a list that draws only the rows in view, the
/// plugin's title buttons and context menu, and a message while the tree is empty.</summary>
internal sealed class TreeViewControl : DockPanel, IToolWindowHeader
{
    private const double Indent = 14;

    private readonly TreeViewModel model;
    private readonly CommandService? commands;
    private readonly FileIcons? icons;
    private readonly ListBox list;
    private readonly SearchBox? filter;
    private readonly EmptyState empty;
    private readonly ProgressBar loading;
    private readonly Border message;
    private readonly TextBlock messageText;
    private readonly StackPanel actions;
    private bool isSyncing;

    public TreeViewControl(TreeViewModel model, CommandService? commands, FileIcons? icons)
    {
        this.model = model;
        this.commands = commands;
        this.icons = icons;
        var definition = model.Definition;

        list = new ListBox
        {
            ItemsSource = model.Rows,
            SelectionMode = definition.CanSelectMany ? SelectionMode.Multiple : SelectionMode.Single,
            Padding = new Thickness(4, 2),
            ItemTemplate = new FuncDataTemplate<TreeNode?>((node, _) => new TreeRowView(this), supportsRecycling: true),
        };
        list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(TemplatedControl.PaddingProperty, new Thickness(2, 0, 6, 0)),
                new Setter(MinHeightProperty, 26.0),
                new Setter(MarginProperty, new Thickness(0, 1)),
            },
        });
        AutomationProperties.SetName(list, definition.ToolWindow.Title);
        list.ContainerPrepared += (_, e) => Describe(e.Container);
        list.SelectionChanged += (_, _) =>
        {
            if (!isSyncing)
                model.SetSelection([.. list.SelectedItems?.OfType<TreeNode>() ?? []]);
            UpdateActions();
        };
        list.DoubleTapped += (_, e) =>
        {
            if (e.Source is Visual source && source.FindAncestorOfType<ToggleButton>(includeSelf: true) is null && NodeAt(source) is { } node)
                Open(node);
        };
        list.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);
        list.AddHandler(TextInputEvent, OnListTextInput, RoutingStrategies.Tunnel);
        list.AddHandler(PointerPressedEvent, OnListPointerPressed, RoutingStrategies.Tunnel);
        list.ContextRequested += OnContextRequested;
        list.ContextMenu = new ContextMenu();

        empty = new EmptyState { Icon = Icons.Find(definition.ToolWindow.Icon) ?? Icons.ListTree, Title = definition.EmptyMessage ?? "Nothing to show", IsVisible = false };
        loading = new ProgressBar { IsIndeterminate = true, Height = 2, MinHeight = 2, IsVisible = false, VerticalAlignment = VerticalAlignment.Top };
        messageText = new TextBlock { TextWrapping = TextWrapping.Wrap, Classes = { "caption" } };
        message = new Border { Padding = new Thickness(10, 6), Margin = new Thickness(6, 4, 6, 2), CornerRadius = new CornerRadius(6), Child = messageText, IsVisible = false };
        message[!Border.BackgroundProperty] = new DynamicResourceExtension("SurfaceSunkenBrush");
        AutomationProperties.SetLiveSetting(messageText, AutomationLiveSetting.Polite);

        if (definition.ShowFilter)
        {
            filter = new SearchBox { PlaceholderText = "Filter", Margin = new Thickness(6, 4, 6, 2) };
            AutomationProperties.SetName(filter, $"Filter {definition.ToolWindow.Title}");
            filter.PropertyChanged += (_, e) =>
            {
                if (e.Property == SearchBox.TextProperty)
                    model.SetFilter(filter.Text ?? "");
            };
            filter.AddHandler(KeyDownEvent, OnFilterKeyDown, RoutingStrategies.Tunnel);
            SetDock(filter, Dock.Top);
            Children.Add(filter);
        }

        SetDock(message, Dock.Top);
        Children.Add(message);
        Children.Add(new Panel { Children = { list, empty, loading } });

        actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        BuildActions();
        if (commands is not null)
            commands.Changed += (_, _) => BuildActions();

        model.StateChanged += (_, _) => UpdateState();
        model.Rows.CollectionChanged += (_, _) => UpdateState();
        model.RevealRequested += (_, e) => Reveal(e.Node, e.Focus);
        model.SelectionChanged += (_, _) => SyncSelection();
        if (icons is not null)
            icons.Changed += (_, _) => RefreshRows();
        UpdateState();
    }

    public event EventHandler? HeaderChanged
    {
        add { }
        remove { }
    }

    public string? HeaderTitle => null;

    public Control HeaderActions => actions;

    /// <summary>Gets the list of rows, for the tests.</summary>
    internal ListBox List => list;

    internal TreeViewModel Model => model;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _ = model.LoadAsync();
    }

    /// <summary>Runs an item's command, or expands or collapses it when it has none.</summary>
    internal void Open(TreeNode node)
    {
        if (node.Kind != TreeNodeKind.Item)
            return;

        if (node.Item.CommandId is { } id && commands is not null)
            _ = commands.ExecuteAsync(id, node.Item.CommandArgument ?? Target(node));
        else if (node.HasChildren)
            _ = model.ToggleAsync(node);
    }

    internal void Toggle(TreeNode node) => _ = model.ToggleAsync(node);

    internal void Check(TreeNode node, bool value) => model.SetChecked(node, value);

    internal Control? IconFor(TreeItem item, bool hasChildren)
    {
        if (HammerUI.Icons.Find(item.Icon) is { } named)
            return new SymbolIcon { Data = named, Size = 14, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };

        if (item.IconPath is { } path && icons is not null)
        {
            var symbol = new SymbolIcon { Size = 14, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            icons.For(path, hasChildren).ApplyTo(symbol, hasChildren ? "AccentBrush" : "TextSecondaryBrush");
            return symbol;
        }

        return null;
    }

    private TreeViewTarget Target(TreeNode? node) => new(model.Id, node?.Item) { Selection = model.Selection };

    private void UpdateState()
    {
        loading.IsVisible = model.IsLoading;
        message.IsVisible = model.Message is not null;
        messageText.Text = model.Message;
        var isEmpty = model.Rows.Count == 0 && !model.IsLoading;
        empty.IsVisible = isEmpty && (model.IsEmpty || model.Error is not null || model.Filter.Length > 0);
        empty.Title = model.Error is not null ? "The items could not be loaded" : model.Filter.Length > 0 ? "Nothing matches the filter" : model.Definition.EmptyMessage ?? "Nothing to show";
        empty.Hint = model.Error;
    }

    private void RefreshRows()
    {
        foreach (var row in list.GetVisualDescendants().OfType<TreeRowView>())
            row.Rebuild();
    }

    private void Reveal(TreeNode node, bool focus)
    {
        var index = model.Rows.IndexOf(node);
        if (index < 0)
            return;

        SyncSelection();
        list.ScrollIntoView(index);
        if (focus)
            Dispatcher.UIThread.Post(() => (list.ContainerFromIndex(model.Rows.IndexOf(node)) as InputElement ?? list).Focus(NavigationMethod.Tab), DispatcherPriority.Background);
    }

    private void SyncSelection()
    {
        isSyncing = true;
        if (list.SelectedItems is { } selected)
        {
            selected.Clear();
            foreach (var node in model.SelectedNodes)
            {
                if (model.Rows.IndexOf(node) >= 0)
                    selected.Add(node);
            }
        }

        isSyncing = false;
        UpdateActions();
    }

    private void BuildActions()
    {
        actions.Children.Clear();
        if (commands is not null)
        {
            var menu = TreeViewMenus.Title(model.Id);
            foreach (var item in commands.MenuItems.Where(i => i.Menu == menu).OrderBy(i => i.Group, StringComparer.Ordinal).ThenBy(i => i.Order))
            {
                if (commands.Find(item.CommandId) is not { } command)
                    continue;

                var button = Action(HammerUI.Icons.Find(command.Icon), command.Title, () => _ = commands.ExecuteAsync(command.Id, Target(null)));
                button.Tag = command.Id;
                actions.Children.Add(button);
            }
        }

        actions.Children.Add(Action(HammerUI.Icons.ChevronsUp, "Collapse all", model.CollapseAll));
        UpdateActions();
    }

    private void UpdateActions()
    {
        if (commands is null)
            return;

        foreach (var button in actions.Children.OfType<Button>())
        {
            if (button.Tag is string id)
                button.IsEnabled = commands.CanExecute(id, Target(null));
        }
    }

    private static Button Action(Geometry? icon, string tip, Action action)
    {
        var button = new Button
        {
            Classes = { "icon", "small" },
            Content = icon is null ? new TextBlock { Text = tip, Classes = { "caption" } } : new SymbolIcon { Data = icon, Size = 16 },
        };
        ToolTip.SetTip(button, tip);
        AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => action();
        return button;
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        var node = e.Source is Visual source ? NodeAt(source) : null;
        node ??= list.SelectedItem as TreeNode;
        if (commands is null || node is not { Kind: TreeNodeKind.Item })
        {
            e.Handled = true;
            return;
        }

        var items = MenuBuilder.ContributedItems(commands, TreeViewMenus.Item(model.Id), Target(node));
        if (items.Count > 0 && items[0] is Separator)
            items.RemoveAt(0);
        if (items.Count == 0)
        {
            e.Handled = true;
            return;
        }

        list.ContextMenu = new ContextMenu { ItemsSource = items };
    }

    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(list).Properties.IsRightButtonPressed || e.Source is not Visual source || NodeAt(source) is not { } node)
            return;

        if (list.SelectedItems?.Contains(node) != true)
            list.SelectedItem = node;
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (list.SelectedItem is not TreeNode node)
            return;

        switch (e.Key)
        {
            case Key.Right when node.HasChildren && !node.IsExpanded:
                _ = model.ExpandAsync(node);
                break;
            case Key.Right when node.IsExpanded && model.Rows.IndexOf(node) is var index && index + 1 < model.Rows.Count && model.Rows[index + 1].Depth > node.Depth:
                Select(model.Rows[index + 1]);
                break;
            case Key.Left when node.IsExpanded:
                model.Collapse(node);
                break;
            case Key.Left when node.Parent is { } parent:
                Select(parent);
                break;
            case Key.Enter:
                Open(node);
                break;
            case Key.Space when node.Item.IsChecked is { } isChecked:
                model.SetChecked(node, !isChecked);
                break;
            case Key.Escape when filter is { Text.Length: > 0 }:
                filter.Clear();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void Select(TreeNode node)
    {
        list.SelectedItem = node;
        var index = model.Rows.IndexOf(node);
        list.ScrollIntoView(index);
        (list.ContainerFromIndex(index) as InputElement)?.Focus(NavigationMethod.Directional);
    }

    private void OnListTextInput(object? sender, TextInputEventArgs e)
    {
        if (filter is null || string.IsNullOrWhiteSpace(e.Text) || e.Text.Any(char.IsControl))
            return;

        filter.Text = (filter.Text ?? "") + e.Text;
        filter.FocusInput();
        e.Handled = true;
    }

    private void OnFilterKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when model.Rows.Count > 0:
                Select(list.SelectedItem as TreeNode ?? model.Rows[0]);
                break;
            case Key.Escape when filter is { Text.Length: > 0 }:
                filter.Clear();
                break;
            case Key.Enter when list.SelectedItem is TreeNode node:
                Open(node);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private static TreeNode? NodeAt(Visual source) => source.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as TreeNode;

    private static void Describe(Control container)
    {
        if (container is not ListBoxItem item || item.DataContext is not TreeNode node)
            return;

        var parts = new List<string> { node.Item.Label };
        if (!string.IsNullOrEmpty(node.Item.Description))
            parts.Add(node.Item.Description);
        if (node.HasChildren)
            parts.Add(node.IsExpanded ? "expanded" : "collapsed");
        if (node.Item.IsChecked is { } isChecked)
            parts.Add(isChecked ? "checked" : "not checked");
        parts.Add($"level {node.Depth + 1}");
        AutomationProperties.SetName(item, string.Join(", ", parts));
        item.Focusable = node.Kind == TreeNodeKind.Item;
    }

    /// <summary>A row of the tree, drawn again when its node changes; rows are reused as the list scrolls.</summary>
    private sealed class TreeRowView(TreeViewControl owner) : ContentControl
    {
        private TreeNode? node;

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            Watch(DataContext as TreeNode);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            Watch(DataContext as TreeNode);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            Watch(null);
        }

        public void Rebuild()
        {
            Content = node is null ? null : Build(node);
            if (this.FindAncestorOfType<ListBoxItem>() is { } container)
                Describe(container);
        }

        private void Watch(TreeNode? value)
        {
            if (ReferenceEquals(node, value))
                return;

            if (node is not null)
                node.PropertyChanged -= OnNodeChanged;
            node = value;
            if (node is not null)
                node.PropertyChanged += OnNodeChanged;
            Rebuild();
        }

        private void OnNodeChanged(object? sender, PropertyChangedEventArgs e) => Rebuild();

        private DockPanel Build(TreeNode shown)
        {
            var item = shown.Item;
            var row = new DockPanel { Margin = new Thickness(shown.Depth * Indent, 0, 0, 0), Background = Brushes.Transparent };
            var chevronSlot = new Panel { Width = 20, Margin = new Thickness(2, 0, 0, 0) };
            if (shown.HasChildren)
            {
                var chevron = new ToggleButton { IsChecked = shown.IsExpanded, Focusable = false };
                if (Application.Current?.TryFindResource("TreeExpanderToggle", out var theme) == true && theme is ControlTheme toggleTheme)
                    chevron.Theme = toggleTheme;
                AutomationProperties.SetName(chevron, shown.IsExpanded ? "Collapse" : "Expand");
                chevron.Click += (_, _) => owner.Toggle(shown);
                chevronSlot.Children.Add(chevron);
            }

            SetDock(chevronSlot, Dock.Left);
            row.Children.Add(chevronSlot);

            if (shown.Kind != TreeNodeKind.Item)
            {
                var note = new TextBlock { Text = item.Label, Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                note[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(shown.Kind == TreeNodeKind.Error ? "DangerBrush" : "TextMutedBrush");
                row.Children.Add(note);
                return row;
            }

            if (item.IsChecked is { } isChecked)
            {
                var box = new CheckBox { IsChecked = isChecked, MinHeight = 0, Focusable = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
                AutomationProperties.SetName(box, item.Label);
                box.IsCheckedChanged += (_, _) => owner.Check(shown, box.IsChecked == true);
                SetDock(box, Dock.Left);
                row.Children.Add(box);
            }

            if (owner.IconFor(item, shown.HasChildren) is { } icon)
            {
                SetDock(icon, Dock.Left);
                row.Children.Add(icon);
            }

            var label = PaletteChrome.Highlighted(item.Label, (IReadOnlyCollection<int>?)shown.FilterMatches ?? Highlights(item));
            label.VerticalAlignment = VerticalAlignment.Center;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            SetDock(label, Dock.Left);
            row.Children.Add(label);
            if (!string.IsNullOrEmpty(item.Description))
            {
                row.Children.Add(new TextBlock
                {
                    Text = item.Description,
                    Classes = { "caption", "muted" },
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
            }

            ToolTip.SetTip(row, item.ToolTip ?? (item.Description is null ? null : $"{item.Label}  {item.Description}"));
            return row;
        }

        private static HashSet<int> Highlights(TreeItem item)
        {
            var indexes = new HashSet<int>();
            foreach (var span in item.Highlights)
            {
                for (var i = Math.Max(0, span.Start); i < Math.Min(item.Label.Length, span.End); i++)
                    indexes.Add(i);
            }

            return indexes;
        }
    }
}
