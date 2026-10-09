using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Git.Clone;
using Runesmith.Git.Commands;
using Runesmith.Git.Git;
using Runesmith.Git.Hosting;
using Runesmith.Plugins.Views;
using Runesmith.Sdk.VersionControl;
using static Runesmith.Plugins.Views.ViewHelpers;

namespace Runesmith.Git.Views.Clone;

/// <summary>The Clone Repository dialog: a tab per hosting account with its repositories grouped by owner, or a sign-in prompt while it is
/// signed out, Add Account for every hosting service, and any Git URL; then the folder to clone into. Cloning shows Git's progress, then
/// opens the folder and closes the dialog.</summary>
internal sealed class CloneDialog : Dialog, IDisposable
{
    private readonly CloneModel model;
    private readonly AvatarCache avatars;
    private readonly CloneService cloner;
    private readonly Action<Uri> openUrl;
    private readonly TabControl tabs = new() { Padding = new Thickness(0) };
    private readonly Button addAccount;
    private readonly Dictionary<IRepositoryHost, HostPage> pages = [];
    private readonly TextBox urlBox = new() { PlaceholderText = "https://host/owner/repository.git or git@host:owner/repository.git" };
    private readonly TextBlock urlProblem = Label(null, "DangerBrush", 12);
    private readonly PathField parentField = new() { Mode = PathFieldMode.Folder, DialogTitle = "Choose the folder to clone into" };
    private readonly TextBox nameBox = new() { PlaceholderText = "folder name" };
    private readonly TextBlock targetHint = Label(null, "TextMutedBrush", 12);
    private readonly SymbolIcon targetIcon = new() { Size = 13, VerticalAlignment = VerticalAlignment.Center };
    private readonly ProgressBar progressBar = new() { Minimum = 0, Maximum = 1, Height = 4, MinWidth = 0, Width = 220, IsVisible = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock progressText = Label(null, "TextMutedBrush", 12);
    private readonly Button cloneButton = new() { Content = "Clone", Classes = { "accent" }, IsDefault = true, MinWidth = 96, IsEnabled = false };
    private readonly Button cancelButton = new() { Content = "Cancel", MinWidth = 84 };
    private readonly CancellationTokenSource closing = new();
    private readonly StackPanel urlPage;
    private IReadOnlyList<CloneSource> sources = [];
    private string? suggestedName;
    private CloneOperation? operation;
    private bool isDisposed;

    public CloneDialog(CloneModel model, AvatarCache avatars, CloneService cloner, Action<Uri> openUrl)
    {
        this.model = model;
        this.avatars = avatars;
        this.cloner = cloner;
        this.openUrl = openUrl;
        GitIcons.Register();
        Header = "Clone Repository";
        Width = 980;
        Height = 720;
        Padding = new Thickness(0);
        ShowCloseButton = true;
        Styles.Add(CreateStyles());

        urlPage = UrlPage();
        addAccount = AddAccountButton();
        FillTabs(select: null);
        tabs.SelectionChanged += (_, e) =>
        {
            if (e.Source != tabs)
                return;

            UpdateTarget();
            if (IsUrlTab)
                Dispatcher.UIThread.Post(() => urlBox.Focus(), DispatcherPriority.Loaded);
            else
                SelectedPage?.Show();
        };

        urlBox.TextChanged += (_, _) => UpdateTarget();
        parentField.Path = CloneMemory.DefaultParent();
        parentField.PropertyChanged += (_, e) =>
        {
            if (e.Property == PathField.PathProperty)
                UpdateTarget();
        };
        nameBox.TextChanged += (_, _) => UpdateTarget();

        var tabArea = new Panel { Children = { tabs, addAccount } };
        var content = new DockPanel { Children = { Destination(), tabArea } };
        DockPanel.SetDock(content.Children[0], Dock.Bottom);
        Content = content;
        var progress = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Children = { progressBar, progressText } };
        progressText.MaxWidth = 560;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { cancelButton, cloneButton } };
        var footer = new DockPanel { Children = { buttons, progress } };
        DockPanel.SetDock(buttons, Dock.Right);
        Footer = footer;
        cloneButton.Click += (_, _) => _ = CloneAsync();
        cancelButton.Click += (_, _) =>
        {
            if (operation is not null)
                operation.Cancel();
            else
                Close();
        };

