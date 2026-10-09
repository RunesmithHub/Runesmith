using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HammerUI;
using HammerUI.Controls;
using HammerUI.Services;
using Runesmith.Git.Commands;
using Runesmith.Git.Hosting;
using Runesmith.Sdk.VersionControl;
using static Runesmith.Plugins.Views.ViewHelpers;

namespace Runesmith.Git.Views.PullRequests;

/// <summary>The Create Pull Request dialog: the current branch into a base branch, with a title and description prefilled from the commits,
/// and Draft. It pushes the branch first when the host does not have it yet, after asking, and closes with the
/// created <see cref="PullRequest"/>.</summary>
internal sealed class CreatePullRequestDialog : Dialog, IDisposable
{
    private readonly PullRequestService service;
    private readonly IDialogService dialogs;
    private readonly PullRequestContext context;
    private readonly ComboBox baseBox = new() { MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = false };
    private readonly TextBox title = new() { PlaceholderText = "Title" };
    private readonly TextBox body = new() { PlaceholderText = "Describe the change: what it does and why", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 180, VerticalContentAlignment = VerticalAlignment.Top };
    private readonly CheckBox draft = new() { Content = "Create as a draft" };
    private readonly TextBlock status = Label(null, "TextMutedBrush", 12, wrap: true);
    private readonly SymbolIcon spinner = new() { Data = Icons.RotateCw, Size = 13, Classes = { "spin" }, IsVisible = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button create = new() { Content = "Create Pull Request", Classes = { "accent" }, IsDefault = true, IsEnabled = false };
    private readonly CancellationTokenSource closing = new();
    private bool titleTyped;
    private bool bodyTyped;
    private bool filling;
    private bool isBusy;
    private bool isDisposed;

    public CreatePullRequestDialog(PullRequestService service, IDialogService dialogs, PullRequestContext context)
    {
        this.service = service;
        this.dialogs = dialogs;
        this.context = context;
        GitIcons.Register();
        Header = "Create Pull Request";
        Description = $"Ask to merge {context.Repository.Branch} into another branch of {WebLinks.RepositoryName(context.Remote.FetchUrl)} on {context.Host.Name}.";
        Width = 620;
        ShowCloseButton = true;
        draft.IsVisible = context.Host.SupportsDraftPullRequests;

        var head = new Border { Padding = new Thickness(10, 0), Height = 32, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
        head.Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { Icon(GitIcons.Get(GitIcons.Branch), 14), Label(context.Repository.Branch, weight: FontWeight.Medium) } };
        Brush(head, Border.BackgroundProperty, "SurfaceSunkenBrush");
        Brush(head, Border.BorderBrushProperty, "BorderSubtleBrush");
        var arrow = Icon(Icons.ArrowLeft, 16, "TextMutedBrush");
        arrow.Margin = new Thickness(10, 0);
        var branches = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), Children = { baseBox, arrow, head } };
        Grid.SetColumn(arrow, 1);
        Grid.SetColumn(head, 2);
        var branchLabels = new Grid { ColumnDefinitions = new ColumnDefinitions("*,36,*"), Children = { Label("Base: merge into", "TextSecondaryBrush", 12), Label("From", "TextSecondaryBrush", 12) } };
        Grid.SetColumn(branchLabels.Children[1], 2);

