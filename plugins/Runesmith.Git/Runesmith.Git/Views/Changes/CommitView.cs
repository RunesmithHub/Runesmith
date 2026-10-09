using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
using Runesmith.Git.Commands;
using Runesmith.Git.Git;
using Runesmith.Git.Repositories;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Settings;
using Runesmith.Sdk.Shell;

namespace Runesmith.Git.Views.Changes;

/// <summary>The Commit tool window's content: the changes as a tree with check boxes that stage them, the message with Amend, and Commit and
/// Commit and Push.</summary>
internal sealed class CommitView : DockPanel
{
    private const int RecentMessageCount = 15;

    private readonly GitOperations operations;
    private readonly RepositoryService repositories;
    private readonly ISettingsService settings;
    private readonly ICommandService commands;
    private readonly IEditorService editors;
    private readonly ILanguageRegistry? languages;
    private readonly ILauncher? launcher;
    private readonly HashSet<string> collapsed = new(StringComparer.Ordinal);
    private readonly TreeView tree;
    private readonly EmptyState noChanges;
    private readonly Grid workArea;
    private readonly Banner stateBanner = new() { IsDismissible = false };
    private readonly Banner errorBanner = new();
    private readonly TextBox message;
    private readonly TextBlock subjectHint = new() { Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    private readonly CheckBox amend = new() { Content = "Amend", VerticalAlignment = VerticalAlignment.Center };
    private readonly Button commit = new() { Content = "Commit", Classes = { "accent" }, MinWidth = 84 };
    private readonly Button commitAndPush = new() { Content = "Commit and Push", MinWidth = 120 };
    private readonly TextBlock summary = new() { Classes = { "caption", "muted" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly MenuItem signoff = new() { Header = "Sign Off", ToggleType = MenuItemToggleType.CheckBox };
    private readonly MenuItem skipHooks = new() { Header = "Skip Hooks", ToggleType = MenuItemToggleType.CheckBox };
    private string typedBeforeAmend = "";
    private bool isCommitting;
    private bool settingMessage;

    public CommitView(GitOperations operations, ISettingsService settings, ICommandService commands, IEditorService editors, ILanguageRegistry? languages, ILauncher? launcher)
    {
        this.operations = operations;
        repositories = operations.Repositories;
        this.settings = settings;
        this.commands = commands;
        this.editors = editors;
        this.languages = languages;
        this.launcher = launcher;

        tree = new TreeView
        {
            SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncTreeDataTemplate<ChangeNode?>((node, _) => node is null ? new Panel() : Row(node), node => node?.Children ?? []),
        };
        tree.Styles.Add(new Style(x => x.OfType<TreeViewItem>())
        {
            Setters = { new Setter(TreeViewItem.IsExpandedProperty, new Binding(nameof(ChangeNode.IsExpanded)) { Mode = BindingMode.TwoWay }) },
        });
        tree.DoubleTapped += (_, e) =>
        {
            if (e.Source is Visual visual && visual.FindAncestorOfType<CheckBox>() is null && tree.SelectedItem is ChangeNode { Entry: { } entry } node)
                _ = operations.ShowDiffAsync(entry, node.Group == ChangeGroup.Staged);
        };
        tree.KeyDown += OnTreeKeyDown;
        tree.ContextRequested += (_, _) => tree.ContextMenu = ContextMenuFor(SelectedFiles());
        // Avalonia opens a context menu only from a control that had one when the request began, so each starts with an empty one.
        tree.ContextMenu = new ContextMenu();

        noChanges = new EmptyState
        {
            Icon = Icons.CheckCircle,
            Title = "No changes",
            Hint = "The working tree matches the last commit. Edit files and they show here, ready to commit.",
            IsVisible = false,
        };

        message = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            PlaceholderText = "Commit message",
            MinHeight = 92,
            MaxHeight = 260,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        message.TextChanged += (_, _) => OnMessageChanged();
        message.AddHandler(KeyDownEvent, OnMessageKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        amend.IsCheckedChanged += (_, _) => _ = OnAmendChangedAsync();
        ToolTip.SetTip(amend, "Replace the last commit with the staged changes and this message");
        commit.Click += (_, _) => _ = CommitAsync(push: false);
        commitAndPush.Click += (_, _) => _ = CommitAsync(push: true);
        ToolTip.SetTip(commit, "Commit the staged changes (Ctrl+Enter)");
        ToolTip.SetTip(commitAndPush, "Commit, then push to the upstream branch (Ctrl+Shift+Enter)");

        var recent = IconButton(GitIcons.History, "Recent messages");
        recent.Flyout = DynamicMenu(FillRecentMessages);
        var options = IconButton("settings", "Commit options");
        options.Flyout = new MenuFlyout { Placement = PlacementMode.TopEdgeAlignedRight, Items = { signoff, skipHooks } };
        ToolTip.SetTip(signoff, "Add a Signed-off-by line with your name and address");
        ToolTip.SetTip(skipHooks, "Do not run the pre-commit and commit-msg hooks");

        var messageTools = new DockPanel { Margin = new Thickness(0, 6, 0, 0), LastChildFill = false };
        DockPanel.SetDock(amend, Dock.Left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { recent, options } };
        DockPanel.SetDock(right, Dock.Right);
        messageTools.Children.AddRange([amend, right]);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0), Children = { commit, commitAndPush } };
        var heading = new DockPanel { Margin = new Thickness(2, 0, 2, 6) };
        DockPanel.SetDock(subjectHint, Dock.Right);
        heading.Children.AddRange([subjectHint, summary]);

        var messageArea = new StackPanel { Margin = new Thickness(8, 8, 8, 8), Children = { heading, message, messageTools, buttons } };
        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Rows, Height = 4, Background = Brushes.Transparent };
        workArea = new Grid { RowDefinitions = new RowDefinitions("*,Auto,Auto") };
        var treeArea = new Panel { Children = { tree, noChanges } };
        Grid.SetRow(splitter, 1);
        Grid.SetRow(messageArea, 2);
        var divider = new Border { Classes = { "divider-h" }, VerticalAlignment = VerticalAlignment.Top };
        Grid.SetRow(divider, 2);
        workArea.Children.AddRange([treeArea, splitter, divider, messageArea]);

        var banners = new StackPanel { Children = { stateBanner, errorBanner } };
        DockPanel.SetDock(banners, Dock.Top);
        Children.AddRange([banners, workArea]);

        signoff.Click += (_, _) => signoff.IsChecked = !signoff.IsChecked;
        skipHooks.Click += (_, _) => skipHooks.IsChecked = !skipHooks.IsChecked;
        repositories.Changed += (_, _) => Update();
        operations.FocusMessageRequested += (_, _) => FocusMessage();
        settings.Changed += (_, e) =>
        {
            if (e.Key == GitSettings.SubjectLength)
                OnMessageChanged();
        };
        Update();
    }

    /// <summary>Puts the keyboard focus in the message box, at its end.</summary>
    public void FocusMessage()
    {
        if (!workArea.IsVisible)
            return;
        message.Focus();
        message.CaretIndex = message.Text?.Length ?? 0;
    }

    private void Update()
    {
        var hasRepository = repositories.Current is not null;
        workArea.IsVisible = hasRepository;
        if (!hasRepository || repositories.Status is not { } status)
        {
            tree.ItemsSource = null;
            stateBanner.Hide();
            UpdateButtons();
            return;
        }

        var selected = tree.SelectedItems?.OfType<ChangeNode>().Select(n => n.Key).ToHashSet(StringComparer.Ordinal) ?? [];
        var groups = ChangeNode.Build(status, collapsed);
        foreach (var node in groups.SelectMany(Flatten))
            node.PropertyChanged += (_, e) => Remember(node);
        tree.ItemsSource = groups;
        if (selected.Count > 0 && tree.SelectedItems is { } items)
        {
            foreach (var node in groups.SelectMany(Flatten).Where(n => selected.Contains(n.Key)))
                items.Add(node);
        }

        noChanges.IsVisible = groups.Count == 0;
        tree.IsVisible = groups.Count > 0;
        UpdateState(status);
        UpdateButtons();
    }

    private void UpdateState(StatusSnapshot status)
    {
        var conflicts = status.Conflicts.Count();
        switch (repositories.Ongoing)
        {
            case OngoingOperation.None:
                stateBanner.Hide();
                break;
            case OngoingOperation.Merge:
                stateBanner.Show(conflicts > 0 ? BannerTone.Warning : BannerTone.Info, "Merging",
                    conflicts > 0 ? $"Resolve the conflicts in {Files(conflicts)}, stage them, then commit to finish the merge." : "The conflicts are resolved. Commit to finish the merge.",
                    ("Abort Merge", () => _ = operations.AbortAsync()));
                if (string.IsNullOrWhiteSpace(message.Text) && repositories.Repository is { } repository && File.Exists(Path.Combine(repository.GitDirectory, "MERGE_MSG")))
                    SetMessage(string.Join('\n', File.ReadAllLines(Path.Combine(repository.GitDirectory, "MERGE_MSG")).Where(l => !l.StartsWith('#'))).Trim());
                break;
            default:
                var name = repositories.Ongoing == OngoingOperation.CherryPick ? "Cherry-picking" : repositories.Ongoing == OngoingOperation.Rebase ? "Rebasing" : "Reverting";
                stateBanner.Show(conflicts > 0 ? BannerTone.Warning : BannerTone.Info, name,
                    conflicts > 0 ? $"Resolve the conflicts in {Files(conflicts)} and stage them, then continue." : "The conflicts are resolved. Continue to go on.",
                    ("Continue", () => _ = operations.ContinueAsync()), ("Abort", () => _ = operations.AbortAsync()));
                break;
        }
    }

    private void UpdateButtons()
    {
        var status = repositories.Status;
        var staged = status?.Staged.Count() ?? 0;
        var total = status?.ChangedCount ?? 0;
        var hasMessage = !string.IsNullOrWhiteSpace(message.Text);
        var canCommit = !isCommitting && repositories.Current is not null && hasMessage && (total > 0 || amend.IsChecked == true || repositories.Ongoing == OngoingOperation.Merge);
        commit.IsEnabled = canCommit;
        commitAndPush.IsEnabled = canCommit && repositories.Current?.Remotes.Count > 0;
        commit.Content = amend.IsChecked == true ? "Amend Commit" : "Commit";
        summary.Text = total == 0 ? "" : staged == 0 ? $"{Files(total)} changed, none staged" : $"{staged} of {Files(total)} staged";
    }

    private void OnMessageChanged()
    {
        if (!settingMessage && amend.IsChecked != true)
            typedBeforeAmend = message.Text ?? "";
        var subject = (message.Text ?? "").Split('\n')[0].TrimEnd('\r');
        var limit = settings.Get<int>(GitSettings.SubjectLength);
        subjectHint.IsVisible = subject.Length > limit;
        subjectHint.Text = string.Create(CultureInfo.CurrentCulture, $"Subject {subject.Length} / {limit}");
        subjectHint[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("WarningBrush");
        ToolTip.SetTip(subjectHint, $"Subjects of {limit} characters or fewer read best in logs; move the rest to the body, after an empty line.");
        UpdateButtons();
    }

    private async Task OnAmendChangedAsync()
    {
        if (amend.IsChecked == true)
        {
            typedBeforeAmend = message.Text ?? "";
            if (repositories.Repository is { } repository && await repository.GetLastMessageAsync(CancellationToken.None) is { } last && amend.IsChecked == true)
                SetMessage(last);
        }
        else
        {
            SetMessage(typedBeforeAmend);
        }

        UpdateButtons();
    }

    private void SetMessage(string text)
    {
        settingMessage = true;
        message.Text = text;
        settingMessage = false;
    }

    private async Task CommitAsync(bool push)
    {
        var text = message.Text?.Trim() ?? "";
        if (text.Length == 0 || isCommitting || repositories.Status is not { } status)
            return;

        if (!status.Staged.Any() && amend.IsChecked != true && repositories.Ongoing != OngoingOperation.Merge)
        {
            errorBanner.Show(BannerTone.Info, "Nothing is staged", "Check the files to commit, or stage every change and commit them all.",
                ($"Stage All ({Files(status.ChangedCount - status.Conflicts.Count())}) and Commit", () => _ = StageAllAndCommitAsync(push)));
            return;
        }

        isCommitting = true;
        errorBanner.Hide();
        UpdateButtons();
        try
        {
            await operations.CommitAsync(text, new CommitOptions(amend.IsChecked == true, signoff.IsChecked, skipHooks.IsChecked), push);
            typedBeforeAmend = "";
            SetMessage("");
            amend.IsChecked = false;
        }
        catch (GitException exception)
        {
            ShowCommitError(exception);
        }
        finally
        {
            isCommitting = false;
            UpdateButtons();
        }
    }

    private async Task StageAllAndCommitAsync(bool push)
    {
        errorBanner.Hide();
        if (repositories.Repository is not { } repository || repositories.Status is not { } status)
            return;

        await StageAsync(repository, [.. status.Entries.Where(e => e.Kind is not StatusKind.Ignored and not StatusKind.Unmerged)]);
        await CommitAsync(push);
    }

    private void ShowCommitError(GitException exception)
    {
        switch (exception.Kind)
        {
            case GitErrorKind.NothingToCommit:
                errorBanner.Show(BannerTone.Info, "Nothing to commit", exception.Message);
                break;
            case GitErrorKind.Conflict:
                errorBanner.Show(BannerTone.Warning, "Resolve the conflicts first", exception.Message);
                break;
            default:
                var fromHook = exception.Result.Error.Contains("hook", StringComparison.OrdinalIgnoreCase);
                errorBanner.Show(BannerTone.Error, fromHook ? "A hook stopped the commit" : "The commit failed", exception.Message,
                    fromHook && !skipHooks.IsChecked ? [("Commit Without Hooks", () =>
                    {
                        skipHooks.IsChecked = true;
                        _ = CommitAsync(push: false);
                    })] : []);
                break;
        }
    }

    private void OnMessageKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
            return;
        e.Handled = true;
        _ = CommitAsync(push: e.KeyModifiers.HasFlag(KeyModifiers.Shift));
    }

    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        var files = SelectedFiles();
        if (files.Count == 0)
            return;

        switch (e.Key)
        {
            case Key.Space:
                _ = ToggleAsync(files, stage: files.Any(f => f.Group != ChangeGroup.Staged));
                break;
            case Key.Enter when files is [{ Entry: { } entry } file]:
                _ = operations.ShowDiffAsync(entry, file.Group == ChangeGroup.Staged);
                break;
            case Key.Delete:
                _ = operations.RollbackAsync([.. files.Select(f => f.Entry!).Distinct()]);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private StackPanel Row(ChangeNode node)
    {
        var check = new CheckBox { IsChecked = node.IsStaged, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(0), MinWidth = 0, Margin = new Thickness(0, 0, 6, 0) };
        ToolTip.SetTip(check, node.IsStaged ? "Unstage" : "Stage");
        check.Click += (_, _) =>
        {
            var targets = tree.SelectedItems?.OfType<ChangeNode>().Contains(node) == true ? SelectedFiles() : [.. node.Files()];
            _ = ToggleAsync(targets, stage: !node.IsStaged);
        };

        var panel = new StackPanel { Orientation = Orientation.Horizontal, Children = { check } };
        if (node.IsGroup)
        {
            panel.Children.Add(new TextBlock { Text = node.Name, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(new TextBlock
            {
                Text = node.FileCount.ToString(CultureInfo.CurrentCulture),
                Classes = { "caption", "muted" },
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            });
            return panel;
        }

        if (node.Entry is not { } entry)
        {
            var folder = new SymbolIcon { Data = Icons.Folder, Size = 14, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            folder[!SymbolIcon.ForegroundProperty] = new DynamicResourceExtension("TextMutedBrush");
            panel.Children.Add(folder);
            panel.Children.Add(new TextBlock { Text = node.Name, VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(new TextBlock
            {
                Text = node.FileCount.ToString(CultureInfo.CurrentCulture),
                Classes = { "caption", "muted" },
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            });
            return panel;
        }

        var full = repositories.Repository?.FullPath(entry.Path) ?? entry.Path;
        var icon = new SymbolIcon
        {
            Data = Icons.Find(languages?.GetLanguageForFile(full).Icon) ?? Icons.File,
            Size = 14,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        icon[!SymbolIcon.ForegroundProperty] = new DynamicResourceExtension("TextSecondaryBrush");
        var brush = LetterBrush(node.Letter);
        var name = new TextBlock { Text = node.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        name[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(brush == "TextSecondaryBrush" ? "TextPrimaryBrush" : brush);
        if (node.Letter == 'D')
            name.TextDecorations = TextDecorations.Strikethrough;
        var letter = new TextBlock
        {
            Text = node.Letter.ToString(),
            FontWeight = FontWeight.SemiBold,
            Classes = { "mono" },
            Width = 14,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        letter[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(brush);
        ToolTip.SetTip(letter, Describe(node));
        panel.Children.AddRange([icon, name, letter]);
        if (entry.OriginalPath is { } original && node.Group == ChangeGroup.Staged)
        {
            var sameFolder = Path.GetDirectoryName(original) == Path.GetDirectoryName(entry.Path);
            panel.Children.Add(new TextBlock
            {
                Text = $"from {(sameFolder ? Path.GetFileName(original) : original)}",
                Classes = { "caption", "muted" },
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            });
        }

        ToolTip.SetTip(name, entry.OriginalPath is { } from ? $"{entry.Path}\nRenamed from {from}" : entry.Path);
        return panel;
    }

    private ContextMenu ContextMenuFor(List<ChangeNode> files)
    {
        var items = new List<Control>();
        if (files.Count == 0)
            return new ContextMenu { ItemsSource = items };

        var entries = files.Select(f => f.Entry!).Distinct().ToList();
        var repository = repositories.Repository;
        if (files is [{ Entry: { } single } only])
            items.Add(Item("Show Diff", Icons.Find("git-compare"), () => _ = operations.ShowDiffAsync(single, only.Group == ChangeGroup.Staged), "Enter"));
        if (files.Any(f => f.Group != ChangeGroup.Staged))
            items.Add(Item("Stage", Icons.Plus, () => _ = ToggleAsync(files, stage: true), "Space"));
        if (files.Any(f => f.Group == ChangeGroup.Staged))
            items.Add(Item("Unstage", Icons.Minus, () => _ = ToggleAsync(files, stage: false)));
        items.Add(Item("Roll Back...", Icons.Undo, () => _ = operations.RollbackAsync(entries), "Delete"));
        items.Add(new Separator());
        items.Add(Item("Open", Icons.FileCode, () =>
        {
            foreach (var entry in entries.Where(e => repository is not null && File.Exists(repository.FullPath(e.Path))))
                _ = editors.OpenAsync(repository!.FullPath(entry.Path));
        }));
        if (launcher is not null && repository is not null && entries is [var revealed])
            items.Add(Item(OperatingSystem.IsMacOS() ? "Reveal in Finder" : "Reveal in File Manager", Icons.ExternalLink, () => launcher.Reveal(repository.FullPath(revealed.Path))));
        if (repository is not null && entries is [var historyEntry] && historyEntry.Kind != StatusKind.Untracked)
            items.Add(Item("Show History", Icons.Find(GitIcons.History), () => operations.ShowFileHistory(repository.FullPath(historyEntry.Path))));
        if (entries.All(e => e.Kind == StatusKind.Untracked))
            items.Add(Item("Add to .gitignore", Icons.EyeOff, () => _ = operations.AddToGitignoreAsync([.. entries.Select(e => e.Path)])));
        items.Add(new Separator());
        items.Add(Item(entries.Count == 1 ? "Copy Path" : "Copy Paths", Icons.Copy, () => _ = CopyAsync(string.Join(Environment.NewLine, entries.Select(e => repository?.FullPath(e.Path) ?? e.Path)))));
        items.Add(Item(entries.Count == 1 ? "Copy Relative Path" : "Copy Relative Paths", null, () => _ = CopyAsync(string.Join(Environment.NewLine, entries.Select(e => e.Path)))));
        return new ContextMenu { ItemsSource = items };
    }

    private async Task ToggleAsync(IReadOnlyList<ChangeNode> files, bool stage)
    {
        if (repositories.Repository is not { } repository)
            return;

        if (stage)
        {
            await StageAsync(repository, [.. files.Where(f => f.Group != ChangeGroup.Staged).Select(f => f.Entry!)]);
            return;
        }

        try
        {
            var paths = files.Where(f => f.Group == ChangeGroup.Staged).SelectMany(f => f.Entry!.OriginalPath is { } original ? [f.Entry.Path, original] : new[] { f.Entry.Path });
            await repository.UnstageAsync(paths.Distinct(StringComparer.Ordinal), CancellationToken.None);
        }
        catch (GitException exception)
        {
            errorBanner.Show(BannerTone.Error, "Could not unstage", exception.Message);
        }

        await repositories.RefreshAsync();
    }

    private async Task StageAsync(GitRepository repository, IReadOnlyList<StatusEntry> entries)
    {
        try
        {
            await repository.StageAsync(entries.Select(e => e.Path).Distinct(StringComparer.Ordinal), CancellationToken.None);
        }
        catch (GitException exception)
        {
            errorBanner.Show(BannerTone.Error, "Could not stage", exception.Message);
        }

        await repositories.RefreshAsync();
    }

    private List<ChangeNode> SelectedFiles() =>
        [.. (tree.SelectedItems?.OfType<ChangeNode>() ?? []).SelectMany(n => n.Files()).DistinctBy(n => n.Key)];

    private void FillRecentMessages(MenuFlyout menu)
    {
        menu.Items.Add(new MenuItem { Header = "Loading...", IsEnabled = false });
        _ = LoadAsync();

        async Task LoadAsync()
        {
            var messages = repositories.Repository is { } repository ? await repository.GetRecentMessagesAsync(RecentMessageCount, CancellationToken.None) : [];
            menu.Items.Clear();
            if (messages.Count == 0)
                menu.Items.Add(new MenuItem { Header = "No commits yet", IsEnabled = false });
            foreach (var text in messages)
            {
                var item = new MenuItem { Header = text.Split('\n')[0], MaxWidth = 460 };
                ToolTip.SetTip(item, text);
                item.Click += (_, _) =>
                {
                    message.Text = text;
                    FocusMessage();
                };
                menu.Items.Add(item);
            }
        }
    }

    private void Remember(ChangeNode node)
    {
        if (node.IsExpanded)
        {
            collapsed.Remove(node.Key);
            collapsed.Add("open:" + node.Key);
        }
        else
        {
            collapsed.Add(node.Key);
            collapsed.Remove("open:" + node.Key);
        }
    }

    private async Task CopyAsync(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    private static IEnumerable<ChangeNode> Flatten(ChangeNode node) => node.Children.SelectMany(Flatten).Prepend(node);

    private static string Files(int count) => count == 1 ? "1 file" : string.Create(CultureInfo.CurrentCulture, $"{count} files");

    private static string LetterBrush(char letter) => letter switch
    {
        'A' or 'U' => "SuccessBrush",
        'D' or '!' => "DangerBrush",
        'R' or 'C' => "AccentBrush",
        'M' or 'T' => "InfoBrush",
        _ => "TextSecondaryBrush",
    };

    private static string Describe(ChangeNode node) => node.Letter switch
    {
        'A' => "Added",
        'U' => "Untracked",
        'D' => "Deleted",
        'R' => "Renamed",
        'C' => "Copied",
        'T' => "Type changed",
        '!' => "Conflict",
        _ => "Modified",
    };

    private static Button IconButton(string icon, string tip)
    {
        var button = new Button { Classes = { "icon", "small" }, Content = new SymbolIcon { Data = Icons.Find(icon), Size = 14 } };
        ToolTip.SetTip(button, tip);
        return button;
    }

    private static MenuFlyout DynamicMenu(Action<MenuFlyout> fill)
    {
        var menu = new MenuFlyout { Placement = PlacementMode.TopEdgeAlignedRight };
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

    private static MenuItem Item(string header, Geometry? icon, Action run, string? gesture = null)
    {
        var item = new MenuItem { Header = header, Icon = icon is null ? null : new SymbolIcon { Data = icon, Size = 14 }, InputGesture = gesture is null ? null : KeyGesture.Parse(gesture) };
        item.Click += (_, _) => run();
        return item;
    }
}
