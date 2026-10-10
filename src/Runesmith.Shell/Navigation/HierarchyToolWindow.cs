using System.Composition;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.ToolWindows;
using Runesmith.Shell.Editors;
using Runesmith.Shell.Views;

namespace Runesmith.Shell.Navigation;

/// <summary>The Hierarchy panel: the callers or callees of a function, or the base or derived types of a type, as a tree that loads each
/// level when it opens.</summary>
[Export(typeof(IToolWindowProvider))]
[Export]
[Shared]
[method: ImportingConstructor]
public sealed class HierarchyToolWindow(EditorService editors, INavigationFeatures features, Lazy<IToolWindowManager> toolWindows) : IToolWindowProvider
{
    public const string Id = "hierarchy";

    /// <summary>The panel's icon, which HammerUI does not have.</summary>
    public const string Icon = "call-hierarchy";

    private static readonly Lazy<bool> IconRegistered = new(() =>
    {
        Icons.Register(Icon, "M17 16h4a1 1 0 0 1 1 1v4a1 1 0 0 1-1 1h-4a1 1 0 0 1-1-1v-4a1 1 0 0 1 1-1z", "M3 16h4a1 1 0 0 1 1 1v4a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1v-4a1 1 0 0 1 1-1z",
            "M10 2h4a1 1 0 0 1 1 1v4a1 1 0 0 1-1 1h-4a1 1 0 0 1-1-1V3a1 1 0 0 1 1-1z", "M5 16v-3a1 1 0 0 1 1-1h12a1 1 0 0 1 1 1v3", "M12 12V8");
        return true;
    });

    public ToolWindowDefinition Definition { get; } = new(Id, "Hierarchy", IconRegistered.Value ? Icon : "list-tree", DockSide.Bottom) { Order = 4 };

    /// <summary>Gets the tree the panel shows.</summary>
    public HierarchyModel Model { get; } = new(features);

    public Control CreateContent() => new HierarchyView(Model, (location, focus) => editors.RevealAsync(location.FilePath, location.Start, location.End, focus));

    /// <summary>Shows the callers of the items and the panel.</summary>
    public void ShowCalls(IReadOnlyList<HierarchyItem> items)
    {
        Model.ShowCalls(items);
        toolWindows.Value.Show(Id);
    }

    /// <summary>Shows the subtypes of the items and the panel.</summary>
    public void ShowTypes(IReadOnlyList<HierarchyItem> items)
    {
        Model.ShowTypes(items);
        toolWindows.Value.Show(Id);
    }
}

/// <summary>The content of the Hierarchy panel.</summary>
internal sealed class HierarchyView : DockPanel
{
    private readonly HierarchyModel model;
    private readonly TextBlock title = new() { FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly ToggleButton first = Direction();
    private readonly ToggleButton second = Direction();
    private readonly StackPanel header;
    private readonly TreeView tree;
    private readonly EmptyState empty = new()
    {
        Icon = Icons.ListTree,
        Title = "No hierarchy",
        Hint = "Right-click a function and choose Show Call Hierarchy, or a type and choose Show Type Hierarchy.",
    };

    public HierarchyView(HierarchyModel model, Func<DocumentLocation, bool, Task> reveal)
    {
        this.model = model;
        var toggles = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(14, 0, 0, 0), Children = { first, second } };
        header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 6), Children = { title, toggles } };
        DockPanel.SetDock(header, Dock.Top);
        Children.Add(header);
        first.Click += (_, _) =>
        {
            model.SetDirection(model.IsCallHierarchy ? HierarchyDirection.Callers : HierarchyDirection.Supertypes);
            Update();
        };
        second.Click += (_, _) =>
        {
            model.SetDirection(model.IsCallHierarchy ? HierarchyDirection.Callees : HierarchyDirection.Subtypes);
            Update();
        };

        tree = new TreeView
        {
            ItemsSource = model.Roots,
            ItemTemplate = new FuncTreeDataTemplate<HierarchyNode?>((node, _) => node is null ? new Panel() : Row(node), node => node?.Children ?? []),
        };
        tree.Styles.Add(new Style(x => x.OfType<TreeViewItem>())
        {
            Setters = { new Setter(TreeViewItem.IsExpandedProperty, new Binding(nameof(HierarchyNode.IsExpanded)) { Mode = BindingMode.TwoWay }) },
        });
        tree.SelectionChanged += (_, _) =>
        {
            if (tree.SelectedItem is HierarchyNode { Target: { } target })
                _ = reveal(target, false);
        };
        tree.AddHandler(TappedEvent, (_, _) =>
        {
            if (tree.SelectedItem is HierarchyNode { Target: { } target })
                _ = reveal(target, true);
        }, RoutingStrategies.Bubble);
        tree.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && tree.SelectedItem is HierarchyNode { Target: { } target })
            {
                _ = reveal(target, true);
                e.Handled = true;
            }
        };
        Children.Add(new Panel { Children = { tree, empty } });
        model.Changed += (_, _) => Update();
        Update();
    }

    /// <summary>Gets the tree, for tests.</summary>
    internal TreeView Tree => tree;

    private void Update()
    {
        title.Text = model.Title;
        (first.Content, second.Content) = model.IsCallHierarchy ? ("Callers", "Callees") : ("Supertypes", "Subtypes");
        first.IsChecked = model.Direction is HierarchyDirection.Callers or HierarchyDirection.Supertypes;
        second.IsChecked = !first.IsChecked;
        var hasRows = model.Roots.Count > 0;
        header.IsVisible = hasRows;
        tree.IsVisible = hasRows;
        empty.IsVisible = !hasRows;
    }

    private static StackPanel Row(HierarchyNode node)
    {
        if (node.Item is not { } item)
            return new StackPanel { Children = { new TextBlock { Text = node.Message, Classes = { "caption", "muted" } } } };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { SymbolBreadcrumbs.KindIcon(item.Kind, 14), new TextBlock { Text = item.Name, VerticalAlignment = VerticalAlignment.Center } },
        };
        if (item.Detail is { } detail)
            row.Children.Add(new TextBlock { Text = detail.ReplaceLineEndings(" "), Classes = { "caption", "muted" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 360 });

        var where = $"{Path.GetFileName(item.Location.FilePath)}:{(item.SelectionLocation ?? item.Location).Start.Line + 1}";
        row.Children.Add(new TextBlock { Text = where, Classes = { "caption", "muted" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
        if (node.CallSites.Count > 1)
            row.Children.Add(new Badge { Content = $"{node.CallSites.Count} calls", VerticalAlignment = VerticalAlignment.Center });
        ToolTip.SetTip(row, item.Location.FilePath);
        return row;
    }

    private static ToggleButton Direction() => new() { Classes = { "option" }, Padding = new Thickness(8, 2), FontSize = 12 };
}
