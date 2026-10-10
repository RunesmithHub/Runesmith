using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Editor;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Running;
using Runesmith.Sdk.Testing;
using Runesmith.Text;

namespace Runesmith.Shell.Testing;

/// <summary>The Test Explorer's content: the run buttons, a filter, grouping and counts above the tree of tests, and the selected test's
/// result and output beside it.</summary>
internal sealed class TestExplorerView : DockPanel
{
    private readonly TestService tests;
    private readonly ICommandService commands;
    private readonly Func<string?> root;
    private readonly Action<string, TextPosition> open;
    private readonly TestRows rows = new();
    private readonly TreeView tree;
    private readonly EmptyState empty = new();
    private readonly TextBox filter;
    private readonly ToggleButton failedOnly;
    private readonly ComboBox grouping;
    private readonly StackPanel summary = new() { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel details = new() { Spacing = 10, Margin = new Thickness(14, 10, 14, 14) };
    private readonly StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel tools = new() { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
    private readonly Dictionary<string, Button> buttons = [];
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private int dirty = 1;
    private string? shownKey;
    private (TestState, int, string?)? shownDetails;

    public TestExplorerView(TestService tests, ICommandService commands, Func<string?> root, Action<string, TextPosition> open)
    {
        this.tests = tests;
        this.commands = commands;
        this.root = root;
        this.open = open;

        foreach (var (id, icon, brush) in new[]
        {
            (TestCommands.RunAll, TestIcons.RunAll, "SuccessBrush"),
            (TestCommands.DebugAll, TestIcons.Debug, null),
            (TestCommands.RerunFailed, TestIcons.RerunFailed, "DangerBrush"),
            (TestCommands.RerunLast, "rotate-cw", null),
            (TestCommands.Stop, "stop", "DangerBrush"),
        })
        {
            buttons[id] = CommandButton(id, icon, brush);
            actions.Children.Add(buttons[id]);
        }

        var refresh = CommandButton(TestCommands.Refresh, "refresh", null);
        buttons[TestCommands.Refresh] = refresh;
        var collapse = SmallButton(Icons.ChevronsUp, "Collapse All", rows.CollapseAll);
        tools.Children.AddRange([refresh, collapse]);

        filter = new TextBox { PlaceholderText = "Filter by name, or @tag", Classes = { "small" }, Width = 240, VerticalAlignment = VerticalAlignment.Center };
        filter.TextChanged += (_, _) => MarkDirty();
        failedOnly = new ToggleButton { Classes = { "icon", "small" }, Content = new SymbolIcon { Data = Icons.Filter, Size = 14 }, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(failedOnly, "Show only failed tests");
        failedOnly.IsCheckedChanged += (_, _) => MarkDirty();
        grouping = new ComboBox
        {
            Classes = { "small" },
            ItemsSource = new[] { "Group by project", "Group by file", "Group by status", "Group by tag" },
            SelectedIndex = 0,
            VerticalAlignment = VerticalAlignment.Center,
        };
        grouping.SelectionChanged += (_, _) => MarkDirty();

        var bar = new DockPanel { Margin = new Thickness(8, 4, 8, 6) };
        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { actions, new Border { Width = 1, Height = 16, Margin = new Thickness(2, 0), Classes = { "divider" } }, filter, failedOnly, grouping },
        };
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(tools, Dock.Right);
        summary.HorizontalAlignment = HorizontalAlignment.Right;
        summary.Margin = new Thickness(12, 0, 8, 0);
        bar.Children.AddRange([left, tools, summary]);
        DockPanel.SetDock(bar, Dock.Top);

        tree = new TreeView
        {
            ItemsSource = rows.Roots,
            ItemTemplate = new FuncTreeDataTemplate<TestRow?>((row, _) => row is null ? new Panel() : Row(row), row => row?.Children ?? []),
        };
        tree.Styles.Add(new Style(x => x.OfType<TreeViewItem>())
        {
            Setters = { new Setter(TreeViewItem.IsExpandedProperty, new Binding(nameof(TestRow.IsExpanded)) { Mode = BindingMode.TwoWay }) },
        });
        tree.SelectionChanged += (_, _) => ShowDetails(force: true);
        tree.DoubleTapped += (_, _) => GoToSource(tree.SelectedItem as TestRow);
        tree.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && tree.SelectedItem is TestRow row)
            {
                e.Handled = true;
                GoToSource(row);
            }
        };
        tree.ContextRequested += (_, _) => tree.ContextMenu = Menu(tree.SelectedItem as TestRow);
        tree.ContextMenu = new ContextMenu();

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("1.3*,Auto,*") };
        var left2 = new Panel { MinWidth = 220, Children = { tree, empty } };
        Grid.SetColumn(left2, 0);
        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, Width = 1, Classes = { "workbench" } };
        Grid.SetColumn(splitter, 1);
        var right = new ScrollViewer { Content = details, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MinWidth = 220 };
        Grid.SetColumn(right, 2);
        grid.Children.AddRange([left2, splitter, right]);
        Children.AddRange([bar, grid]);

        tests.Tree.Changed += (_, _) => MarkDirty();
        tests.Tree.ResultsChanged += (_, _) => MarkDirty();
        tests.StateChanged += (_, _) => MarkDirty();
        tests.RunStarted += (_, _) => MarkDirty();
        commands.Changed += (_, _) => MarkDirty();
        timer.Tick += (_, _) => Refresh();
        AttachedToVisualTree += (_, _) =>
        {
            Refresh();
            timer.Start();
        };
        DetachedFromVisualTree += (_, _) => timer.Stop();
        Refresh();
    }

    /// <summary>Selects the row of a test or group, opening the groups above it.</summary>
    public void Reveal(TestNode node)
    {
        grouping.SelectedIndex = 0;
        filter.Text = "";
        failedOnly.IsChecked = false;
        Update();
        for (var at = node.Parent; at is not null; at = at.Parent)
        {
            if (rows.Find(TestRows.KeyOf(at)) is { } parent)
                parent.IsExpanded = true;
        }

        if (rows.Find(TestRows.KeyOf(node)) is { } row)
            Dispatcher.UIThread.Post(() => tree.SelectedItem = row, DispatcherPriority.Background);
    }

    private void MarkDirty() => Interlocked.Exchange(ref dirty, 1);

    private void Refresh()
    {
        UpdateButtons();
        if (tests.IsRunning)
            ShowDetails(force: false);
        if (Interlocked.Exchange(ref dirty, 0) == 0)
            return;

        Update();
        UpdateSummary();
        ShowDetails(force: false);
    }

    private void Update()
    {
        var selected = (tree.SelectedItem as TestRow)?.Key;
        rows.Update(tests.Tree, (TestGrouping)Math.Max(0, grouping.SelectedIndex), filter.Text ?? "", failedOnly.IsChecked == true, root());
        if (selected is not null && rows.Find(selected) is { } row && tree.SelectedItem != row)
            tree.SelectedItem = row;

        var hasTests = tests.Tree.AllRoots.Count > 0;
        empty.IsVisible = rows.Roots.Count == 0;
        (empty.Icon, empty.Title, empty.Hint) = tests.Providers.Count == 0
            ? (Icons.Find(TestIcons.Tests), "No test provider", "Install a plugin that finds tests, such as the plugin for your language, from the plugin hub.")
            : root() is null
                ? (Icons.Find(TestIcons.Tests), "No folder is open", "Open a folder to see its tests.")
                : tests.IsDiscovering && !hasTests
                    ? (Icons.RotateCw, "Finding tests...", "The tests show here as they are found.")
                    : hasTests
                        ? (Icons.Search, "No tests match", "Change the filter, or show every test.")
                        : (Icons.Find(TestIcons.Tests), "No tests found", "Build the folder, then refresh the tests.");
    }

    private void UpdateButtons()
    {
        foreach (var (id, button) in buttons)
        {
            button.IsEnabled = commands.CanExecute(id);
            ToolTip.SetTip(button, commands.Find(id) is { } command
                ? commands.GetKeyBinding(id) is { } keys ? $"{command.Title} ({keys})" : command.Title
                : id);
        }

        buttons[TestCommands.Stop].IsVisible = tests.IsRunning;
        buttons[TestCommands.DebugAll].IsVisible = tests.Providers.Any(p => p.CanDebug);
    }

    private void UpdateSummary()
    {
        summary.Children.Clear();
        var counts = tests.Tree.Count();
        var total = counts.Values.Sum();
        if (total == 0)
            return;

        void Add(TestState state, int count, string label)
        {
            if (count == 0)
                return;

            var (icon, brush) = TestIcons.Of(state);
            var symbol = new SymbolIcon { Data = Icons.Find(icon), Size = 13, VerticalAlignment = VerticalAlignment.Center };
            symbol[!SymbolIcon.ForegroundProperty] = new DynamicResourceExtension(brush);
            var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { symbol, new TextBlock { Text = count.ToString(CultureInfo.CurrentCulture), VerticalAlignment = VerticalAlignment.Center } } };
            ToolTip.SetTip(item, $"{count} {label}");
            summary.Children.Add(item);
        }

        Add(TestState.Passed, counts.GetValueOrDefault(TestState.Passed), "passed");
        Add(TestState.Failed, counts.GetValueOrDefault(TestState.Failed) + counts.GetValueOrDefault(TestState.Errored), "failed");
        Add(TestState.Skipped, counts.GetValueOrDefault(TestState.Skipped), "skipped");
        Add(TestState.Running, counts.GetValueOrDefault(TestState.Running) + counts.GetValueOrDefault(TestState.Queued), "running or waiting");
        Add(TestState.None, counts.GetValueOrDefault(TestState.None), "not run");

        var text = rows.Shown == total ? Tests(total) : $"{rows.Shown} of {Tests(total)}";
        if (tests.CurrentRun is { } run)
            text += run.State == TestRunState.Running ? $"  ·  running {TestDecorations.Format(run.Elapsed)}" : $"  ·  last run {TestDecorations.Format(run.Elapsed)}";
        summary.Children.Add(new TextBlock { Text = text, Classes = { "caption", "muted" }, VerticalAlignment = VerticalAlignment.Center });
    }

    private static string Tests(int count) => count == 1 ? "1 test" : $"{count} tests";

    private static StackPanel Row(TestRow row)
    {
        var symbol = new SymbolIcon { Size = 14, VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var description = new TextBlock { Classes = { "caption", "muted" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var duration = new TextBlock { Classes = { "caption", "muted" }, VerticalAlignment = VerticalAlignment.Center };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { symbol, label, description, duration } };

        void Update()
        {
            var (icon, brush) = TestIcons.Of(row.State);
            symbol.Data = Icons.Find(icon);
            symbol[!SymbolIcon.ForegroundProperty] = new DynamicResourceExtension(brush);
            symbol.Classes.Set("spin", row.State == TestState.Running);
            label.Text = row.Label;
            label.FontWeight = row.Node is null || !row.Node.IsTest ? FontWeight.Medium : FontWeight.Normal;
            description.Text = row.Description;
            description.IsVisible = !string.IsNullOrEmpty(row.Description);
            duration.Text = row.Duration;
            duration.IsVisible = row.Duration is not null;
            ToolTip.SetTip(panel, row.ToolTip);
        }

        row.PropertyChanged += (_, _) => Update();
        Update();
        return panel;
    }

    private ContextMenu Menu(TestRow? row)
    {
        var menu = new ContextMenu();
        if (row is null || row.Nodes.Count == 0)
            return menu;

        MenuItem Item(string header, string icon, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = header, Icon = new SymbolIcon { Data = Icons.Find(icon), Size = 14 }, IsEnabled = enabled };
            item.Click += (_, _) => action();
            return item;
        }

        menu.Items.Add(Item("Run", TestIcons.Run, () => _ = tests.RunAsync(row.Nodes)));
        menu.Items.Add(Item("Debug", TestIcons.Debug, () => _ = tests.RunAsync(row.Nodes, RunMode.Debug), row.Nodes.Any(n => n.Provider.CanDebug)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Go to Source", "file-code", () => GoToSource(row), row.Node?.Item is { FilePath: not null }));
        return menu;
    }

    private void GoToSource(TestRow? row)
    {
        if (row?.Node?.Item is { FilePath: { } path } item)
            open(path, item.Start ?? new TextPosition(0, 0));
    }

    private void ShowDetails(bool force)
    {
        var row = tree.SelectedItem as TestRow;
        var node = row?.Node;
        var run = tests.CurrentRun;
        var key = row?.Key ?? "run";
        (TestState, int, string?) signature = node is not null
            ? (node.State, node.Result?.Output.Length ?? 0, node.Result?.Failure?.Message)
            : (row?.State ?? TestState.None, run?.Output.Count ?? 0, run?.State.ToString());
        if (!force && key == shownKey && signature == shownDetails)
            return;

        shownKey = key;
        shownDetails = signature;
        details.Children.Clear();
        if (node is not null && node.IsTest)
            ShowTest(node, run);
        else
            ShowGroup(row, run);
    }

    private void ShowTest(TestNode node, TestRunSession? run)
    {
        var state = node.State;
        details.Children.Add(Title(state, node.Item.Label, node.Duration is { } duration ? TestDecorations.Format(duration) : null));
        details.Children.Add(new TextBlock { Text = node.Path, Classes = { "caption", "muted" }, TextWrapping = TextWrapping.Wrap });
        if (node.Item.FilePath is { } path)
            details.Children.Add(Link($"{Path.GetFileName(path)}:{(node.Item.Start?.Line ?? 0) + 1}", path, node.Item.Start ?? new TextPosition(0, 0)));

        var result = node.Result;
        if (state is TestState.None)
            details.Children.Add(Muted("This test has not run yet."));
        else if (state is TestState.Queued or TestState.Running)
            details.Children.Add(Muted(state == TestState.Running ? "Running..." : "Waiting to run..."));
        else if (state == TestState.Skipped)
            details.Children.Add(Muted(result?.SkipReason is { Length: > 0 } reason ? $"Skipped: {reason}" : "Skipped."));

        if (result?.Failure is { } failure)
        {
            var message = new SelectableTextBlock { Text = failure.Message, TextWrapping = TextWrapping.Wrap };
            message[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("DangerBrush");
            details.Children.Add(message);
            if (failure.Expected is not null || failure.Actual is not null)
                details.Children.Add(Comparison(failure.Expected, failure.Actual));
            if (failure.StackTrace.Count > 0)
            {
                details.Children.Add(Heading("Stack trace"));
                var frames = new StackPanel { Spacing = 2 };
                foreach (var frame in failure.StackTrace)
                {
                    frames.Children.Add(frame.FilePath is { } file && frame.Position is { } position
                        ? Link(frame.Text.Trim(), file, position, mono: true)
                        : new SelectableTextBlock { Text = frame.Text.Trim(), FontFamily = Mono, FontSize = 12, TextWrapping = TextWrapping.Wrap });
                }

                details.Children.Add(frames);
            }
        }

        var output = run?.OutputOf(node) is { Length: > 0 } live ? live : result?.Output ?? "";
        if (output.Length > 0)
        {
            details.Children.Add(Heading("Output"));
            details.Children.Add(Output(output));
        }
    }

    private void ShowGroup(TestRow? row, TestRunSession? run)
    {
        if (row is not null)
        {
            details.Children.Add(Title(row.State, row.Label, row.Duration));
            if (row.Description is { Length: > 0 } description)
                details.Children.Add(Muted(description));
        }
        else if (run is not null)
        {
            var state = run.State == TestRunState.Running ? TestState.Running : run.Count().Keys.Any(s => s is TestState.Failed or TestState.Errored) ? TestState.Failed : TestState.Passed;
            var title = run.State switch
            {
                TestRunState.Running => run.Mode == RunMode.Debug ? "Debugging tests" : "Running tests",
                TestRunState.Stopped => "The last run was stopped",
                _ => "Last run",
            };
            details.Children.Add(Title(state, title, TestDecorations.Format(run.Elapsed)));
            var counts = run.Count();
            details.Children.Add(Muted(string.Join(", ", new[]
            {
                (counts.GetValueOrDefault(TestState.Passed), "passed"),
                (counts.GetValueOrDefault(TestState.Failed) + counts.GetValueOrDefault(TestState.Errored), "failed"),
                (counts.GetValueOrDefault(TestState.Skipped), "skipped"),
            }.Where(c => c.Item1 > 0).Select(c => $"{c.Item1} {c.Item2}"))));
        }
        else
        {
            details.Children.Add(Muted("Select a test to see its result and output. Run tests with the buttons above or from the gutter beside them."));
            return;
        }

        if (run is not null && run.Output.Count > 0 && (row is null || row.Nodes.Any(n => run.Nodes.Contains(n) || run.Nodes.Any(r => IsWithin(n, r)))))
        {
            details.Children.Add(Heading("Run output"));
            details.Children.Add(Output(run.Output.GetText()));
        }
    }

    private static bool IsWithin(TestNode node, TestNode ancestor)
    {
        for (var at = node; at is not null; at = at.Parent)
        {
            if (at == ancestor)
                return true;
        }

        return false;
    }

    private static FontFamily Mono => new(EditorOptions.BundledFontFamily);

    private static StackPanel Title(TestState state, string text, string? duration)
    {
        var (icon, brush) = TestIcons.Of(state);
        var symbol = new SymbolIcon { Data = Icons.Find(icon), Size = 16, VerticalAlignment = VerticalAlignment.Center };
        symbol[!SymbolIcon.ForegroundProperty] = new DynamicResourceExtension(brush);
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { symbol, new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap } } };
        if (duration is not null)
            title.Children.Add(new TextBlock { Text = duration, Classes = { "caption", "muted" }, VerticalAlignment = VerticalAlignment.Center });
        return title;
    }

    private static TextBlock Heading(string text) =>
        new() { Text = text, FontWeight = FontWeight.SemiBold, FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };

    private static TextBlock Muted(string text) => new() { Text = text, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap };

    private static Border Output(string text) => new()
    {
        Classes = { "well" },
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(8, 6),
        Child = new SelectableTextBlock { Text = text.TrimEnd(), FontFamily = Mono, FontSize = 12, TextWrapping = TextWrapping.Wrap },
        [!Border.BackgroundProperty] = new DynamicResourceExtension("SurfaceSunkenBrush"),
    };

    private static Grid Comparison(string? expected, string? actual)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto") };
        void Add(int row, string label, string? value, string brush)
        {
            var name = new TextBlock { Text = label, Classes = { "caption", "muted" }, Margin = new Thickness(0, 2, 12, 2), VerticalAlignment = VerticalAlignment.Top };
            var text = new SelectableTextBlock { Text = value ?? "", FontFamily = Mono, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2) };
            text[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(brush);
            Grid.SetRow(name, row);
            Grid.SetRow(text, row);
            Grid.SetColumn(text, 1);
            grid.Children.AddRange([name, text]);
        }

        Add(0, "Expected", expected, "SuccessBrush");
        Add(1, "Actual", actual, "DangerBrush");
        return grid;
    }

    private TextBlock Link(string text, string path, TextPosition position, bool mono = false)
    {
        var link = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Cursor = new Cursor(StandardCursorType.Hand),
            FontFamily = mono ? Mono : FontFamily.Default,
            FontSize = mono ? 12 : 13,
        };
        link[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("AccentBrush");
        ToolTip.SetTip(link, $"Open {path}");
        link.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            open(path, position);
        };
        return link;
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

    private static Button SmallButton(Geometry icon, string tip, Action action)
    {
        var button = new Button { Classes = { "icon", "small" }, Content = new SymbolIcon { Data = icon, Size = 14 } };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => action();
        return button;
    }
}
