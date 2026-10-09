using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Git.Commands;
using Runesmith.Git.Git;
using Runesmith.Sdk.Languages;

namespace Runesmith.Git.Views.History;

/// <summary>A folder or file of a commit's changed files.</summary>
internal sealed class CommitFileNode
{
    public CommitFileNode(string name, FileChange? change)
    {
        Name = name;
        Change = change;
    }

    public string Name { get; private set; }

    public FileChange? Change { get; }

    public List<CommitFileNode> Children { get; } = [];

    /// <summary>Builds the tree of changed files, joining folders that hold only one folder.</summary>
    public static List<CommitFileNode> Build(IReadOnlyList<FileChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var root = new CommitFileNode("", null);
        foreach (var change in changes.OrderBy(c => c.Path, StringComparer.OrdinalIgnoreCase))
        {
            var parts = change.Path.Split('/');
            var parent = root;
            foreach (var part in parts[..^1])
            {
                var folder = parent.Children.FirstOrDefault(c => c.Change is null && c.Name == part);
                if (folder is null)
                {
                    folder = new CommitFileNode(part, null);
                    parent.Children.Add(folder);
                }

                parent = folder;
            }

            parent.Children.Add(new CommitFileNode(parts[^1], change));
        }

        Compress(root);
        return root.Children;
    }

    private static void Compress(CommitFileNode node)
    {
        foreach (var child in node.Children)
        {
            while (child.Change is null && child.Children is [{ Change: null } only])
            {
                child.Name += "/" + only.Name;
                child.Children.Clear();
                child.Children.AddRange(only.Children);
            }

            Compress(child);
        }

        node.Children.Sort((a, b) => (a.Change is null) != (b.Change is null) ? (a.Change is null ? -1 : 1) : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
    }
}

/// <summary>The Git window's details of the selected commit: its message, author and committer, hashes, parents and changed files.</summary>
internal sealed class CommitDetailsView : DockPanel
{
    private readonly GitOperations operations;
    private readonly ILanguageRegistry? languages;
    private readonly Action<string> selectCommit;
    private readonly SelectableTextBlock subject = new() { FontWeight = FontWeight.SemiBold, FontSize = 14, TextWrapping = TextWrapping.Wrap };
    private readonly SelectableTextBlock body = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly Grid facts = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBlock filesTitle = new() { Classes = { "caption", "muted" }, FontWeight = FontWeight.SemiBold, Margin = new Thickness(12, 10, 12, 4) };
    private readonly TreeView files;
    private readonly EmptyState empty = new() { Icon = Icons.Find(GitIcons.Commit), Title = "No commit selected", Hint = "Select a commit to see its message and the files it changed." };
    private readonly Grid content;
    private GitCommit? commit;
    private int version;