        var form = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                branchLabels, branches,
                Spaced(Label("Title", "TextSecondaryBrush", 12), 8), title,
                Spaced(Label("Description", "TextSecondaryBrush", 12), 8), body,
                Spaced(draft, 4),
            },
        };
        Content = form;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var cancel = new Button { Content = "Cancel", MinWidth = 84, IsCancel = true };
        cancel.Click += (_, _) => Close();
        buttons.Children.AddRange([cancel, create]);
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, MaxWidth = 300, VerticalAlignment = VerticalAlignment.Center, Children = { spinner, status } };
        var footer = new DockPanel { Children = { buttons, left } };
        DockPanel.SetDock(buttons, Dock.Right);
        Footer = footer;

        title.TextChanged += (_, _) =>
        {
            titleTyped |= !filling;
            UpdateCreate();
        };
        body.TextChanged += (_, _) => bodyTyped |= !filling;
        baseBox.SelectionChanged += (_, _) => _ = PrefillAsync();
        create.Click += (_, _) => _ = CreateAsync();
        AttachedToVisualTree += (_, _) => _ = LoadAsync();
        DetachedFromVisualTree += (_, _) => Dispose();
    }

    protected override Type StyleKeyOverride => typeof(Dialog);

    public void Dispose()
    {
        if (isDisposed)
            return;

        isDisposed = true;
        closing.Cancel();
        closing.Dispose();
    }

    private static Control Spaced(Control control, double top)
    {
        control.Margin = new Thickness(0, top, 0, 0);
        return control;
    }

    private async Task LoadAsync()
    {
        SetBusy(true, $"Reading the branches on {context.Host.Name}...");
        try
        {
            var (defaultBranch, names) = await service.GetBranchesAsync(context, closing.Token);
            baseBox.ItemsSource = names.Where(name => name != context.Repository.Branch).ToList();
            baseBox.SelectedItem = defaultBranch != context.Repository.Branch ? defaultBranch : baseBox.Items.Cast<string>().FirstOrDefault();
            baseBox.IsEnabled = true;
            SetBusy(false, null);
        }
        catch (OperationCanceledException) when (closing.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetBusy(false, exception.Message, isError: true);
        }
    }

    private async Task PrefillAsync()
    {
        if (baseBox.SelectedItem is not string baseBranch)
            return;

        UpdateCreate();
        var drafted = await service.DraftAsync(context, baseBranch, closing.Token);
        filling = true;
        if (!titleTyped)
            title.Text = drafted.Title;
        if (!bodyTyped)
            body.Text = drafted.Body;
        filling = false;
        UpdateCreate();
    }

    private void UpdateCreate() => create.IsEnabled = !isBusy && baseBox.SelectedItem is string && !string.IsNullOrWhiteSpace(title.Text);

    private async Task CreateAsync()
    {
        if (isBusy || baseBox.SelectedItem is not string baseBranch || string.IsNullOrWhiteSpace(title.Text))
            return;

        var repository = context.Repository;
        var needsPush = repository.Upstream is null || repository.Ahead > 0;
        if (needsPush)
        {
            var question = repository.Upstream is null
                ? $"{context.Host.Name} does not have {repository.Branch} yet. Push it to {context.Remote.Name} first?"
                : $"{repository.Branch} has {repository.Ahead} commit{(repository.Ahead == 1 ? "" : "s")} that {context.Host.Name} does not have yet. Push them first?";
            if (!await dialogs.ConfirmAsync("Push the branch first?", question, "Push and Create"))
                return;
        }

        try
        {
            if (needsPush)
            {
                SetBusy(true, $"Pushing {repository.Branch}...");
                await service.PushAsync(context, closing.Token);
            }

            SetBusy(true, "Creating the pull request...");
            var created = await context.Host.CreatePullRequestAsync(context.Remote.FetchUrl,
                new PullRequestDraft(repository.Branch!, baseBranch, title.Text!.Trim(), body.Text?.Trim() ?? "", draft.IsChecked == true), closing.Token);
            Close(created);
        }
        catch (OperationCanceledException) when (closing.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetBusy(false, exception.Message, isError: true);
        }
    }

    private void SetBusy(bool busy, string? text, bool isError = false)
    {
        isBusy = busy;
        spinner.IsVisible = busy;
        status.Text = text;
        Brush(status, TextBlock.ForegroundProperty, isError ? "DangerBrush" : "TextMutedBrush");
        title.IsEnabled = body.IsEnabled = draft.IsEnabled = !busy;
        UpdateCreate();
    }
}
