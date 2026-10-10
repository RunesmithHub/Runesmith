using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Dap.Protocol;
using Runesmith.Editor;
using Runesmith.Sdk.Commands;
using Runesmith.Shell.Running;
using Runesmith.Shell.Services;
using Runesmith.Shell.ToolWindows;
using Runesmith.Shell.Views;

namespace Runesmith.Shell.Debugging;

/// <summary>The Debug tool window's content: the call stack, variables and watches side by side, the debug console, and the breakpoints, with
/// the session's controls in the window's header.</summary>
internal sealed class DebugView : DockPanel, IToolWindowHeader
{
    private const double ListPadding = 4;

    private readonly DebugService debug;
    private readonly BreakpointService breakpoints;
    private readonly ICommandService commands;
    private readonly NotificationService notifications;
    private readonly Func<string?> root;
    private readonly CallStackModel callStack;
    private readonly VariablesModel variables;
    private readonly WatchesModel watches;
    private readonly BreakpointsModel breakpointList;
    private readonly DebugConsoleModel console;
    private readonly StackPanel actions;
    private readonly Dictionary<string, Button> buttons = [];
    private readonly TextBlock callStackMessage = Message();
    private readonly TextBlock variablesMessage = Message();
    private readonly ListBox consoleList;
    private readonly EmptyState consoleEmpty = new() { Icon = Icons.Terminal, Title = "No output yet", Hint = "The program's output and your evaluations show here while you debug." };
    private readonly TextBox consoleInput;
    private readonly ConsoleLinkResolver resolver;
    private readonly Action<string, int, int> open;
    private readonly DispatcherTimer consoleTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private ConsoleLineList consoleLines = [];
    private ConsoleBuffer? shownConsole;
    private string? title;

    public DebugView(DebugService debug, BreakpointService breakpoints, ICommandService commands, NotificationService notifications, Func<string?> root,
        Action<StackFrame> navigate)
    {
        this.debug = debug;
        this.breakpoints = breakpoints;
        this.commands = commands;
        this.notifications = notifications;
        this.root = root;
        callStack = new CallStackModel(debug, navigate);
        variables = new VariablesModel(debug);
        watches = new WatchesModel(debug, breakpoints);
        breakpointList = new BreakpointsModel(breakpoints, debug, root);
        console = new DebugConsoleModel(debug);
        resolver = new ConsoleLinkResolver(() => root() is { } folder ? [folder] : [], () => null);
        open = (path, line, column) => navigate(new StackFrame(0, "", line, column) { Source = new Source { Path = path } });

        actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var (id, icon, brush) in new[]
        {
            (CommandIds.Continue, DebugIcons.Continue, "SuccessBrush"),
            (CommandIds.Pause, "pause", null),
            (CommandIds.StepOver, DebugIcons.StepOver, null),
            (CommandIds.StepInto, DebugIcons.StepInto, null),
            (CommandIds.StepOut, DebugIcons.StepOut, null),
            (CommandIds.RestartDebugging, "rotate-cw", null),
            (CommandIds.StopDebugging, "stop", "DangerBrush"),
        })
        {
            buttons[id] = CommandButton(id, icon, brush);
            actions.Children.Add(buttons[id]);
        }

        consoleInput = new TextBox { PlaceholderText = "Evaluate an expression in the paused frame and press Enter", Classes = { "small" }, Margin = new Thickness(8, 4, 8, 6) };
        consoleList = ConsoleList();
        var tabs = new TabControl
        {
            Padding = new Thickness(0),
            Items =
            {
                new TabItem { Header = "Variables", Content = VariablesTab() },
                new TabItem { Header = "Console", Content = ConsoleTab() },
                new TabItem { Header = "Breakpoints", Content = BreakpointsTab() },
            },
        };
        Children.Add(tabs);

