using System.Composition;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Git.Commands;
using Runesmith.Git.Hosting;
using Runesmith.Plugins.Views;
using Runesmith.Sdk.ToolWindows;
using Runesmith.Sdk.VersionControl;
using static Runesmith.Plugins.Views.ViewHelpers;

namespace Runesmith.Git.Views.PullRequests;

/// <summary>The Pull Requests tool window, on the right: the open pull requests of the repository on the host that owns its remote.</summary>
[Export(typeof(IToolWindowProvider))]
[method: ImportingConstructor]
internal sealed class PullRequestsToolWindow(
    PullRequestService service,
    PullRequestActions actions,
    GitAvatarCache avatars,
    [Import(AllowDefault = true)] Lazy<IToolWindowManager>? toolWindows) : IToolWindowProvider
{
    public const string Id = "git.pullRequests";

    public ToolWindowDefinition Definition { get; } = Create();

    public Control CreateContent() => new PullRequestsView(service, actions, avatars, toolWindows);

    private static ToolWindowDefinition Create()
    {
        GitIcons.Register();
        return new ToolWindowDefinition(Id, "Pull Requests", GitIcons.PullRequest, DockSide.Right) { Order = 20 };
    }
}

/// <summary>The content of the Pull Requests tool window: a header with the repository and Refresh, then the list or what is missing.</summary>
internal sealed class PullRequestsView : DockPanel
{
    private readonly PullRequestsModel model;
    private readonly PullRequestActions actions;
    private readonly AvatarCache avatars;
    private readonly Lazy<IToolWindowManager>? toolWindows;
    private readonly TextBlock repositoryName = Label(null, "TextSecondaryBrush", 12, FontWeight.SemiBold);
    private readonly Button refresh = new() { Classes = { "icon", "small" }, Content = new SymbolIcon { Data = Icons.Refresh, Size = 14 } };
    private readonly Button create = new() { Classes = { "icon", "small" }, Content = new SymbolIcon { Data = Icons.Plus, Size = 14 } };
    private readonly ListBox list = new() { Classes = { "pull-requests" }, Background = Brushes.Transparent, Padding = new Thickness(6, 0, 6, 6) };
    private readonly ContentControl status = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch };
    private int generation;

    public PullRequestsView(PullRequestService service, PullRequestActions actions, AvatarCache avatars, Lazy<IToolWindowManager>? toolWindows)
    {
        model = new PullRequestsModel(service);
        this.actions = actions;
        this.avatars = avatars;
        this.toolWindows = toolWindows;
        GitIcons.Register();
        ToolTip.SetTip(refresh, "Refresh");
        ToolTip.SetTip(create, "Create Pull Request...");
        refresh.Click += (_, _) => _ = LoadAsync(force: true);
        create.Click += (_, _) => _ = actions.CreateAsync();
        actions.Created += (_, _) => _ = LoadAsync(force: true);

        var header = new DockPanel { Margin = new Thickness(12, 6, 6, 6), Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { create, refresh } }, repositoryName } };
        DockPanel.SetDock(header.Children[0], Dock.Right);
        DockPanel.SetDock(header, Dock.Top);
        list.ItemTemplate = new FuncDataTemplate<PullRequest>((item, _) => item is null ? new Panel() : Row(item));
        list.DoubleTapped += (_, _) =>
        {
            if (list.SelectedItem is PullRequest item)
                actions.OpenUrl(item.WebUrl);
        };
        list.ContainerPrepared += (_, e) =>
        {
            if (e.Container.DataContext is PullRequest item)
                e.Container.ContextMenu = Menu(item);
        };
        Children.AddRange([header, new Panel { Children = { list, status } }]);

        service.Hosts.Changed += (_, _) => Dispatcher.UIThread.Post(() => _ = LoadAsync(force: false));
        if (service.Repositories is { } repositories)
            repositories.Changed += (_, _) => _ = LoadAsync(force: false);
        AttachedToVisualTree += (_, _) => _ = LoadAsync(force: false);
    }

    private async Task LoadAsync(bool force)
    {
        var current = ++generation;
        var context = model.Context;
        repositoryName.Text = context is null ? "" : WebLinks.RepositoryName(context.Remote.FetchUrl);
        ToolTip.SetTip(repositoryName, context is null ? null : $"{WebLinks.RepositoryName(context.Remote.FetchUrl)} on {context.Host.Name}");
        create.IsVisible = refresh.IsVisible = context is { Host.Account: not null };

        var state = model.Ready(force);
        if (state is null)
        {
            if (!model.IsShowing(context!))
                Show(null, Loading());
            state = await model.LoadAsync(CancellationToken.None);
            if (current != generation)
                return;
        }

        switch (state)
        {
            case NotHostedState notHosted:
                Show(null, Empty(GitIcons.Get(GitIcons.PullRequest), "No hosted repository",
                    notHosted.HasRepository ? "This repository has no remote on a hosting service Runesmith knows." : "Open a folder that is a Git repository with a remote on a hosting service, such as GitHub.",
                    null, null));
                SetBadge(null);
                break;
            case SignedOutState signedOut:
                Show(null, Empty(GitIcons.Get(signedOut.Host.Icon), "Sign in to see pull requests", $"Pull requests, their reviews and checks come from your {signedOut.Host.Name} account.",
                    $"Sign in to {signedOut.Host.Name}", () => _ = PullRequestActions.SignInAsync(signedOut.Host)));
                SetBadge(null);
                break;
            case LoadedState loaded:
                Show(loaded.Items, loaded.Items.Count == 0
                    ? Empty(GitIcons.Get(GitIcons.PullRequest), "No open pull requests", "Pull requests for this repository show here.", "Create Pull Request...", () => _ = actions.CreateAsync())
                    : null);
                SetBadge(loaded.Items.Count > 0 ? loaded.Items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
                break;
            case FailedState failed:
                Show(null, Empty(Icons.AlertTriangle, "Pull requests could not be loaded", failed.Message, "Try Again", () => _ = LoadAsync(force: true)));
                break;
        }
    }

    private void SetBadge(string? badge) => toolWindows?.Value.SetBadge(PullRequestsToolWindow.Id, badge);

    private void Show(IReadOnlyList<PullRequest>? items, Control? message)
    {
        list.ItemsSource = items;
        list.IsVisible = items is { Count: > 0 };
        status.Content = message;
        status.IsVisible = message is not null;
    }

    private static StackPanel Loading()
    {
        var spinner = new SymbolIcon { Data = Icons.RotateCw, Size = 14, Classes = { "spin" }, VerticalAlignment = VerticalAlignment.Center };
        Brush(spinner, SymbolIcon.ForegroundProperty, "AccentBrush");
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Children = { spinner, Label("Loading pull requests...", "TextSecondaryBrush") } };
    }

    private static StackPanel Empty(Geometry icon, string title, string hint, string? action, Action? click)
    {
        var symbol = Icon(icon, 26, "TextMutedBrush");
        symbol.HorizontalAlignment = HorizontalAlignment.Center;
        var heading = Label(title, weight: FontWeight.SemiBold);
        heading.HorizontalAlignment = HorizontalAlignment.Center;
        var text = Label(hint, "TextMutedBrush", 12, wrap: true);
        text.TextAlignment = TextAlignment.Center;
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(20, 0), Children = { symbol, heading, text } };
        if (action is not null && click is not null)
        {
            var button = new Button { Content = action, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0), Classes = { "small" } };
            button.Click += (_, _) => click();
            panel.Children.Add(button);
        }

        return panel;
    }

    private Grid Row(PullRequest item)
    {
        var title = Label(item.Title, weight: FontWeight.Medium);
        ToolTip.SetTip(title, item.Title);
        var number = Label($"#{item.Number}", "TextMutedBrush", 12);
        var branchIcon = Icon(GitIcons.Get(GitIcons.Branch), 11, "TextMutedBrush");
        branchIcon.Margin = new Thickness(0, 0, 3, 0);
        var branch = new DockPanel { Children = { branchIcon, Label(item.HeadBranch, "TextMutedBrush", 12) } };
        DockPanel.SetDock(branchIcon, Dock.Left);
        ToolTip.SetTip(branch, $"{item.HeadBranch} into {item.BaseBranch}");
        var when = Label(Ago(item.UpdatedAt, DateTimeOffset.UtcNow), "TextMutedBrush", 12);
        number.Margin = new Thickness(0, 0, 8, 0);
        when.Margin = new Thickness(8, 0, 0, 0);
        var meta = new DockPanel { Children = { number, when, branch } };
        DockPanel.SetDock(number, Dock.Left);
        DockPanel.SetDock(when, Dock.Right);

        var badges = new WrapPanel { ItemSpacing = 4, LineSpacing = 4, Margin = new Thickness(0, 2, 0, 0) };
        if (item.IsDraft)
            badges.Children.Add(new Badge { Content = "Draft" });
        if (ReviewBadge(item.Review) is { } review)
            badges.Children.Add(review);
        if (ChecksBadge(item.Checks) is { } checks)
            badges.Children.Add(checks);
        badges.IsVisible = badges.Children.Count > 0;

        var text = new StackPanel { Spacing = 3, Children = { title, meta, badges } };
        var avatar = new Avatar(avatars, item.Author, item.AuthorAvatarUrl?.AbsoluteUri, 24);
        avatar.VerticalAlignment = VerticalAlignment.Top;
        avatar.Margin = new Thickness(0, 1, 0, 0);
        ToolTip.SetTip(avatar, item.Author);

        var checkout = new Button { Classes = { "icon", "small" }, Content = new SymbolIcon { Data = GitIcons.Get(GitIcons.Branch), Size = 13 }, VerticalAlignment = VerticalAlignment.Top };
        ToolTip.SetTip(checkout, "Checkout");
        checkout.Click += (_, _) => _ = actions.CheckoutAsync(item);
        var open = new Button { Classes = { "icon", "small" }, Content = new SymbolIcon { Data = Icons.ExternalLink, Size = 13 }, VerticalAlignment = VerticalAlignment.Top };
        ToolTip.SetTip(open, "Open in Browser");
        open.Click += (_, _) => actions.OpenUrl(item.WebUrl);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { checkout, open }, IsVisible = false, Margin = new Thickness(4, 0, 0, 0) };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,10,*,Auto"), Margin = new Thickness(0, 8), Background = Brushes.Transparent, Children = { avatar, text, buttons } };
        Grid.SetColumn(text, 2);
        Grid.SetColumn(buttons, 3);
        grid.PointerEntered += (_, _) => buttons.IsVisible = true;
        grid.PointerExited += (_, _) => buttons.IsVisible = false;
        return grid;
    }

    private static Badge? ReviewBadge(ReviewState review) => review switch
    {
        ReviewState.Approved => WithIcon(Icons.Check, "Approved", "success"),
        ReviewState.ChangesRequested => WithIcon(Icons.AlertCircle, "Changes requested", "danger"),
        ReviewState.ReviewRequired => WithIcon(Icons.Eye, "Review required", null),
        _ => null,
    };

    private static Badge? ChecksBadge(ChecksState checks) => checks switch
    {
        ChecksState.Passing => WithIcon(Icons.CheckCircle, "Passing", "success", "All checks passed"),
        ChecksState.Failing => WithIcon(Icons.X, "Failing", "danger", "Some checks failed"),
        ChecksState.Pending => WithIcon(GitIcons.Get(GitIcons.CircleDot), "Running", "warning", "Checks are running"),
        _ => null,
    };

    private static Badge WithIcon(Geometry icon, string text, string? kind, string? tip = null)
    {
        var badge = new Badge
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Children = { new SymbolIcon { Data = icon, Size = 10, StrokeThickness = 2.4, VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = text } } },
        };
        if (kind is not null)
            badge.Classes.Add(kind);
        ToolTip.SetTip(badge, tip);
        return badge;
    }

    private ContextMenu Menu(PullRequest item)
    {
        var open = new MenuItem { Header = "Open in Browser", Icon = new SymbolIcon { Data = Icons.ExternalLink, Size = 14 } };
        open.Click += (_, _) => actions.OpenUrl(item.WebUrl);
        var checkout = new MenuItem { Header = "Checkout", Icon = new SymbolIcon { Data = GitIcons.Get(GitIcons.Branch), Size = 14 } };
        checkout.Click += (_, _) => _ = actions.CheckoutAsync(item);
        var copy = new MenuItem { Header = "Copy Link", Icon = new SymbolIcon { Data = Icons.Copy, Size = 14 } };
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                await clipboard.SetTextAsync(item.WebUrl.AbsoluteUri);
        };
        return new ContextMenu { Items = { open, checkout, new Separator(), copy } };
    }
}