    public CommitDetailsView(GitOperations operations, ILanguageRegistry? languages, Action<string> selectCommit)
    {
        this.operations = operations;
        this.languages = languages;
        this.selectCommit = selectCommit;
        body[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextSecondaryBrush");
        files = new TreeView
        {
            ItemTemplate = new FuncTreeDataTemplate<CommitFileNode?>((node, _) => node is null ? new Panel() : FileRow(node), node => node?.Children ?? []),
        };
        files.ContainerPrepared += (_, e) =>
        {
            if (e.Container is TreeViewItem item)
                item.IsExpanded = true;
        };
        files.DoubleTapped += (_, _) => OpenSelected();
        files.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
                OpenSelected();
        };

        var header = new StackPanel { Margin = new Thickness(12, 10, 12, 0), Children = { subject, body, facts } };
        var scroll = new ScrollViewer { Content = header, MaxHeight = 320, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        content = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        Grid.SetRow(filesTitle, 1);
        Grid.SetRow(files, 2);
        content.Children.AddRange([scroll, filesTitle, files]);
        content.IsVisible = false;
        Children.Add(new Panel { Children = { content, empty } });
    }

    /// <summary>Shows a commit, or nothing.</summary>
    public async Task ShowAsync(GitCommit? selected)
    {
        commit = selected;
        var current = ++version;
        if (selected is null || operations.Repositories.Repository is not { } repository)
        {
            content.IsVisible = false;
            empty.IsVisible = true;
            return;
        }

        CommitDetails details;
        try
        {
            details = await repository.GetCommitAsync(selected.Sha, CancellationToken.None);
        }
        catch (GitException exception)
        {
            empty.Title = "Could not read the commit";
            empty.Hint = exception.Message;
            return;
        }

        if (current != version)
            return;

        var message = details.Message.ReplaceLineEndings("\n");
        var split = message.IndexOf("\n\n", StringComparison.Ordinal);
        subject.Text = split < 0 ? message.Trim() : message[..split].Trim();
        body.Text = split < 0 ? "" : message[(split + 2)..].Trim();
        body.IsVisible = body.Text.Length > 0;
        FillFacts(details.Commit);
        filesTitle.Text = details.Files.Count == 1 ? "1 changed file" : string.Create(CultureInfo.CurrentCulture, $"{details.Files.Count} changed files");
        files.ItemsSource = CommitFileNode.Build(details.Files);
        content.IsVisible = true;
        empty.IsVisible = false;
    }

    private void FillFacts(GitCommit shown)
    {
        facts.Children.Clear();
        facts.RowDefinitions.Clear();
        AddFact("Author", Person(shown.AuthorName, shown.AuthorEmail, shown.AuthorDate));
        if (shown.CommitterName != shown.AuthorName || shown.CommitterEmail != shown.AuthorEmail || Math.Abs((shown.CommitDate - shown.AuthorDate).TotalMinutes) > 1)
            AddFact("Committer", Person(shown.CommitterName, shown.CommitterEmail, shown.CommitDate));
        AddFact("Commit", Hash(shown.Sha, link: false));
        if (shown.Parents.Count > 0)
        {
            var parents = new WrapPanel();
            foreach (var parent in shown.Parents)
                parents.Children.Add(Hash(parent, link: true));
            AddFact(shown.Parents.Count == 1 ? "Parent" : "Parents", parents);
        }

        if (shown.Refs.Count > 0)
            AddFact("Refs", new TextBlock { Text = string.Join(", ", shown.Refs.Select(r => r.Name)), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
    }

    private void AddFact(string label, Control value)
    {
        var row = facts.RowDefinitions.Count;
        facts.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var name = new TextBlock { Text = label, Classes = { "caption", "muted" }, Margin = new Thickness(0, 6, 12, 4), VerticalAlignment = VerticalAlignment.Top };
        value.Margin = new Thickness(0, 3, 0, 3);
        Grid.SetRow(name, row);
        Grid.SetRow(value, row);
        Grid.SetColumn(value, 1);
        facts.Children.AddRange([name, value]);
    }

    private static StackPanel Person(string name, string email, DateTimeOffset date)
    {
        var detail = new SelectableTextBlock
        {
            Text = $"{email}, {date.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}",
            Classes = { "caption" },
            TextWrapping = TextWrapping.Wrap,
        };
        detail[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMutedBrush");
        return new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { new SelectableTextBlock { Text = name, TextWrapping = TextWrapping.Wrap }, detail } };
    }

    private StackPanel Hash(string sha, bool link)
    {
        var text = link
            ? (Control)new Button { Content = sha[..Math.Min(10, sha.Length)], Classes = { "subtle", "small" }, Padding = new Thickness(4, 0) }
            : new SelectableTextBlock { Text = sha[..Math.Min(12, sha.Length)], Classes = { "mono" }, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(text, sha);
        if (text is Button button)
        {
            ToolTip.SetTip(button, $"Select {sha[..Math.Min(10, sha.Length)]} in the log");
            button.Click += (_, _) => selectCommit(sha);
        }

        var copy = new Button { Classes = { "icon", "small" }, Content = new SymbolIcon { Data = Icons.Copy, Size = 12 }, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(copy, "Copy the full hash");
        copy.Click += (_, _) => _ = CopyAsync(sha);
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { text, copy } };
    }

    private StackPanel FileRow(CommitFileNode node)
    {
        if (node.Change is not { } change)
        {
            var folder = new SymbolIcon { Data = Icons.Folder, Size = 14, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            folder[!SymbolIcon.ForegroundProperty] = new DynamicResourceExtension("TextMutedBrush");
            return new StackPanel { Orientation = Orientation.Horizontal, Children = { folder, new TextBlock { Text = node.Name, VerticalAlignment = VerticalAlignment.Center } } };
        }

        var icon = new SymbolIcon { Data = Icons.Find(languages?.GetLanguageForFile(change.Path).Icon) ?? Icons.File, Size = 14, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        icon[!SymbolIcon.ForegroundProperty] = new DynamicResourceExtension("TextSecondaryBrush");
        var brush = change.Status switch
        {
            'A' => "SuccessBrush",
            'D' => "DangerBrush",
            'R' or 'C' => "AccentBrush",
            _ => "InfoBrush",
        };
        var name = new TextBlock { Text = node.Name, VerticalAlignment = VerticalAlignment.Center };
        name[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(brush);
        if (change.Status == 'D')
            name.TextDecorations = TextDecorations.Strikethrough;
        var letter = new TextBlock { Text = change.Status.ToString(), Classes = { "mono" }, FontWeight = FontWeight.SemiBold, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        letter[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(brush);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, name } };
        if (change.OldPath is { } old)
            row.Children.Add(new TextBlock { Text = $"from {old}", Classes = { "caption", "muted" }, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(letter);
        ToolTip.SetTip(row, $"{change.Path}\nDouble-click for the diff");
        return row;
    }

    private void OpenSelected()
    {
        if (commit is not null && files.SelectedItem is CommitFileNode { Change: { } change })
            _ = operations.ShowCommitDiffAsync(commit, change);
    }

    private async Task CopyAsync(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }
}
