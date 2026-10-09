using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Git.Commands;
using Runesmith.Git.Git;
using Runesmith.Git.Repositories;
using Runesmith.Sdk.Commands;

namespace Runesmith.Git.Views.Branches;

/// <summary>The branch widget's popup: a search field over the branches, the everyday actions, and the Recent, Local, Remote and Tags groups,
/// where each branch opens a menu of what can be done with it.</summary>
internal sealed class BranchPopup : Border
{
    private readonly GitOperations operations;
    private readonly RepositoryService repositories;
    private readonly ICommandService commands;
    private readonly Action close;
    private readonly SearchBox search = new() { PlaceholderText = "Search branches and actions" };
    private readonly ListBox list;
    private IReadOnlyList<GitRef> refs = [];
    private IReadOnlyList<string> recent = [];

    public BranchPopup(GitOperations operations, ICommandService commands, Action close)
    {
        this.operations = operations;
        repositories = operations.Repositories;
        this.commands = commands;
        this.close = close;
        Width = 380;
        MaxHeight = 560;
        Padding = new Thickness(0, 8, 0, 6);

        list = new ListBox
        {
            Classes = { "rows" },
            Background = Brushes.Transparent,
            ItemTemplate = new FuncDataTemplate<BranchRow?>((row, _) => row is null ? new Panel() : Row(row)),
            Margin = new Thickness(0, 6, 0, 0),
        };
        list.ContainerPrepared += (_, e) =>
        {
            if (e.Container is ListBoxItem item)
            {
                var isHeader = item.DataContext is HeaderRow;
                item.IsEnabled = !isHeader;
                item.Focusable = !isHeader;
                item.Height = isHeader ? 30 : 28;
            }
        };
        list.Tapped += (_, e) =>
        {
            if (e.Source is Visual visual && visual.FindAncestorOfType<ListBoxItem>() is { DataContext: BranchRow row } item)
                Activate(row, item);
        };
        list.KeyDown += OnListKeyDown;
        search.Margin = new Thickness(8, 0, 8, 0);
        search.PropertyChanged += (_, e) =>
        {
            if (e.Property == SearchBox.TextProperty)
                Fill();
        };
        search.AddHandler(KeyDownEvent, OnSearchKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        var layout = new DockPanel();
        DockPanel.SetDock(search, Dock.Top);
        layout.Children.AddRange([search, list]);
        Child = layout;
    }

    /// <summary>Reads the branches again and focuses the search field; called each time the popup opens.</summary>
    public async Task LoadAsync()
    {
        search.Clear();
        Fill();
        Dispatcher.UIThread.Post(search.FocusInput, DispatcherPriority.Background);
        if (repositories.Repository is not { } repository)
            return;

        var refsTask = repository.GetRefsAsync(CancellationToken.None);
        var recentTask = repository.GetRecentBranchesAsync(10, CancellationToken.None);
        try
        {
            refs = await refsTask;
            recent = await recentTask;
        }
        catch (GitException)
        {
            refs = [];
        }

        Fill();
    }

    private void Fill()
    {
        var actions = new List<ActionRow>
        {
            new("Update Project...", GitIcons.Pull, GitCommands.Pull),
            new("Commit...", GitIcons.Commit, GitCommands.Commit),
            new("Push...", GitIcons.Push, GitCommands.Push),
            new("New Branch...", "plus", GitCommands.NewBranch),
            new("Fetch", GitIcons.Fetch, GitCommands.Fetch),
        };
        var rows = BranchRows.Build(actions, refs, recent, search.Text);
        list.ItemsSource = rows;
        list.SelectedItem = rows.FirstOrDefault(r => r is not HeaderRow && !string.IsNullOrWhiteSpace(search.Text));
    }

    private Control Row(BranchRow row)
    {
        switch (row)
        {
            case HeaderRow header:
                var title = new TextBlock { Text = header.Title, Classes = { "caption", "muted" }, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 4) };
                return title;
            case ActionRow action:
                var gesture = commands.GetKeyBinding(action.CommandId)?.Split(" | ")[0];
                var actionGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
                var actionIcon = new SymbolIcon { Data = Icons.Find(action.Icon), Size = 14, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
                var actionTitle = new TextBlock { Text = action.Title, VerticalAlignment = VerticalAlignment.Center };
                var keys = new TextBlock { Text = gesture, Classes = { "caption", "muted" }, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(actionTitle, 1);
                Grid.SetColumn(keys, 2);
                actionGrid.Children.AddRange([actionIcon, actionTitle, keys]);
                return actionGrid;
            default:
                return RefContent(((RefRow)row).Ref);
        }
    }

    private static Grid RefContent(GitRef branch)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
        var icon = new SymbolIcon
        {
            Data = branch.Kind == RefKind.Tag ? Icons.Tag : branch.IsHead ? Icons.Check : Icons.Find(GitIcons.Branch),
            Size = 14,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        icon[!SymbolIcon.ForegroundProperty] = new DynamicResourceExtension(branch.IsHead ? "AccentBrush" : branch.Kind == RefKind.LocalBranch ? "TextSecondaryBrush" : "TextMutedBrush");
        var name = new TextBlock
        {
            Text = branch.Name,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = branch.IsHead ? FontWeight.SemiBold : FontWeight.Normal,
        };
        var sync = new TextBlock { Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) };
        sync[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMutedBrush");
        sync.Text = branch.IsUpstreamGone ? "gone" : Sync(branch.Ahead, branch.Behind);
        var chevron = new SymbolIcon { Data = Icons.ChevronRight, Size = 12, VerticalAlignment = VerticalAlignment.Center };
        chevron[!SymbolIcon.ForegroundProperty] = new DynamicResourceExtension("TextMutedBrush");
        Grid.SetColumn(name, 1);
        Grid.SetColumn(sync, 2);
        Grid.SetColumn(chevron, 3);
        grid.Children.AddRange([icon, name, sync, chevron]);
        var tip = branch.Kind switch
        {
            RefKind.LocalBranch when branch.Upstream is { } upstream => $"{branch.Name}, tracking {upstream}" + (branch.IsUpstreamGone ? " (deleted on the remote)" : ""),
            RefKind.LocalBranch => $"{branch.Name}, not tracking a remote branch",
            RefKind.RemoteBranch => $"{branch.BranchName} on {branch.Remote}",
            _ => $"Tag {branch.Name}",
        };
        ToolTip.SetTip(grid, branch.Subject.Length > 0 ? $"{tip}\n{branch.Subject}" : tip);
        return grid;
    }

    /// <summary>Writes ahead and behind counts compactly, such as "↑2 ↓1"; empty when both are zero.</summary>
    internal static string Sync(int ahead, int behind) =>
        string.Join(' ', new[] { ahead > 0 ? $"↑{ahead.ToString(CultureInfo.CurrentCulture)}" : null, behind > 0 ? $"↓{behind.ToString(CultureInfo.CurrentCulture)}" : null }.OfType<string>());

    private void Activate(BranchRow row, Control anchor)
    {
        switch (row)
        {
            case ActionRow action:
                close();
                _ = commands.ExecuteAsync(action.CommandId);
                break;
            case RefRow branch:
                // After the key or click that chose the branch is done, so the menu does not take it as its own.
                Dispatcher.UIThread.Post(() => ShowMenu(branch.Ref, anchor), DispatcherPriority.Background);
                break;
        }
    }

    private void ShowMenu(GitRef branch, Control anchor)
    {
        var menu = new MenuFlyout { Placement = PlacementMode.RightEdgeAlignedTop };
        void Add(string header, string? icon, Func<Task> run)
        {
            var item = new MenuItem { Header = header, Icon = icon is null ? null : new SymbolIcon { Data = Icons.Find(icon), Size = 14 } };
            item.Click += (_, _) =>
            {
                close();
                _ = run();
            };
            menu.Items.Add(item);
        }

        void Separator() => menu.Items.Add(new Separator());

        var current = repositories.Current?.Branch ?? "HEAD";
        if (!branch.IsHead)
            Add("Checkout", "check", () => operations.CheckoutAsync(branch));
        Add($"New Branch from {branch.Name}...", "plus", () => operations.NewBranchAsync(branch.FullName, branch.Name, branch.Kind == RefKind.RemoteBranch ? branch.BranchName : ""));
        if (!branch.IsHead)
        {
            Add($"Compare with {current}", "git-compare", () =>
            {
                operations.Compare(branch);
                return Task.CompletedTask;
            });
            Add($"Merge into {current}", GitIcons.Merge, () => operations.MergeAsync(branch));
        }

        Add("Show History", GitIcons.History, () =>
        {
            operations.ShowHistory(new Views.History.HistoryRequest(new LogQuery { Revision = branch.FullName }, $"History of {branch.Name}"));
            return Task.CompletedTask;
        });
        Separator();
        switch (branch.Kind)
        {
            case RefKind.LocalBranch:
                Add(branch.IsHead ? "Push..." : "Push", GitIcons.Push, () => operations.PushBranchAsync(branch));
                Add("Rename...", "pen-line", () => operations.RenameBranchAsync(branch));
                if (!branch.IsHead)
                    Add("Delete", "trash", () => operations.DeleteBranchAsync(branch));
                break;
            case RefKind.RemoteBranch:
                Add("Track", GitIcons.Pull, () => operations.TrackAsync(branch));
                Add($"Delete on {branch.Remote}", "trash", () => operations.DeleteBranchAsync(branch));
                break;
        }

        if (menu.Items.Count > 0 && menu.Items[^1] is Separator)
            menu.Items.RemoveAt(menu.Items.Count - 1);
        menu.ShowAt(anchor);
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                MoveSelection(+1);
                list.ContainerFromItem(list.SelectedItem!)?.Focus(NavigationMethod.Directional);
                break;
            case Key.Enter when list.SelectedItem is BranchRow row && list.ContainerFromItem(row) is Control container:
                Activate(row, container);
                break;
            case Key.Escape when string.IsNullOrEmpty(search.Text):
                close();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Right && list.SelectedItem is BranchRow row && list.ContainerFromItem(row) is Control container)
        {
            Activate(row, container);
            e.Handled = true;
        }
        else if (e.Key == Key.Up && list.SelectedIndex <= 1)
        {
            search.FocusInput();
            e.Handled = true;
        }
    }

    private void MoveSelection(int step)
    {
        if (list.ItemsSource is not IReadOnlyList<BranchRow> rows || rows.Count == 0)
            return;
        var index = list.SelectedIndex;
        do
        {
            index += step;
        }
        while (index >= 0 && index < rows.Count && rows[index] is HeaderRow);
        if (index >= 0 && index < rows.Count)
            list.SelectedIndex = index;
    }
}
