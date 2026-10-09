using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Sdk.ToolWindows;
using Runesmith.Shell.ToolWindows;

namespace Runesmith.Shell.Views;

/// <summary>The window's body: the tool window stripes at the left and right edges, the left, right and bottom areas that show one tool window
/// each, and the editor area in the middle.</summary>
internal sealed class WorkbenchView : Grid
{
    private const double SplitterSize = 4;

    private readonly ToolWindowManager manager;
    private readonly ToolStripe leftStripe = new();
    private readonly ToolStripe rightStripe = new();
    private readonly Dictionary<DockSide, ToolWindowArea> areas;
    private readonly Dictionary<DockSide, GridSplitter> splitters = [];
    private readonly Grid center = new() { RowDefinitions = new RowDefinitions("*,Auto,0") };

    public WorkbenchView(ToolWindowManager manager, Control editorArea)
    {
        this.manager = manager;
        ColumnDefinitions = new ColumnDefinitions("Auto,0,Auto,*,Auto,0,Auto");
        areas = new()
        {
            [DockSide.Left] = new ToolWindowArea(manager, DockSide.Left),
            [DockSide.Right] = new ToolWindowArea(manager, DockSide.Right),
            [DockSide.Bottom] = new ToolWindowArea(manager, DockSide.Bottom),
        };

        leftStripe.TopItems = manager.StripeItems(DockSide.Left);
        leftStripe.BottomItems = manager.StripeItems(DockSide.Bottom);
        rightStripe.TopItems = manager.StripeItems(DockSide.Right);
        foreach (var stripe in new[] { leftStripe, rightStripe })
        {
            stripe.ItemInvoked += (_, e) => manager.Toggle(e.Item.Id);
            stripe.ItemMoved += (_, e) => manager.Move(e.Item.Id, AreaOf(e.Target, e.Group), e.Index);
            stripe.ItemMenu = item => MenuFor(manager, item.Id);
        }

        SetColumn(leftStripe, 0);
        SetColumn(areas[DockSide.Left], 1);
        SetColumn(Splitter(DockSide.Left, GridResizeDirection.Columns), 2);
        SetColumn(center, 3);
        SetColumn(Splitter(DockSide.Right, GridResizeDirection.Columns), 4);
        SetColumn(areas[DockSide.Right], 5);
        SetColumn(rightStripe, 6);

        SetRow(Splitter(DockSide.Bottom, GridResizeDirection.Rows), 1);
        SetRow(areas[DockSide.Bottom], 2);
        center.Children.AddRange([editorArea, splitters[DockSide.Bottom], areas[DockSide.Bottom]]);

        Children.AddRange([leftStripe, areas[DockSide.Left], splitters[DockSide.Left], center, splitters[DockSide.Right], areas[DockSide.Right], rightStripe]);
        manager.Changed += (_, _) => Update();
        Update();
    }

    /// <summary>Gets the entries of a tool window's menu: move it to another area, or hide it.</summary>
    public static IEnumerable<Control> MenuFor(ToolWindowManager manager, string id)
    {
        var area = manager.Layout.AreaOf(id);
        MenuItem Item(string header, Geometry icon, bool enabled, Action action)
        {
            var item = new MenuItem { Header = header, Icon = new SymbolIcon { Data = icon, Size = 16 }, IsEnabled = enabled };
            item.Click += (_, _) => action();
            return item;
        }

        return
        [
            Item("Move to Left", Icons.PanelLeft, area != DockSide.Left, () => manager.Move(id, DockSide.Left)),
            Item("Move to Right", Icons.PanelRight, area != DockSide.Right, () => manager.Move(id, DockSide.Right)),
            Item("Move to Bottom", Icons.PanelBottom, area != DockSide.Bottom, () => manager.Move(id, DockSide.Bottom)),
            new Separator(),
            Item("Hide", Icons.Minus, manager.IsVisible(id), () => manager.Hide(id)),
        ];
    }

    /// <summary>Remembers the areas' sizes as they are on screen.</summary>
    public void StoreSizes()
    {
        StoreSize(DockSide.Left, ColumnDefinitions[1].ActualWidth);
        StoreSize(DockSide.Right, ColumnDefinitions[5].ActualWidth);
        StoreSize(DockSide.Bottom, center.RowDefinitions[2].ActualHeight);
    }

    private DockSide AreaOf(ToolStripe stripe, ToolStripeGroup group) =>
        ReferenceEquals(stripe, rightStripe) ? DockSide.Right : group == ToolStripeGroup.Top ? DockSide.Left : DockSide.Bottom;

