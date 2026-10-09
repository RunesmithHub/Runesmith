using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Git.Commands;
using Runesmith.Git.Git;
using Runesmith.Git.Hosting;
using Runesmith.Git.Repositories;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.VersionControl;

namespace Runesmith.Git.Views.History;

/// <summary>The Git window's content: the filter bar, the log with its graph, and the selected commit's details.</summary>
internal sealed class LogView : DockPanel
{
    private const double RowHeight = 28;
    private const int PrefetchRows = 60;
    private const int MaxGraphLanes = 12;

    private readonly GitOperations operations;
    private readonly RepositoryService repositories;
    private readonly RepositoryHosts hosts;
    private readonly ILauncher? launcher;
    private readonly LogSource source = new(action => Dispatcher.UIThread.Post(action));
    private readonly ListBox list;
    private readonly CommitDetailsView details;
    private readonly SearchBox search = new() { PlaceholderText = "Search messages", Width = 240 };
    private readonly Button branchFilter = FilterButton();
    private readonly Button authorFilter = FilterButton();
    private readonly TextBox pathFilter = new() { PlaceholderText = "Path", Width = 180, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border scope = new() { Classes = { "card" }, Padding = new Thickness(8, 2, 2, 2), IsVisible = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock scopeText = new() { VerticalAlignment = VerticalAlignment.Center, Classes = { "caption" } };
    private readonly EmptyState empty = new() { IsVisible = false };
    private readonly DispatcherTimer searchDelay = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer detailsDelay = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private HistoryRequest? request;
    private string? branch;
    private string? author;
    private string? selectAfterLoad;
    private string appliedPath = "";
    private int lastWidth;
    private ScrollViewer? scroller;
    private (string? Root, string? Head, string? Branch, int Ahead, int Behind) shownState;

    public LogView(GitOperations operations, HistoryNavigator navigator, ILanguageRegistry? languages, RepositoryHosts hosts, ILauncher? launcher)
    {
        this.operations = operations;
        repositories = operations.Repositories;
        this.hosts = hosts;
        this.launcher = launcher;

        list = new ListBox
        {
            Classes = { "rows" },
            ItemsSource = source.Entries,
            ItemTemplate = new FuncDataTemplate<LogEntry?>((entry, _) => entry is null ? new Panel() : new LogRow(this), supportsRecycling: true),
            SelectionMode = SelectionMode.Single,
        };
        list.TemplateApplied += (_, e) =>
        {
            scroller = e.NameScope.Find<ScrollViewer>("PART_ScrollViewer");
            scroller?.ScrollChanged += (_, _) => LoadMoreIfNeeded();
        };
        list.ContainerPrepared += (_, e) =>
        {
            if (e.Container is ListBoxItem item)
            {
                item.Height = RowHeight;
                item.Margin = default;
                item.Padding = new Thickness(6, 0, 10, 0);
            }
        };
        list.SelectionChanged += (_, _) =>
        {
            detailsDelay.Stop();
            detailsDelay.Start();
        };
        list.ContextRequested += (_, e) =>
        {
            if (list.SelectedItem is LogEntry entry)
                list.ContextMenu = ContextMenuFor(entry.Commit);
            else
                e.Handled = true;
        };
        // Avalonia opens a context menu only from a control that had one when the request began, so each starts with an empty one.
        list.ContextMenu = new ContextMenu();
        list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control) && list.SelectedItem is LogEntry entry)
            {
                _ = CopyAsync(entry.Commit.Sha);
                e.Handled = true;
            }
        };
        detailsDelay.Tick += (_, _) =>
        {
            detailsDelay.Stop();
            _ = details!.ShowAsync((list.SelectedItem as LogEntry)?.Commit);
        };
        searchDelay.Tick += (_, _) =>
        {
            searchDelay.Stop();
            Reload();
        };
        search.PropertyChanged += (_, e) =>
        {
            if (e.Property == SearchBox.TextProperty)
            {
                searchDelay.Stop();
                searchDelay.Start();
            }
        };
        pathFilter.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                Reload();
        };
        pathFilter.LostFocus += (_, _) =>
        {
            if (CurrentPathFilter != appliedPath)
                Reload();
        };
        branchFilter.Flyout = DynamicMenu(FillBranchMenu);
        authorFilter.Flyout = DynamicMenu(FillAuthorMenu);
        ToolTip.SetTip(branchFilter, "Show the commits of one branch");
        ToolTip.SetTip(authorFilter, "Show the commits of one author");
        ToolTip.SetTip(pathFilter, "Show the commits that changed a file or folder; press Enter to apply");

        details = new CommitDetailsView(operations, languages, Select);
        source.Changed += (_, _) => OnSourceChanged();

        var clearScope = new Button { Classes = { "icon", "small" }, Content = new SymbolIcon { Data = Icons.X, Size = 12 }, Margin = new Thickness(4, 0, 0, 0) };
        ToolTip.SetTip(clearScope, "Show every branch again");
        clearScope.Click += (_, _) =>
        {
            request = null;
            Reload();
        };
        scope.Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { new SymbolIcon { Data = Icons.Filter, Size = 12, Margin = new Thickness(0, 0, 6, 0) }, scopeText, clearScope } };

        var refresh = new Button { Classes = { "icon", "small" }, Content = new SymbolIcon { Data = Icons.Refresh, Size = 14 }, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(refresh, "Read the log again");
        refresh.Click += (_, _) => Reload();
        var fetch = new Button { Classes = { "icon", "small" }, Content = new SymbolIcon { Data = Icons.Find(GitIcons.Pull), Size = 14 }, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(fetch, "Fetch every remote");
        fetch.Click += async (_, _) =>
        {
            await operations.FetchAsync();
            Reload();
        };

        var filters = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(8, 6, 8, 6),
            Children = { search, branchFilter, authorFilter, pathFilter, scope },
        };
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(0, 0, 8, 0), Children = { fetch, refresh } };
        var bar = new DockPanel();
        DockPanel.SetDock(tools, Dock.Right);
        bar.Children.AddRange([tools, new ScrollViewer { Content = filters, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }]);
        DockPanel.SetDock(bar, Dock.Top);

        var split = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,380") };
        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, Width = 1 };
        splitter[!BackgroundProperty] = new DynamicResourceExtension("BorderSubtleBrush");
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(details, 2);
        split.Children.AddRange([new Panel { Children = { list, empty } }, splitter, details]);
        var divider = new Border { Classes = { "divider-h" } };
        DockPanel.SetDock(divider, Dock.Top);
        Children.AddRange([bar, divider, split]);

        navigator.Requested += (_, r) => Apply(r);
        repositories.Changed += (_, _) => OnRepositoryChanged();
        UpdateFilterLabels();
        if (navigator.TakePending() is { } pending)
            Apply(pending);
        else
            Reload();
    }

    /// <summary>Gets the number of lanes the graph column has room for.</summary>
    public int GraphLanes => Math.Clamp(source.GraphWidth, 1, MaxGraphLanes);

    /// <summary>Gets whether the log has a graph; filters by text or author leave it out.</summary>
    public bool HasGraph => source.Query.HasGraph;

    /// <summary>Gets the commit HEAD points at, for its dot.</summary>
    public string? Head => repositories.Current?.Head;

    private string CurrentPathFilter => pathFilter.Text?.Trim() ?? "";

    /// <summary>Raised when the graph column grows, so the rows on screen widen it.</summary>
    public event EventHandler? GraphWidthChanged;

    private void Apply(HistoryRequest history)
    {
        request = history;
        branch = null;
        pathFilter.Text = "";
        Reload();
    }

    private void Reload()
    {
        var baseQuery = request?.Query ?? new LogQuery { Revision = branch };
        var text = search.Text?.Trim();
        var path = appliedPath = CurrentPathFilter;
        var query = baseQuery with
        {
            Text = string.IsNullOrEmpty(text) ? null : text,
            Author = author,
            Path = baseQuery.Path ?? (path.Length > 0 ? path.Replace('\\', '/') : null),
        };
        selectAfterLoad ??= (list.SelectedItem as LogEntry)?.Commit.Sha;
        scope.IsVisible = request is not null;
        scopeText.Text = request?.Title;
        UpdateFilterLabels();
        var info = repositories.Current;
        shownState = (info?.Root, info?.Head, info?.Branch, info?.Ahead ?? 0, info?.Behind ?? 0);
        _ = source.ResetAsync(repositories.Repository, query);
    }

    private void OnRepositoryChanged()
    {
        var info = repositories.Current;
        if ((info?.Root, info?.Head, info?.Branch, info?.Ahead ?? 0, info?.Behind ?? 0) == shownState)
            return;
        if (info?.Root != shownState.Root)
        {
            request = null;
            branch = null;
            author = null;
            selectAfterLoad = null;
        }

        Reload();
    }

    private void OnSourceChanged()
    {
        var hasEntries = source.Entries.Count > 0;
        list.IsVisible = hasEntries;
        empty.IsVisible = !hasEntries && source.IsComplete;
        if (!hasEntries && source.IsComplete)
        {
            var filtered = request is not null || branch is not null || author is not null || !string.IsNullOrWhiteSpace(search.Text) || CurrentPathFilter.Length > 0;
            empty.Icon = Icons.Find(source.Error is null ? GitIcons.History : "alert-triangle");
            empty.Title = repositories.Current is null ? "No Git repository" : source.Error is not null ? "Could not read the log" : filtered ? "No commits match" : "No commits yet";
            empty.Hint = repositories.Current is null ? "Open a folder in a Git repository to see its history."
                : source.Error ?? (filtered ? "Change or clear the filters to see more commits." : "Commit your first changes from the Commit window.");
        }

        if (GraphLanes != lastWidth)
        {
            lastWidth = GraphLanes;
            GraphWidthChanged?.Invoke(this, EventArgs.Empty);
        }

        if (selectAfterLoad is { } sha && source.IndexOf(sha) is var index and >= 0)
        {
            selectAfterLoad = null;
            list.SelectedIndex = index;
            list.ScrollIntoView(index);
        }
        else if (source.Entries.Count > 0 && list.SelectedItem is null && selectAfterLoad is null)
        {
            list.SelectedIndex = 0;
        }

        if (source.IsComplete)
            selectAfterLoad = null;
        Dispatcher.UIThread.Post(LoadMoreIfNeeded, DispatcherPriority.Background);
    }

    private void LoadMoreIfNeeded()
    {
        if (source.IsComplete || scroller is null)
            return;
        var remaining = scroller.Extent.Height - scroller.Offset.Y - scroller.Viewport.Height;
        if (remaining < PrefetchRows * RowHeight || selectAfterLoad is not null)
            _ = source.LoadMoreAsync();
    }

    private void Select(string sha)
    {
        var index = source.IndexOf(sha);
        if (index >= 0)
        {
            list.SelectedIndex = index;
            list.ScrollIntoView(index);
        }
        else
        {
            selectAfterLoad = sha;
            LoadMoreIfNeeded();
        }
    }

    private void UpdateFilterLabels()
    {
        SetFilterLabel(branchFilter, "Branch", request is not null ? "Filtered" : branch is null ? "All" : GitRef.ShortName(branch));
        branchFilter.IsEnabled = request is null;
        SetFilterLabel(authorFilter, "Author", author ?? "Anyone");
    }

    private void FillBranchMenu(MenuFlyout menu)
    {
        void Add(string header, string? revision, string? icon = null)
        {
            var item = new MenuItem
            {
                Header = header,
                Icon = icon is null ? null : new SymbolIcon { Data = Icons.Find(icon), Size = 14 },
                FontWeight = revision == branch ? FontWeight.SemiBold : FontWeight.Normal,
            };
            item.Click += (_, _) =>
            {
                branch = revision;
                Reload();
            };
            menu.Items.Add(item);
        }

        Add("All branches", null, GitIcons.Merge);
        Add("Current branch (HEAD)", "HEAD", "check");
        menu.Items.Add(new Separator());
        var loading = new MenuItem { Header = "Loading branches...", IsEnabled = false };
        menu.Items.Add(loading);
        _ = LoadAsync();

        async Task LoadAsync()
        {
            if (repositories.Repository is not { } repository)
                return;
            var refs = await repository.GetRefsAsync(CancellationToken.None);
            menu.Items.Remove(loading);
            foreach (var reference in refs.Where(r => r.Kind != RefKind.Tag).OrderBy(r => r.Kind).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).Take(60))
                Add(reference.Name, reference.FullName, reference.Kind == RefKind.LocalBranch ? GitIcons.Branch : null);
        }
    }

    private void FillAuthorMenu(MenuFlyout menu)
    {
        void Add(string header, string? value)
        {
            var item = new MenuItem { Header = header, FontWeight = value == author ? FontWeight.SemiBold : FontWeight.Normal };
            item.Click += (_, _) =>
            {
                author = value;
                Reload();
            };
            menu.Items.Add(item);
        }

        Add("Anyone", null);
        var authors = source.Entries.Take(2000).GroupBy(e => e.Commit.AuthorName).OrderByDescending(g => g.Count()).Take(15).Select(g => g.Key).ToList();
        if (author is not null && !authors.Contains(author))
            authors.Insert(0, author);
        if (authors.Count > 0)
            menu.Items.Add(new Separator());
        foreach (var name in authors)
            Add(name, name);
    }

    private ContextMenu ContextMenuFor(GitCommit commit)
    {
        var items = new List<Control>();
        void Add(string header, string? icon, Action run)
        {
            var item = new MenuItem { Header = header, Icon = icon is null ? null : new SymbolIcon { Data = Icons.Find(icon), Size = 14 } };
            item.Click += (_, _) => run();
            items.Add(item);
        }

        var current = repositories.Current?.Branch ?? "HEAD";
        foreach (var label in commit.Refs.Where(r => r.Kind == RefKind.LocalBranch && !r.IsHead))
            Add($"Checkout {label.Name}", "check", () => _ = operations.CheckoutAsync(new GitRef("refs/heads/" + label.Name, RefKind.LocalBranch, commit.Sha)));
        Add("Checkout Revision", "check", () => _ = operations.CheckoutCommitAsync(commit));
        Add("New Branch...", "plus", () => _ = operations.NewBranchAsync(commit.Sha, commit.ShortSha));
        items.Add(new Separator());
        Add("Cherry-pick", GitIcons.Merge, () => _ = operations.CherryPickAsync(commit));
        Add("Revert Commit", "undo", () => _ = operations.RevertAsync(commit));
        var reset = new MenuItem { Header = $"Reset {current} to Here", Icon = new SymbolIcon { Data = Icons.RotateCcw, Size = 14 } };
        foreach (var (title, mode) in new[] { ("Soft: keep the changes staged", ResetMode.Soft), ("Mixed: keep the changes in the working tree", ResetMode.Mixed), ("Hard: throw the changes away", ResetMode.Hard) })
        {
            var item = new MenuItem { Header = title };
            item.Click += (_, _) => _ = operations.ResetAsync(commit, mode);
            reset.Items.Add(item);
        }

        items.Add(reset);
        items.Add(new Separator());
        Add("Copy Hash", "copy", () => _ = CopyAsync(commit.Sha));
        if (launcher is not null && hosts.Find(repositories.Current, preferUpstream: true) is { } hosted
            && hosted.Host.GetWebUrl(hosted.Remote.FetchUrl, new WebTarget(WebTargetKind.Commit, commit.Sha)) is { } link)
            Add($"Open on {hosted.Host.Name}", "external-link", () => launcher.OpenUrl(link));
        return new ContextMenu { ItemsSource = items };
    }

    private async Task CopyAsync(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    private static Button FilterButton() => new() { Classes = { "small" }, VerticalAlignment = VerticalAlignment.Center };

    private static void SetFilterLabel(Button button, string name, string value)
    {
        var label = new TextBlock { Text = name + ": ", VerticalAlignment = VerticalAlignment.Center };
        label[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMutedBrush");
        var chevron = new SymbolIcon { Data = Icons.ChevronDown, Size = 12, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        button.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { label, new TextBlock { Text = value, MaxWidth = 160, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }, chevron },
        };
    }

    private static MenuFlyout DynamicMenu(Action<MenuFlyout> fill)
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        // A menu flyout whose items were never set shows none of the items added when it opens.
        menu.Items.Add(new Separator());
        menu.Items.Clear();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            fill(menu);
        };
        return menu;
    }

    /// <summary>A row of the log: the graph, the labels and subject, the author and the date; it shows whichever entry the list gives it.</summary>
    private sealed class LogRow : Grid
    {
        private readonly LogView view;
        private readonly GraphCell graph = new();
        private readonly StackPanel labels = new() { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock subject = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock author = new() { Classes = { "caption", "muted" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(12, 0, 0, 0) };
        private readonly TextBlock date = new() { Classes = { "caption", "muted" }, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Margin = new Thickness(12, 0, 0, 0) };

        public LogRow(LogView view)
        {
            this.view = view;
            Height = RowHeight;
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,150,110");
            var text = new DockPanel { Children = { labels, subject }, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(labels, Dock.Left);
            Grid.SetColumn(text, 2);
            Grid.SetColumn(author, 3);
            Grid.SetColumn(date, 4);
            Children.AddRange([graph, text, author, date]);
            DataContextChanged += (_, _) => Show();
            AttachedToVisualTree += (_, _) => view.GraphWidthChanged += OnGraphWidthChanged;
            DetachedFromVisualTree += (_, _) => view.GraphWidthChanged -= OnGraphWidthChanged;
        }

        private void OnGraphWidthChanged(object? sender, EventArgs e) => graph.Width = view.HasGraph ? view.GraphLanes * GraphCell.LaneWidth + 8 : 0;

        private void Show()
        {
            if (DataContext is not LogEntry entry)
                return;

            var commit = entry.Commit;
            OnGraphWidthChanged(null, EventArgs.Empty);
            graph.Show(entry.Graph, commit.Sha == view.Head, commit.IsMerge);
            subject.Text = commit.Subject;
            author.Text = commit.AuthorName;
            date.Text = GitOperations.Relative(commit.AuthorDate);
            ToolTip.SetTip(date, commit.AuthorDate.ToLocalTime().ToString("f", CultureInfo.CurrentCulture));
            ToolTip.SetTip(subject, $"{commit.ShortSha} {commit.Subject}");
            subject[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(commit.IsMerge ? "TextSecondaryBrush" : "TextPrimaryBrush");
            labels.Children.Clear();
            foreach (var label in commit.Refs.OrderBy(r => r.IsHead ? 0 : r.Kind == RefKind.LocalBranch ? 1 : r.Kind == RefKind.RemoteBranch ? 2 : 3).Take(4))
                labels.Children.Add(Pill(label, entry.Graph?.Color ?? 0));
            if (commit.Refs.Count > 4)
                labels.Children.Add(new TextBlock { Text = $"+{commit.Refs.Count - 4}", Classes = { "caption", "muted" }, VerticalAlignment = VerticalAlignment.Center });
            labels.IsVisible = labels.Children.Count > 0;
        }

        private static Border Pill(CommitRef label, int lane)
        {
            var color = GraphCell.ColorOf(lane);
            var isTag = label.Kind == RefKind.Tag;
            var tint = isTag ? Color.Parse("#E2B203") : label.Kind == RefKind.RemoteBranch ? Color.Parse("#8F9BB3") : color;
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            if (label.IsHead || isTag)
                content.Children.Add(new SymbolIcon { Data = isTag ? Icons.Tag : Icons.Find(GitIcons.Branch), Size = 10, VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(new TextBlock { Text = label.Name, FontSize = 11, FontWeight = label.IsHead ? FontWeight.SemiBold : FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center });
            var pill = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 0),
                Height = 18,
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(tint, label.IsHead ? 0.32 : 0.18),
                BorderBrush = new SolidColorBrush(tint, 0.55),
                BorderThickness = new Thickness(label.Kind == RefKind.RemoteBranch ? 1 : 0),
                Child = content,
            };
            ToolTip.SetTip(pill, label.Kind switch
            {
                null => "HEAD (detached)",
                RefKind.LocalBranch when label.IsHead => $"{label.Name}, the current branch",
                RefKind.LocalBranch => $"Branch {label.Name}",
                RefKind.RemoteBranch => $"Remote branch {label.Name}",
                _ => $"Tag {label.Name}",
            });
            return pill;
        }
    }
}
