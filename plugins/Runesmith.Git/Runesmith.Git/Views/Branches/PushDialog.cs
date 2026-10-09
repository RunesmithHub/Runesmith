using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using HammerUI;
using HammerUI.Controls;
using HammerUI.Services;
using Runesmith.Git.Commands;
using Runesmith.Git.Git;

namespace Runesmith.Git.Views.Branches;

/// <summary>Where the user chose to push, and whether to force it.</summary>
internal sealed record PushRequest(string Remote, string Branch, bool Force);

/// <summary>The Push dialog: the commits that go, where they go, and a forced push behind a check box.</summary>
internal static class PushDialog
{
    /// <summary>Shows the dialog; returns null when the user cancels.</summary>
    public static async Task<PushRequest?> ShowAsync(IDialogService dialogs, string branch, IReadOnlyList<string> remotes, string remote, string remoteBranch,
        IReadOnlyList<GitCommit> outgoing, bool hasUpstream, int behind)
    {
        var remoteBox = new ComboBox { ItemsSource = remotes, SelectedItem = remote, MinWidth = 120, IsVisible = remotes.Count > 1 };
        var remoteLabel = new TextBlock { Text = remote, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold, IsVisible = remotes.Count <= 1 };
        var branchBox = new TextBox { Text = remoteBranch, MinWidth = 220 };
        var target = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new SymbolIcon { Data = Icons.Find(GitIcons.Branch), Size = 14, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = branch, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
                new SymbolIcon { Data = Icons.ArrowRight, Size = 14, VerticalAlignment = VerticalAlignment.Center },
                remoteBox,
                remoteLabel,
                new TextBlock { Text = "/", VerticalAlignment = VerticalAlignment.Center },
                branchBox,
            },
        };

        var list = new ListBox
        {
            Classes = { "rows" },
            ItemsSource = outgoing,
            Height = 220,
            ItemTemplate = new FuncDataTemplate<GitCommit>((commit, _) => commit is null ? new Panel() : Row(commit)),
        };
        var empty = new EmptyState
        {
            Icon = Icons.CheckCircle,
            Title = hasUpstream ? "Nothing to push" : "A new branch",
            Hint = hasUpstream ? $"{remote}/{remoteBranch} has every commit of {branch}." : $"{branch} is new on {remote}; pushing it creates it there and tracks it.",
            Height = 220,
            IsVisible = outgoing.Count == 0,
        };
        list.IsVisible = outgoing.Count > 0;
        var force = new CheckBox { Content = "Force push, if the remote is where it was at the last fetch" };
        var behindHint = new TextBlock
        {
            Text = $"{remote}/{remoteBranch} has {GitOperations.Commits(behind).ToLowerInvariant()} that {branch} does not have. Update the project first, or the push is rejected.",
            Classes = { "caption" },
            TextWrapping = TextWrapping.Wrap,
            IsVisible = behind > 0,
        };
        behindHint[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("WarningBrush");
        var push = new Button { Content = "Push", Classes = { "accent" }, IsDefault = true, MinWidth = 84, IsEnabled = outgoing.Count > 0 || !hasUpstream };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 84 };
        var count = new TextBlock
        {
            Text = outgoing.Count == 0 ? "" : outgoing.Count >= 100 ? "100 or more commits" : GitOperations.Commits(outgoing.Count),
            Classes = { "caption", "muted" },
        };
        var dialog = new Dialog
        {
            Header = "Push Commits",
            Description = hasUpstream ? $"To {remote}/{remoteBranch}, which {branch} tracks" : $"{branch} has no upstream yet; this push sets it",
            Width = 620,
            ShowCloseButton = false,
            Content = new StackPanel { Spacing = 12, Children = { target, count, behindHint, new Border { Classes = { "card" }, Padding = new Thickness(4), Child = new Panel { Children = { list, empty } } }, force } },
            Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { cancel, push } },
        };

        void Validate() => push.IsEnabled = (branchBox.Text?.Trim().Length ?? 0) > 0 && (outgoing.Count > 0 || !hasUpstream || force.IsChecked == true ||
            (remoteBox.SelectedItem as string) != remote || branchBox.Text?.Trim() != remoteBranch);
        branchBox.TextChanged += (_, _) => Validate();
        remoteBox.SelectionChanged += (_, _) => Validate();
        force.IsCheckedChanged += (_, _) =>
        {
            push.Content = force.IsChecked == true ? "Force Push" : "Push";
            push.Classes.Set("danger", force.IsChecked == true);
            Validate();
        };
        push.Click += (_, _) => dialog.Close(new PushRequest(remoteBox.SelectedItem as string ?? remote, branchBox.Text!.Trim(), force.IsChecked == true));
        cancel.Click += (_, _) => dialog.Close(null);
        return await dialogs.ShowAsync(dialog) as PushRequest;
    }

    private static Grid Row(GitCommit commit)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Height = 28 };
        var sha = new TextBlock { Text = commit.ShortSha, Classes = { "mono", "muted" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        var subject = new TextBlock { Text = commit.Subject, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var author = new TextBlock { Text = commit.AuthorName, Classes = { "caption", "muted" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var date = new TextBlock { Text = GitOperations.Relative(commit.AuthorDate), Classes = { "caption", "muted" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        Grid.SetColumn(subject, 1);
        Grid.SetColumn(author, 2);
        Grid.SetColumn(date, 3);
        grid.Children.AddRange([sha, subject, author, date]);
        ToolTip.SetTip(subject, commit.Subject);
        return grid;
    }
}