        model.Changed += OnHostsChanged;
        AttachedToVisualTree += (_, _) => SelectedPage?.Show();
        UpdateTarget();
        DetachedFromVisualTree += (_, _) => Dispose();
    }

    protected override Type StyleKeyOverride => typeof(Dialog);

    /// <summary>Gets the folder the repository is cloned into, from the location and name fields.</summary>
    public string? Target => parentField.Path is { Length: > 0 } parent && nameBox.Text?.Trim() is { Length: > 0 } name && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        ? Path.Combine(parent.Trim(), name)
        : null;

    /// <summary>Selects the URL tab with a URL filled in, such as one the command was given.</summary>
    public void UseUrl(string url)
    {
        urlBox.Text = url;
        Select(UrlSource.Instance);
    }

    public void Dispose()
    {
        if (isDisposed)
            return;

        isDisposed = true;
        model.Changed -= OnHostsChanged;
        if (operation is not null)
        {
            operation.IsWatched = false;
            operation.Progress -= OnCloneProgress;
        }

        closing.Cancel();
        closing.Dispose();
    }

    private bool IsUrlTab => SelectedSource is UrlSource;

    private CloneSource? SelectedSource => tabs.SelectedIndex >= 0 && tabs.SelectedIndex < sources.Count ? sources[tabs.SelectedIndex] : null;

    private HostPage? SelectedPage => SelectedSource is HostSource source && pages.TryGetValue(source.Host, out var page) ? page : null;

    private void OnHostsChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (!isDisposed)
            FillTabs(SelectedSource);
    });

    // A host's page outlives the rebuild of the tabs, so signing in to one keeps the search and selection of the others.
    private void FillTabs(CloneSource? select)
    {
        sources = model.Sources;
        var items = new List<TabItem>();
        foreach (var source in sources)
        {
            var margin = items.Count == 0 ? new Thickness(16, 0, 12, 0) : new Thickness(0, 0, 12, 0);
            if (source is HostSource host)
            {
                if (!pages.TryGetValue(host.Host, out var page))
                {
                    page = new HostPage(this, host.Host);
                    pages[host.Host] = page;
                }

                items.Add(new TabItem { Header = TabHeader(GitIcons.Get(host.Host.Icon), host.Host.Name, host.Host.Account), Content = page, Margin = margin });
            }
            else
            {
                items.Add(new TabItem { Header = TabHeader(GitIcons.Get(GitIcons.Globe), "URL", null), Content = urlPage, Margin = margin });
            }
        }

        foreach (var gone in pages.Keys.Where(h => !sources.OfType<HostSource>().Any(s => s.Host == h)).ToList())
            pages.Remove(gone);

        tabs.ItemsSource = items;
        var index = select is null ? -1 : IndexOf(select);
        tabs.SelectedIndex = index >= 0 ? index : 0;
        SelectedPage?.Show();
        UpdateTarget();
    }

    private int IndexOf(CloneSource source)
    {
        for (var i = 0; i < sources.Count; i++)
        {
            if (sources[i] == source)
                return i;
        }

        return -1;
    }

    private void Select(CloneSource source)
    {
        var index = IndexOf(source);
        if (index >= 0)
            tabs.SelectedIndex = index;
    }

    private static StackPanel TabHeader(Geometry icon, string text, string? account)
    {
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            Children = { new SymbolIcon { Data = icon, Size = 14, VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } },
        };
        if (account is not null)
            header.Children.Add(Label(account, "TextMutedBrush", 12));
        return header;
    }

    private Button AddAccountButton()
    {
        var button = IconButton(Icons.Plus, "Add Account", "subtle", "small");
        button.HorizontalAlignment = HorizontalAlignment.Right;
        button.VerticalAlignment = VerticalAlignment.Top;
        button.Margin = new Thickness(0, 6, 16, 0);
        button.IsVisible = model.Providers.Count > 0;
        ToolTip.SetTip(button, "Sign in to another account, such as one on another server");
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        foreach (var provider in model.Providers)
        {
            var item = new MenuItem { Header = $"{provider.Name} Account...", Icon = new SymbolIcon { Data = GitIcons.Get(provider.Icon), Size = 14 } };
            item.Click += (_, _) => _ = AddAccountAsync(provider);
            menu.Items.Add(item);
        }

        button.Flyout = menu;
        return button;
    }

    private async Task AddAccountAsync(IRepositoryHostProvider provider)
    {
        CloneSource? added;
        try
        {
            added = await model.AddAccountAsync(provider, closing.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (added is not null && !isDisposed)
            FillTabs(added);
    }

    private StackPanel UrlPage()
    {
        var label = Label("Repository URL", weight: FontWeight.Medium);
        var hint = Label("HTTPS addresses on a server you are signed in to use that sign-in; every other address uses your own Git setup, such as SSH keys and credential helpers.",
            "TextMutedBrush", 12, wrap: true);
        urlProblem.IsVisible = false;
        return new StackPanel { Spacing = 8, Margin = new Thickness(24, 22, 24, 16), MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left, Children = { label, urlBox, urlProblem, hint } };
    }

    private Border Destination()
    {
        var parentLabel = Label("Location", "TextSecondaryBrush");
        var nameLabel = Label("Folder", "TextSecondaryBrush");
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,12,*,16,Auto,12,220"), RowDefinitions = new RowDefinitions("Auto,6,Auto") };
        grid.Children.AddRange([parentLabel, parentField, nameLabel, nameBox]);
        Grid.SetColumn(parentField, 2);
        Grid.SetColumn(nameLabel, 4);
        Grid.SetColumn(nameBox, 6);
        var hint = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { targetIcon, targetHint } };
        Grid.SetRow(hint, 2);
        Grid.SetColumn(hint, 2);
        Grid.SetColumnSpan(hint, 5);
        grid.Children.Add(hint);
        var border = new Border { Padding = new Thickness(16, 12, 16, 4), BorderThickness = new Thickness(0, 1, 0, 0), Child = grid };
        Brush(border, Border.BorderBrushProperty, "BorderSubtleBrush");
        return border;
    }

    private string? SelectedUrl()
    {
        if (IsUrlTab)
            return RepositoryCloner.IsValidUrl(urlBox.Text?.Trim()) ? urlBox.Text!.Trim() : null;

        return SelectedPage?.Selected?.CloneUrl;
    }

    private void UpdateTarget()
    {
        if (IsUrlTab)
        {
            var text = urlBox.Text?.Trim();
            var valid = RepositoryCloner.IsValidUrl(text);
            urlProblem.Text = string.IsNullOrEmpty(text) || valid ? null : "This is not a Git URL. Use an https, ssh or git address, or user@host:path.";
            urlProblem.IsVisible = urlProblem.Text is not null;
        }

        var suggested = IsUrlTab ? RepositoryCloner.FolderName(urlBox.Text?.Trim()) : SelectedPage?.Selected?.Name;
        // The name follows the repository until the user types one of their own.
        if (suggested is not null && suggested != nameBox.Text && (string.IsNullOrWhiteSpace(nameBox.Text) || nameBox.Text == suggestedName))
        {
            suggestedName = suggested;
            nameBox.Text = suggested;
        }

        var target = Target;
        var occupied = target is not null && RepositoryCloner.IsOccupied(target);
        targetHint.Text = target is null ? "Choose a location and a folder name." : occupied ? $"{target} exists and is not empty; choose another folder." : $"Clones into {target}";
        targetIcon.Data = occupied ? Icons.AlertTriangle : Icons.Folder;
        Brush(targetIcon, SymbolIcon.ForegroundProperty, occupied ? "WarningBrush" : "TextMutedBrush");
        Brush(targetHint, TextBlock.ForegroundProperty, occupied ? "WarningBrush" : "TextMutedBrush");
        cloneButton.IsEnabled = operation is null && SelectedUrl() is not null && target is not null && !occupied;
    }

    private async Task CloneAsync()
    {
        if (operation is not null || SelectedUrl() is not { } url || Target is not { } target || RepositoryCloner.IsOccupied(target))
            return;

        new CloneMemory(Path.GetDirectoryName(target)).Save();
        operation = cloner.Start(url, target);
        operation.Progress += OnCloneProgress;
        SetCloning(true);
        ShowProgress($"Cloning into {target}...", null, isError: false);
        try
        {
            await operation.Completion;
            if (!isDisposed)
                Close(target);
        }
        catch (OperationCanceledException)
        {
            ShowProgress("The clone was cancelled.", null, isError: false);
        }
        catch (GitException exception)
        {
            ShowProgress(exception.Message, null, isError: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            ShowProgress(exception.Message, null, isError: true);
        }
        finally
        {
            operation.Progress -= OnCloneProgress;
            operation.Dispose();
            operation = null;
            if (!isDisposed)
            {
                SetCloning(false);
                UpdateTarget();
            }
        }
    }

    private void OnCloneProgress(CloneStep step) => ShowProgress(step.Text, step.Fraction, isError: false);

    private void SetCloning(bool cloning)
    {
        tabs.IsEnabled = !cloning;
        addAccount.IsEnabled = !cloning;
        parentField.IsEnabled = !cloning;
        nameBox.IsEnabled = !cloning;
        cloneButton.IsEnabled = !cloning;
        progressBar.IsVisible = cloning;
        progressBar.IsIndeterminate = cloning;
        progressBar.Value = 0;
    }

    private void ShowProgress(string text, double? fraction, bool isError)
    {
        progressText.Text = text;
        progressText.TextWrapping = isError ? TextWrapping.Wrap : TextWrapping.NoWrap;
        progressText.MaxLines = isError ? 4 : 1;
        ToolTip.SetTip(progressText, text);
        Brush(progressText, TextBlock.ForegroundProperty, isError ? "DangerBrush" : "TextMutedBrush");
        if (fraction is { } value)
        {
            progressBar.IsIndeterminate = false;
            progressBar.Value = Math.Max(progressBar.Value, value);
        }
    }

    private static Styles CreateStyles() =>
    [
        new Style(x => x.OfType<ListBox>().Class("repositories"))
        {
            Setters = { new Setter(BackgroundProperty, Brushes.Transparent), new Setter(PaddingProperty, new Thickness(10, 0, 10, 10)) },
        },
        new Style(x => x.OfType<ListBox>().Class("repositories").Descendant().OfType<ListBoxItem>())
        {
            Setters = { new Setter(PaddingProperty, new Thickness(10, 0)), new Setter(MinHeightProperty, 0d), new Setter(CornerRadiusProperty, new CornerRadius(8)) },
        },
        new Style(x => x.OfType<ListBox>().Class("repositories").Descendant().OfType<ListBoxItem>().Class("group"))
        {
            Setters = { new Setter(OpacityProperty, 1d), new Setter(CursorProperty, Cursor.Default) },
        },
        new Style(x => Selectors.Or(x.OfType<ToggleButton>().Class("owner"), x.OfType<Button>().Class("owner")))
        {
            Setters =
            {
                new Setter(HeightProperty, 26d),
                new Setter(MinHeightProperty, 26d),
                new Setter(PaddingProperty, new Thickness(6, 0, 10, 0)),
                new Setter(FontSizeProperty, 12d),
                new Setter(CornerRadiusProperty, new CornerRadius(13)),
                new Setter(BorderThicknessProperty, new Thickness(1)),
            },
        },
    ];

    /// <summary>One host's tab: its repositories grouped by owner with search and owner chips, or a sign-in prompt while it is signed out.</summary>
    private sealed class HostPage : ContentControl
    {
        private const double RowHeight = 56;
        private const double GroupHeight = 34;

        private readonly CloneDialog dialog;
        private readonly IRepositoryHost host;
        private readonly SearchBox search = new() { PlaceholderText = "Search repositories by name, owner or description" };
        private readonly WrapPanel ownerChips = new() { ItemSpacing = 6, LineSpacing = 6 };
        private readonly ListBox list = new() { Classes = { "repositories" }, SelectionMode = SelectionMode.Single };
        private readonly ContentControl listStatus = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        private Window? window;
        private bool reloadOnReturn;
        private RepositoryCatalog? catalog;
        private string? shownFor;
        private string? owner;
        private int generation;

        public HostPage(CloneDialog dialog, IRepositoryHost host)
        {
            this.dialog = dialog;
            this.host = host;
            list.ItemTemplate = new FuncDataTemplate<RepositoryListRow>((row, _) => row switch
            {
                OwnerRow group => OwnerView(group),
                RepositoryRow repository => RepositoryView(repository.Repository),
                _ => new Panel(),
            });
            list.ContainerPrepared += (_, e) =>
            {
                var isGroup = e.Container.DataContext is OwnerRow;
                e.Container.IsEnabled = !isGroup;
                e.Container.Focusable = !isGroup;
                e.Container.Height = isGroup ? GroupHeight : RowHeight;
                e.Container.Classes.Set("group", isGroup);
            };
            list.SelectionChanged += (_, _) => OnSelectionChanged();
            list.DoubleTapped += (_, e) =>
            {
                if (e.Source is Visual visual && visual.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is RepositoryRow)
                    _ = dialog.CloneAsync();
            };
            search.PropertyChanged += (_, e) =>
            {
                if (e.Property == SearchBox.TextProperty)
                    Refill();
            };
            search.AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel);
        }

        /// <summary>Gets the selected repository.</summary>
        public HostedRepository? Selected => (list.SelectedItem as RepositoryRow)?.Repository;

        /// <summary>Shows the page as the host is now: the sign-in prompt, or its repositories, loaded once per account.</summary>
        public void Show()
        {
            if (host.Account is null)
            {
                catalog = null;
                shownFor = null;
                Content = SignedOutView();
                return;
            }

            if (shownFor != host.Account)
                _ = LoadAsync(refresh: false);
        }

        private async Task LoadAsync(bool refresh)
        {
            if (host.Account is not { } account)
                return;

            shownFor = account;
            Content = RepositoriesView(account);
            ShowListStatus(Loading("Loading repositories..."));
            list.ItemsSource = null;
            var current = ++generation;
            try
            {
                var loaded = await dialog.model.LoadAsync(host, refresh, dialog.closing.Token);
                if (current != generation || dialog.isDisposed)
                    return;

                catalog = loaded;
                FillOwners();
                Refill();
                if (dialog.SelectedPage == this)
                    search.FocusInput();
            }
            catch (OperationCanceledException) when (dialog.closing.IsCancellationRequested)
            {
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (current == generation)
                    ShowListStatus(Message(Icons.AlertTriangle, "The repositories could not be loaded", exception.Message, "Try Again", () => _ = LoadAsync(refresh: true)));
            }
        }

        private StackPanel SignedOutView()
        {
            var tile = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(14), Child = new SymbolIcon { Data = GitIcons.Get(host.Icon), Size = 28, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            Brush(tile, Border.BackgroundProperty, "AccentSubtleBrush");
            Brush((SymbolIcon)tile.Child, SymbolIcon.ForegroundProperty, "AccentBrush");
            var title = Label($"Clone from your {host.Name} account", fontSize: 16, weight: FontWeight.SemiBold);
            var hint = Label("Sign in to see the repositories of your account and of the organizations you give Runesmith access to.", "TextSecondaryBrush", wrap: true);
            hint.TextAlignment = TextAlignment.Center;
            hint.MaxWidth = 380;
            var signIn = IconButton(GitIcons.Get(host.Icon), $"Sign in to {host.Name}", "accent");
            signIn.HorizontalAlignment = HorizontalAlignment.Center;
            signIn.Click += (_, _) => _ = SignInAsync();
            var url = Link("Or clone any repository by its URL", () => dialog.Select(UrlSource.Instance));
            url.HorizontalAlignment = HorizontalAlignment.Center;
            foreach (var control in new Control[] { tile, title })
                control.HorizontalAlignment = HorizontalAlignment.Center;
            return new StackPanel { Spacing = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Children = { tile, title, hint, signIn, url } };
        }

        private async Task SignInAsync()
        {
            try
            {
                await host.SignInAsync(dialog.closing.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        // The search box, chips and list are kept between loads, so they leave the previous view before joining the new one.
        private static void Release(params Control[] controls)
        {
            foreach (var control in controls)
            {
                if (control.Parent is Panel panel)
                    panel.Children.Remove(control);
            }
        }

        // Access is granted in the browser, so the list loads again when the user comes back to the window.
        private void OpenManageAccess(Uri url)
        {
            reloadOnReturn = true;
            dialog.openUrl(url);
        }

        // Installing on an organization happens in the browser too, so the list loads again on return.
        private void AddOrganization()
        {
            reloadOnReturn = true;
            _ = dialog.model.AddOrganizationAsync(host);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            window = TopLevel.GetTopLevel(this) as Window;
            window?.Activated += OnWindowActivated;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            window?.Activated -= OnWindowActivated;
            window = null;
        }

        private void OnWindowActivated(object? sender, EventArgs e)
        {
            if (!reloadOnReturn || host.Account is null)
                return;

            reloadOnReturn = false;
            _ = LoadAsync(refresh: true);
        }

        private DockPanel RepositoriesView(string account)
        {
            Release(search, ownerChips, list, listStatus);
            var refresh = new Button { Classes = { "icon" }, Content = new SymbolIcon { Data = Icons.Refresh, Size = 15 }, VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(refresh, "Load the repositories again");
            refresh.Click += (_, _) => _ = LoadAsync(refresh: true);
            var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8, 0, 0, 0), Children = { refresh } };
            if (host.ManageAccessUrl is { } manageUrl)
            {
                var manage = IconButton(Icons.Shield, "Manage Access");
                manage.VerticalAlignment = VerticalAlignment.Center;
                ToolTip.SetTip(manage, $"Choose which repositories and organizations Runesmith may use, on {host.Name}");
                manage.Click += (_, _) => OpenManageAccess(manageUrl);
                tools.Children.Add(manage);
            }

            var who = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0),
                Children = { new Avatar(dialog.avatars, account, host.AccountAvatarUrl?.AbsoluteUri, 20), Label(account, "TextSecondaryBrush", 12) },
            };
            ToolTip.SetTip(who, $"Signed in to {host.Name} as {account}");
            tools.Children.Add(who);

            var top = new DockPanel { Margin = new Thickness(16, 14, 16, 10), Children = { tools, search } };
            DockPanel.SetDock(tools, Dock.Right);
            ownerChips.Margin = new Thickness(16, 0, 16, 10);
            var listArea = new Panel { Children = { list, listStatus } };
            var view = new DockPanel { Children = { top, ownerChips, listArea } };
            DockPanel.SetDock(top, Dock.Top);
            DockPanel.SetDock(ownerChips, Dock.Top);
            return view;
        }

        private void FillOwners()
        {
            ownerChips.Children.Clear();
            var owners = catalog?.Owners(host.Account) ?? [];
            var canAdd = dialog.model.CanAddOrganization(host);
            ownerChips.IsVisible = owners.Count > 1 || (canAdd && owners.Count > 0);
            if (owner is not null && !owners.Any(o => o.Login == owner))
                owner = null;

            ownerChips.Children.Add(Chip(null, "All", null));
            foreach (var candidate in owners)
                ownerChips.Children.Add(Chip(candidate.Login, candidate.Login, new Avatar(dialog.avatars, candidate.Login, candidate.AvatarUrl?.AbsoluteUri, 16, candidate.IsOrganization)));
            if (canAdd)
                ownerChips.Children.Add(AddChip());

            Button AddChip()
            {
                var chip = new Button { Classes = { "owner", "add" }, Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { new SymbolIcon { Data = Icons.Plus, Size = 12, VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = "Organization", VerticalAlignment = VerticalAlignment.Center } } } };
                ToolTip.SetTip(chip, $"Let Runesmith use the repositories of another organization on {host.Name}");
                chip.Click += (_, _) => AddOrganization();
                return chip;
            }

            ToggleButton Chip(string? login, string text, Control? icon)
            {
                var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } } };
                if (icon is not null)
                    content.Children.Insert(0, icon);
                var chip = new ToggleButton { Content = content, IsChecked = owner == login, Classes = { "owner" } };
                chip.Click += (_, _) =>
                {
                    owner = login;
                    foreach (var other in ownerChips.Children.OfType<ToggleButton>())
                        other.IsChecked = ReferenceEquals(other, chip);
                    Refill();
                };
                return chip;
            }
        }

        private void Refill()
        {
            if (catalog is null)
                return;

            var rows = catalog.Rows(host.Account, search.Text, owner);
            var selected = Selected;
            list.ItemsSource = rows;
            list.SelectedItem = rows.OfType<RepositoryRow>().FirstOrDefault(r => r.Repository.CloneUrl == selected?.CloneUrl);
            if (rows.Count > 0)
                list.ScrollIntoView(0);

            if (rows.Count > 0)
                ShowListStatus(null);
            else if (catalog.Repositories.Count == 0 && host.ManageAccessUrl is { } manageUrl)
                ShowListStatus(WithAddOrganization(Message(GitIcons.Get(host.Icon), "No repositories yet", "The account has no repositories Runesmith may use. Manage Access chooses more.",
                    "Manage Access", () => OpenManageAccess(manageUrl))));
            else if (catalog.Repositories.Count == 0)
                ShowListStatus(Message(GitIcons.Get(host.Icon), "No repositories yet", "The account has no repositories yet.", null, null));
            else
                ShowListStatus(Message(Icons.Search, "No repositories match", "Try other words, or show every owner.", null, null));
            dialog.UpdateTarget();
        }

        private StackPanel WithAddOrganization(StackPanel message)
        {
            if (dialog.model.CanAddOrganization(host))
            {
                var add = Link("Add an organization...", AddOrganization);
                add.HorizontalAlignment = HorizontalAlignment.Center;
                message.Children.Add(add);
            }

            return message;
        }

        private void ShowListStatus(Control? status)
        {
            listStatus.Content = status;
            listStatus.IsVisible = status is not null;
        }

        private static StackPanel Loading(string text)
        {
            var spinner = new SymbolIcon { Data = Icons.RotateCw, Size = 14, Classes = { "spin" }, VerticalAlignment = VerticalAlignment.Center };
            Brush(spinner, SymbolIcon.ForegroundProperty, "AccentBrush");
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { spinner, Label(text, "TextSecondaryBrush") } };
        }

        private static StackPanel Message(Geometry icon, string title, string hint, string? action, Action? click)
        {
            var symbol = Icon(icon, 28, "TextMutedBrush");
            symbol.HorizontalAlignment = HorizontalAlignment.Center;
            var heading = Label(title, weight: FontWeight.SemiBold);
            heading.HorizontalAlignment = HorizontalAlignment.Center;
            var text = Label(hint, "TextSecondaryBrush", wrap: true);
            text.TextAlignment = TextAlignment.Center;
            text.MaxWidth = 420;
            var panel = new StackPanel { Spacing = 8, Margin = new Thickness(24), Children = { symbol, heading, text } };
            if (action is not null && click is not null)
            {
                var button = new Button { Content = action, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
                button.Click += (_, _) => click();
                panel.Children.Add(button);
            }

            return panel;
        }

        private StackPanel OwnerView(OwnerRow group)
        {
            var name = Label(group.Owner.Login, "TextSecondaryBrush", 12, FontWeight.SemiBold);
            var kind = Label(group.IsAccount ? "Your account" : group.Owner.IsOrganization ? "Organization" : "User", "TextMutedBrush", 11);
            var count = Label(group.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), "TextDisabledBrush", 11);
            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(2, 0, 0, 6),
                Children = { new Avatar(dialog.avatars, group.Owner.Login, group.Owner.AvatarUrl?.AbsoluteUri, 18, group.Owner.IsOrganization), name, kind, count },
            };
        }

        private Grid RepositoryView(HostedRepository repository)
        {
            var name = Label(repository.Name, weight: FontWeight.SemiBold);
            var visibility = new Badge { Content = repository.IsPrivate ? PrivateLabel() : "Public", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var badges = new StackPanel { Orientation = Orientation.Horizontal, Children = { visibility } };
            if (repository.IsFork)
                badges.Children.Add(new Badge { Content = "Fork", Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            if (repository.IsArchived)
                badges.Children.Add(new Badge { Content = "Archived", Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            var titleLine = new StackPanel { Orientation = Orientation.Horizontal, Children = { name, badges } };
            var description = Label(string.IsNullOrWhiteSpace(repository.Description) ? "No description" : repository.Description, "TextMutedBrush", 12);
            var text = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { titleLine, description } };

            var facts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 4, 0) };
            if (repository.Language is { Length: > 0 } language)
            {
                var dot = new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(LanguageColors.For(language)), VerticalAlignment = VerticalAlignment.Center };
                facts.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { dot, Label(language, "TextSecondaryBrush", 12) } });
            }

            var stars = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, MinWidth = 44, Children = { Icon(Icons.Star, 13, "TextMutedBrush"), Label(Count(repository.Stars), "TextSecondaryBrush", 12) } };
            facts.Children.Add(stars);
            var updated = Label(Ago(repository.UpdatedAt, DateTimeOffset.UtcNow), "TextMutedBrush", 12);
            updated.MinWidth = 84;
            updated.TextAlignment = TextAlignment.Right;
            ToolTip.SetTip(updated, repository.UpdatedAt is { } changed ? $"Updated {changed.ToLocalTime():g}" : null);
            facts.Children.Add(updated);

            var avatar = new Avatar(dialog.avatars, repository.Owner, repository.OwnerAvatarUrl?.AbsoluteUri, 30, repository.OwnerIsOrganization);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,12,*,Auto"), Children = { avatar, text, facts } };
            Grid.SetColumn(text, 2);
            Grid.SetColumn(facts, 3);
            ToolTip.SetTip(grid, $"{repository.Owner}/{repository.Name}");
            ToolTip.SetShowDelay(grid, 900);
            return grid;
        }

        private static StackPanel PrivateLabel() => new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3,
            Children = { new SymbolIcon { Data = Icons.Lock, Size = 10, StrokeThickness = 2.4, VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = "Private" } },
        };

        private void OnSearchKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key is not (Key.Down or Key.Up) || list.ItemsSource is not IReadOnlyList<RepositoryListRow> rows)
                return;

            var items = rows.OfType<RepositoryRow>().ToList();
            var index = list.SelectedItem is RepositoryRow current ? items.IndexOf(current) : -1;
            var next = e.Key == Key.Down ? Math.Min(index + 1, items.Count - 1) : Math.Max(index - 1, 0);
            if (next >= 0 && next < items.Count)
            {
                list.SelectedItem = items[next];
                list.ScrollIntoView(items[next]);
            }

            e.Handled = true;
        }

        private void OnSelectionChanged()
        {
            if (list.SelectedItem is OwnerRow)
            {
                list.SelectedItem = null;
                return;
            }

            dialog.UpdateTarget();
        }
    }
}