        breakpoints.Changed += (_, _) => UiThread.Run(breakpointList.Refresh);
        breakpoints.StatusesChanged += (_, _) => UiThread.Run(breakpointList.Refresh);
        breakpoints.ExceptionFiltersChanged += (_, _) => UiThread.Run(breakpointList.Refresh);
        breakpoints.WatchesChanged += (_, _) => UiThread.Run(() => _ = watches.RefreshAsync(force: true));
        commands.Changed += (_, _) => UiThread.Run(UpdateButtons);
        consoleTimer.Tick += (_, _) => FlushConsole();
        AttachedToVisualTree += (_, _) =>
        {
            Refresh();
            consoleTimer.Start();
        };
        DetachedFromVisualTree += (_, _) => consoleTimer.Stop();
        breakpointList.Refresh();
        Refresh();
    }

    public event EventHandler? HeaderChanged;

    public string? HeaderTitle => title;

    public Control HeaderActions => actions;

    /// <summary>Shows the current session as it is now.</summary>
    public void Refresh()
    {
        var session = debug.Current;
        var newTitle = session switch
        {
            null => null,
            { State: DebugState.Paused } => $"{session.Name}, paused{(session.Stop?.Reason is { } reason ? " " + DebugToolWindow.Reason(reason) : "")}",
            { State: DebugState.Starting } => $"{session.Name}, starting",
            _ => $"{session.Name}, running",
        };
        if (newTitle != title)
        {
            title = newTitle;
            HeaderChanged?.Invoke(this, EventArgs.Empty);
        }

        actions.IsVisible = session is not null;
        UpdateButtons();
        callStack.Refresh();
        callStackMessage.Text = callStack.Message;
        callStackMessage.IsVisible = callStack.Message is not null;
        _ = RefreshVariablesAsync();
        _ = watches.RefreshAsync();
        consoleInput.IsEnabled = console.CanEvaluate;
        FlushConsole();
    }

    private async Task RefreshVariablesAsync()
    {
        await variables.RefreshAsync();
        variablesMessage.Text = variables.Message;
        variablesMessage.IsVisible = variables.Message is not null;
    }

    private void UpdateButtons()
    {
        var paused = debug.Current is { State: DebugState.Paused };
        foreach (var (id, button) in buttons)
        {
            button.IsEnabled = commands.CanExecute(id);
            ToolTip.SetTip(button, commands.Find(id) is { } command
                ? commands.GetKeyBinding(id) is { } keys ? $"{command.Title} ({keys})" : command.Title
                : id);
        }

        buttons[CommandIds.Continue].IsVisible = paused || debug.Current is null;
        buttons[CommandIds.Pause].IsVisible = !buttons[CommandIds.Continue].IsVisible;
    }

    private Button CommandButton(string id, string icon, string? brush)
    {
        var symbol = new SymbolIcon { Data = Icons.Find(icon), Size = 14 };
        if (brush is not null)
            symbol[!SymbolIcon.ForegroundProperty] = new DynamicResourceExtension(brush);
        var button = new Button { Classes = { "icon", "small" }, Content = symbol };
        ToolTip.SetShowOnDisabled(button, true);
        button.Click += (_, _) => _ = commands.ExecuteAsync(id);
        return button;
    }

    private Grid VariablesTab()
    {
        var threads = new ComboBox { ItemsSource = callStack.Threads, Classes = { "small" }, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(8, 0, 8, 4) };
        threads.ItemTemplate = new FuncDataTemplate<ThreadItem>((thread, _) => new TextBlock { Text = thread?.Text });
        threads.SelectionChanged += (_, _) => callStack.SelectedThread = threads.SelectedItem as ThreadItem;
        var frames = new ListBox
        {
            ItemsSource = callStack.Frames,
            Classes = { "rows" },
            ItemTemplate = new FuncDataTemplate<FrameItem>((frame, _) => frame is null ? new Panel() : FrameRow(frame)),
        };
        frames.SelectionChanged += (_, _) =>
        {
            if (frames.SelectedItem is FrameItem frame)
                callStack.SelectedFrame = frame;
        };
        callStack.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CallStackModel.SelectedThread) && !Equals(threads.SelectedItem, callStack.SelectedThread))
                threads.SelectedItem = callStack.SelectedThread;
            else if (e.PropertyName == nameof(CallStackModel.SelectedFrame) && !Equals(frames.SelectedItem, callStack.SelectedFrame))
                frames.SelectedItem = callStack.SelectedFrame;
        };
        callStack.Threads.CollectionChanged += (_, _) => threads.IsVisible = callStack.Threads.Count > 0;
        threads.IsVisible = false;
        var stackBody = new DockPanel();
        DockPanel.SetDock(threads, Dock.Top);
        stackBody.Children.AddRange([threads, new Panel { Children = { frames, callStackMessage } }]);

        var variableTree = Tree(variables.Roots, watchTree: false);
        var watchTree = Tree(watches.Items, watchTree: true);
        var addWatch = SmallButton(Icons.Plus, "Add a watch expression", () => _ = AddWatchAsync());
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,1.6*,Auto,1.2*") };
        Place(grid, Pane("Call Stack", null, stackBody), 0);
        Place(grid, Splitter(), 1);
        Place(grid, Pane("Variables", null, new Panel { Children = { variableTree, variablesMessage } }), 2);
        Place(grid, Splitter(), 3);
        Place(grid, Pane("Watch", addWatch, watchTree), 4);
        return grid;
    }

    private static void Place(Grid grid, Control control, int column)
    {
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    private static GridSplitter Splitter() => new() { ResizeDirection = GridResizeDirection.Columns, Width = 1, Classes = { "workbench" } };

    private static DockPanel Pane(string name, Control? action, Control content)
    {
        var header = new DockPanel { Margin = new Thickness(10, 6, 6, 4), Height = 22 };
        if (action is not null)
        {
            DockPanel.SetDock(action, Dock.Right);
            header.Children.Add(action);
        }

        header.Children.Add(new TextBlock { Text = name, FontWeight = FontWeight.SemiBold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(header, Dock.Top);
        return new DockPanel { MinWidth = 140, Children = { header, content } };
    }

    private static TextBlock Message() =>
        new() { Classes = { "caption", "muted" }, Margin = new Thickness(10, 4), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top };

    private static StackPanel FrameRow(FrameItem frame)
    {
        var marker = new SymbolIcon { Data = Icons.Find(DebugIcons.CurrentLine), Size = 12, IsVisible = frame.IsCurrent, VerticalAlignment = VerticalAlignment.Center };
        marker[!SymbolIcon.ForegroundProperty] = new DynamicResourceExtension("WarningBrush");
        var name = new TextBlock { Text = frame.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        if (!frame.HasSource)
            name[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMutedBrush");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new Panel { Width = 12, Children = { marker } }, name } };
        if (frame.Location is { } location)
            row.Children.Add(new TextBlock { Text = location, Classes = { "caption", "muted" }, VerticalAlignment = VerticalAlignment.Center });
        ToolTip.SetTip(row, frame.Frame.Source?.Path ?? "No source for this frame");
        return row;
    }

    private TreeView Tree(System.Collections.ObjectModel.ObservableCollection<VariableNode> items, bool watchTree)
    {
        var tree = new TreeView
        {
            ItemsSource = items,
            ItemTemplate = new FuncTreeDataTemplate<VariableNode?>((node, _) => node is null ? new Panel() : VariableRow(node), node => node?.Children ?? []),
        };
        tree.Styles.Add(new Style(x => x.OfType<TreeViewItem>())
        {
            Setters = { new Setter(TreeViewItem.IsExpandedProperty, new Binding(nameof(VariableNode.IsExpanded)) { Mode = BindingMode.TwoWay }) },
        });
        tree.ContextRequested += (_, _) => tree.ContextMenu = VariableMenu(tree, items, watchTree);
        tree.ContextMenu = new ContextMenu();
        if (watchTree)
        {
            tree.DoubleTapped += (_, _) =>
            {
                if (tree.SelectedItem is VariableNode node && items.IndexOf(node) is >= 0 and var index)
                    _ = EditWatchAsync(index, node.Name);
            };
            tree.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Delete && tree.SelectedItem is VariableNode node && items.IndexOf(node) is >= 0 and var index)
                {
                    e.Handled = true;
                    watches.Remove(index);
                }
            };
        }

        return tree;
    }

    private static StackPanel VariableRow(VariableNode node)
    {
        if (node.IsPlaceholder)
            return new StackPanel { Children = { new TextBlock { Text = node.Name, Classes = { "caption", "muted" } } } };

        var name = new TextBlock { Text = node.Name, VerticalAlignment = VerticalAlignment.Center, FontWeight = node.IsScope ? FontWeight.SemiBold : FontWeight.Normal };
        if (!node.IsScope)
            name[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("AccentBrush");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { name } };
        if (node.IsScope)
            return row;

        var value = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 600 };
        var type = new TextBlock { Classes = { "caption", "muted" }, VerticalAlignment = VerticalAlignment.Center };
        void Update()
        {
            value.Text = node.Value.ReplaceLineEndings(" ");
            value[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(node.IsError ? "DangerBrush" : "TextBrush");
            type.Text = node.Type;
            type.IsVisible = !string.IsNullOrEmpty(node.Type);
            ToolTip.SetTip(row, string.IsNullOrEmpty(node.Type) ? node.Value : $"{node.Value}\n{node.Type}");
        }

        node.PropertyChanged += (_, _) => Update();
        Update();
        row.Children.AddRange([new TextBlock { Text = "=", Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center }, value, type]);
        return row;
    }

    private ContextMenu VariableMenu(TreeView tree, System.Collections.ObjectModel.ObservableCollection<VariableNode> items, bool watchTree)
    {
        var menu = new ContextMenu();
        if (tree.SelectedItem is VariableNode { IsPlaceholder: false } node)
        {
            menu.Items.Add(Item("Copy Value", Icons.Copy, () => _ = CopyAsync(node.Value)));
            if (!node.IsScope && !watchTree)
                menu.Items.Add(Item("Add to Watch", Icons.Eye, () => watches.Add(node.Name)));
            if (watchTree && items.IndexOf(node) is >= 0 and var index)
            {
                menu.Items.Add(Item("Edit Watch...", null, () => _ = EditWatchAsync(index, node.Name)));
                menu.Items.Add(Item("Remove Watch", Icons.Trash, () => watches.Remove(index)));
            }
        }

        if (watchTree)
            menu.Items.Add(Item("Add Watch...", Icons.Plus, () => _ = AddWatchAsync()));
        return menu;
    }

    private async Task AddWatchAsync()
    {
        if (await Prompt.AskAsync(notifications.Dialogs, "Add Watch", "Expression", validate: text => text.Length == 0 ? "Type an expression." : null) is { Length: > 0 } expression)
            watches.Add(expression);
    }

    private async Task EditWatchAsync(int index, string expression)
    {
        if (await Prompt.AskAsync(notifications.Dialogs, "Edit Watch", "Expression", expression) is { } edited)
            watches.Edit(index, edited);
    }

    private DockPanel ConsoleTab()
    {
        consoleInput.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Enter:
                    e.Handled = true;
                    var text = consoleInput.Text ?? "";
                    consoleInput.Text = "";
                    _ = console.EvaluateAsync(text);
                    break;
                case Key.Up when console.Previous() is { } previous:
                    e.Handled = true;
                    consoleInput.Text = previous;
                    consoleInput.CaretIndex = previous.Length;
                    break;
                case Key.Down:
                    e.Handled = true;
                    consoleInput.Text = console.Next();
                    consoleInput.CaretIndex = consoleInput.Text.Length;
                    break;
            }
        };
        DockPanel.SetDock(consoleInput, Dock.Bottom);
        consoleList.ItemsSource = consoleLines;
        return new DockPanel { Children = { consoleInput, new Panel { Children = { consoleList, consoleEmpty } } } };
    }

    private ListBox ConsoleList()
    {
        var list = new ListBox
        {
            Classes = { "rows", "console" },
            FontFamily = new FontFamily(EditorOptions.BundledFontFamily),
            FontSize = 12.5,
            SelectionMode = SelectionMode.Single,
            Padding = new Thickness(0, ListPadding),
            ItemTemplate = new FuncDataTemplate<ConsoleLine>((line, _) => line is null ? new TextBlock() : new ConsoleLineView(line, resolver, open)),
        };
        list.ContainerPrepared += (_, e) =>
        {
            e.Container.MinHeight = 0;
            if (e.Container is ListBoxItem item)
                item.Padding = new Thickness(10, 0);
        };
        list.ContextMenu = new ContextMenu
        {
            Items =
            {
                Item("Copy Line", Icons.Copy, () => _ = CopyAsync(list.SelectedItem is ConsoleLine line ? line.Text : null)),
                Item("Copy All", null, () => _ = CopyAsync(shownConsole?.GetText())),
                new Separator(),
                Item("Clear", Icons.Trash, () => shownConsole?.Clear()),
            },
        };
        return list;
    }

    private void FlushConsole()
    {
        var buffer = console.Console;
        consoleEmpty.IsVisible = buffer is null || buffer.Count == 0;
        if (buffer != shownConsole)
        {
            shownConsole = buffer;
            consoleLines = [];
            consoleList.ItemsSource = consoleLines;
            if (buffer is not null)
                consoleLines.Apply(buffer.Drain(all: true));
            ScrollConsoleToEnd();
            return;
        }

        if (buffer is null)
            return;

        var atEnd = consoleList.Scroll is not { } scroll || scroll.Offset.Y + scroll.Viewport.Height >= scroll.Extent.Height - 24;
        if (consoleLines.Apply(buffer.Drain()) && atEnd)
            ScrollConsoleToEnd();
    }

    private void ScrollConsoleToEnd()
    {
        if (consoleLines.Count == 0)
            return;

        consoleList.ScrollIntoView(consoleLines.Count - 1);
        consoleList.FindDescendantOfType<ScrollViewer>()?.ScrollToEnd();
    }

    private DockPanel BreakpointsTab()
    {
        var list = new ListBox
        {
            ItemsSource = breakpointList.Items,
            Classes = { "rows" },
            ItemTemplate = new FuncDataTemplate<BreakpointItem>((item, _) => item is null ? new Panel() : BreakpointRow(item)),
        };
        list.DoubleTapped += (_, _) =>
        {
            if (list.SelectedItem is BreakpointItem item)
                open(item.Breakpoint.Path, item.Breakpoint.Line + 1, 1);
        };
        list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete && list.SelectedItem is BreakpointItem item)
            {
                e.Handled = true;
                breakpointList.Remove(item);
            }
        };
        list.ContextRequested += (_, _) => list.ContextMenu = BreakpointMenu(list.SelectedItem as BreakpointItem);
        list.ContextMenu = new ContextMenu();

        var filters = new WrapPanel { Margin = new Thickness(10, 4, 10, 4), Orientation = Orientation.Horizontal };
        void UpdateFilters()
        {
            filters.Children.Clear();
            if (breakpointList.ExceptionFilters.Count == 0)
            {
                filters.IsVisible = false;
                return;
            }

            filters.IsVisible = true;
            filters.Children.Add(new TextBlock { Text = "Stop on", Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            foreach (var filter in breakpointList.ExceptionFilters)
            {
                var box = new CheckBox { Content = filter.Label, IsChecked = filter.IsEnabled, Margin = new Thickness(0, 0, 14, 0) };
                box.IsCheckedChanged += (_, _) => filter.IsEnabled = box.IsChecked == true;
                if (filter.Description is { Length: > 0 } description)
                    ToolTip.SetTip(box, description);
                filters.Children.Add(box);
            }
        }

        breakpointList.ExceptionFilters.CollectionChanged += (_, _) => UpdateFilters();
        UpdateFilters();

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Margin = new Thickness(6, 4, 8, 0),
            Children =
            {
                SmallButton(Icons.Check, "Enable all breakpoints", () => breakpoints.EnableAll(true)),
                SmallButton(Icons.Slash, "Disable all breakpoints", () => breakpoints.EnableAll(false)),
                SmallButton(Icons.Trash, "Remove all breakpoints", () => _ = commands.ExecuteAsync(DebugCommands.RemoveAllBreakpoints)),
            },
        };
        var empty = new EmptyState { Icon = Icons.Find(DebugIcons.Breakpoint), Title = "No breakpoints", Hint = "Click beside a line number, or press F9 on a line, to add one." };
        void UpdateEmpty() => empty.IsVisible = breakpointList.Items.Count == 0;
        breakpointList.Items.CollectionChanged += (_, _) => UpdateEmpty();
        UpdateEmpty();
        DockPanel.SetDock(bar, Dock.Top);
        DockPanel.SetDock(filters, Dock.Top);
        return new DockPanel { Children = { bar, filters, new Panel { Children = { list, empty } } } };
    }

    private static StackPanel BreakpointRow(BreakpointItem item)
    {
        var enabled = new CheckBox { IsChecked = item.IsEnabled, VerticalAlignment = VerticalAlignment.Center, MinWidth = 0, Padding = new Thickness(0) };
        enabled.IsCheckedChanged += (_, _) => item.IsEnabled = enabled.IsChecked == true;
        var icon = new SymbolIcon { Data = Icons.Find(item.Breakpoint.IsLogPoint ? DebugIcons.LogPoint : DebugIcons.Breakpoint), Size = 12, VerticalAlignment = VerticalAlignment.Center };
        icon[!SymbolIcon.ForegroundProperty] = new DynamicResourceExtension(!item.IsEnabled ? "TextMutedBrush" : item.IsUnverified ? "WarningBrush" : item.Breakpoint.IsLogPoint ? "InfoBrush" : "DangerBrush");
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                enabled,
                icon,
                new TextBlock { Text = $"{item.FileName}:{item.Line}", VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = item.Folder, Classes = { "caption", "muted" }, VerticalAlignment = VerticalAlignment.Center },
            },
        };
        if (item.Detail is { } detail)
            row.Children.Add(new TextBlock { Text = detail, Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center });
        if (item.IsUnverified)
            row.Children.Add(new TextBlock { Text = item.Status?.Message ?? "Not set yet", Classes = { "caption", "muted" }, VerticalAlignment = VerticalAlignment.Center });
        ToolTip.SetTip(row, item.Breakpoint.Path);
        return row;
    }

    private ContextMenu BreakpointMenu(BreakpointItem? item)
    {
        var menu = new ContextMenu();
        if (item is null)
            return menu;

        var target = new ContextMenuTarget(item.Breakpoint.Path) { Lines = (item.Breakpoint.Line + 1, item.Breakpoint.Line + 1) };
        menu.Items.Add(Item("Go to Line", null, () => open(item.Breakpoint.Path, item.Breakpoint.Line + 1, 1)));
        menu.Items.Add(Item("Edit Breakpoint...", Icons.Settings, () => _ = commands.ExecuteAsync(DebugCommands.EditBreakpoint, target)));
        menu.Items.Add(Item(item.IsEnabled ? "Disable" : "Enable", null, () => item.IsEnabled = !item.IsEnabled));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Remove", Icons.Trash, () => breakpointList.Remove(item)));
        return menu;
    }

    private static Button SmallButton(Geometry icon, string tip, Action action)
    {
        var button = new Button { Classes = { "icon", "small" }, Content = new SymbolIcon { Data = icon, Size = 14 } };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => action();
        return button;
    }

    private static MenuItem Item(string header, Geometry? icon, Action action)
    {
        var item = new MenuItem { Header = header, Icon = icon is null ? null : new SymbolIcon { Data = icon, Size = 14 } };
        item.Click += (_, _) => action();
        return item;
    }

    private async Task CopyAsync(string? text)
    {
        if (text is not null && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }
}