    private void StoreSize(DockSide area, double size)
    {
        if (manager.Layout.ShownIn(area) is not null && size > 0)
            manager.Layout.Resize(area, size);
    }

    private GridSplitter Splitter(DockSide area, GridResizeDirection direction)
    {
        var splitter = new GridSplitter
        {
            ResizeDirection = direction,
            Classes = { "workbench" },
            Width = direction == GridResizeDirection.Columns ? SplitterSize : double.NaN,
            Height = direction == GridResizeDirection.Rows ? SplitterSize : double.NaN,
        };
        splitter.DragCompleted += (_, _) => StoreSizes();
        splitters[area] = splitter;
        return splitter;
    }

    private void Update()
    {
        StoreSizes();
        // An empty right stripe takes no room; a window's Move to Right menu item still reaches it.
        rightStripe.IsVisible = manager.StripeItems(DockSide.Right).Count > 0;
        foreach (var (area, view) in areas)
        {
            var shown = manager.Layout.ShownIn(area);
            view.Show(shown);
            splitters[area].IsVisible = shown is not null;
            var size = shown is null ? new GridLength(0) : new GridLength(manager.Layout.SizeOf(area));
            switch (area)
            {
                case DockSide.Left:
                    ColumnDefinitions[1].Width = size;
                    break;
                case DockSide.Right:
                    ColumnDefinitions[5].Width = size;
                    break;
                default:
                    center.RowDefinitions[2].Height = size;
                    break;
            }
        }
    }

    /// <summary>An area that shows one tool window, with its title, a menu and a hide button above it.</summary>
    private sealed class ToolWindowArea : DockPanel
    {
        private readonly ToolWindowManager manager;
        private readonly TextBlock title = new() { Classes = { "area-title" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly ContentControl host = new()
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(120) }],
        };

        private readonly ContentControl contentActions = new() { VerticalAlignment = VerticalAlignment.Center, Classes = { "content-actions" } };
        private string? shown;
        private IToolWindowHeader? contentHeader;

        public ToolWindowArea(ToolWindowManager manager, DockSide side)
        {
            this.manager = manager;
            Classes.Add("tool-area");
            Classes.Add(side.ToString().ToLowerInvariant());
            var options = HeaderButton(Icons.MoreHorizontal, "Options");
            options.Flyout = MenuFlyouts.Dynamic(flyout =>
            {
                if (shown is not null)
                {
                    foreach (var entry in MenuFor(manager, shown))
                        flyout.Items.Add(entry);
                }
            });
            var hide = HeaderButton(Icons.Minus, "Hide");
            hide.Click += (_, _) =>
            {
                if (shown is not null)
                    manager.Hide(shown);
            };

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { contentActions, options, hide } };
            var header = new DockPanel { Classes = { "area-header" } };
            DockPanel.SetDock(actions, Dock.Right);
            header.Children.AddRange([actions, title]);
            SetDock(header, Dock.Top);
            Children.AddRange([header, host]);
            IsVisible = false;
        }

        public void Show(string? id)
        {
            IsVisible = id is not null;
            if (id == shown)
                return;

            shown = id;
            if (contentHeader is not null)
                contentHeader.HeaderChanged -= OnHeaderChanged;
            contentHeader = null;
            contentActions.Content = null;
            if (id is null)
            {
                host.Content = null;
                return;
            }

            var content = manager.GetContent(id);
            if (content is IToolWindowHeader header)
            {
                contentHeader = header;
                header.HeaderChanged += OnHeaderChanged;
                if (header.HeaderActions?.Parent is ContentControl owner)
                    owner.Content = null;
                contentActions.Content = header.HeaderActions;
            }

            UpdateTitle();
            if (content?.Parent is ContentControl previous && !ReferenceEquals(previous, host))
                previous.Content = null;
            host.Opacity = 0;
            host.Content = content;
            host.Opacity = 1;
        }

        private void OnHeaderChanged(object? sender, EventArgs e) => UpdateTitle();

        private void UpdateTitle() => title.Text = contentHeader?.HeaderTitle ?? (shown is null ? null : manager.Find(shown)?.Title);

        private static Button HeaderButton(Geometry icon, string tip)
        {
            var button = new Button { Classes = { "icon", "small" }, Content = new SymbolIcon { Data = icon, Size = 16 }, Focusable = false };
            ToolTip.SetTip(button, tip);
            return button;
        }
    }
}
