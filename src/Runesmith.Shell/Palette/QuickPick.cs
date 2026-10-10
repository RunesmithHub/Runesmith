using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using HammerUI;
using HammerUI.Controls;

namespace Runesmith.Shell.Palette;

/// <summary>The search box at the top of the window that finds commands, files and lines, chosen by the query's prefix.</summary>
internal sealed class QuickPick : UserControl, IDisposable
{
    private readonly IReadOnlyList<IQuickPickSource> sources;
    private readonly Action<QuickPickItem?, IQuickPickSource?> close;
    private readonly TextBox query;
    private readonly ListBox results;
    private readonly StackPanel nothing;
    private CancellationTokenSource? search;
    private IQuickPickSource? current;

    public QuickPick(IReadOnlyList<IQuickPickSource> sources, string initialQuery, Action<QuickPickItem?, IQuickPickSource?> close)
    {
        this.sources = sources;
        this.close = close;

        query = PaletteChrome.QueryBox(initialQuery);
        query.TextChanged += (_, _) => Refresh();
        query.AddHandler(KeyDownEvent, OnQueryKeyDown, RoutingStrategies.Tunnel);

        var searchIcon = PaletteChrome.MutedIcon(Icons.Search, 18);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(16, 4, 12, 4) };
        Grid.SetColumn(query, 1);
        var escape = new ShortcutBadge { Text = "Esc", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(escape, 2);
        header.Children.AddRange([searchIcon, query, escape]);

        results = new ListBox
        {
            Classes = { "sidebar" },
            Margin = new Thickness(6),
            MaxHeight = 420,
            Background = Brushes.Transparent,
            ItemTemplate = new FuncDataTemplate<QuickPickItem?>((item, _) => item is null ? new Panel() : Row(item)),
        };
        results.DoubleTapped += (_, _) => Accept();

        var nothingIcon = PaletteChrome.MutedIcon(Icons.Search, 22);
        nothingIcon.HorizontalAlignment = HorizontalAlignment.Center;
        nothing = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsVisible = false,
            Children = { nothingIcon, new TextBlock { Text = "Nothing matches", Classes = { "muted" }, HorizontalAlignment = HorizontalAlignment.Center } },
        };

        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(16, 8) };
        footer.Children.AddRange([new ShortcutBadge { Text = "↑" }, new ShortcutBadge { Text = "↓" }, PaletteChrome.Caption("Navigate", 16), new ShortcutBadge { Text = "Enter" }, PaletteChrome.Caption("Open", 16)]);
        foreach (var source in sources.Where(s => s.Prefix.Length > 0))
            footer.Children.AddRange([new ShortcutBadge { Text = source.Prefix }, PaletteChrome.Caption(source.Name, 14)]);

        var layout = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        var topLine = PaletteChrome.Divider();
        DockPanel.SetDock(topLine, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        var bottomLine = PaletteChrome.Divider();
        DockPanel.SetDock(bottomLine, Dock.Bottom);
        layout.Children.AddRange([header, topLine, footer, bottomLine, new Panel { Children = { results, nothing } }]);

        Content = PaletteChrome.Card(layout);
    }

    public void Dispose()
    {
        search?.Cancel();
        search?.Dispose();
        search = null;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Dispose();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Refresh();
        Dispatcher.UIThread.Post(() =>
        {
            query.Focus();
            query.CaretIndex = query.Text?.Length ?? 0;
        }, DispatcherPriority.Background);
    }

    private void Refresh()
    {
        var text = query.Text ?? "";
        current = sources.Where(s => s.Prefix.Length > 0 && text.StartsWith(s.Prefix, StringComparison.Ordinal)).MaxBy(s => s.Prefix.Length)
            ?? sources.FirstOrDefault(s => s.Prefix.Length == 0)
            ?? sources[0];
        query.PlaceholderText = current.Placeholder;
        var term = text[Math.Min(text.Length, text.StartsWith(current.Prefix, StringComparison.Ordinal) ? current.Prefix.Length : 0)..].Trim();
        _ = SearchAsync(current, term);
    }

    private async Task SearchAsync(IQuickPickSource source, string term)
    {
        search?.Cancel();
        search?.Dispose();
        search = new CancellationTokenSource();
        var token = search.Token;
        try
        {
            var items = await source.SearchAsync(term, token);
            if (token.IsCancellationRequested)
                return;

            results.ItemsSource = items;
            results.SelectedIndex = items.Count > 0 ? 0 : -1;
            nothing.IsVisible = items.Count == 0;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                Move(1);
                break;
            case Key.Up:
                Move(-1);
                break;
            case Key.PageDown:
                Move(10);
                break;
            case Key.PageUp:
                Move(-10);
                break;
            case Key.Enter:
                Accept();
                break;
            case Key.Escape:
                close(null, null);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void Move(int rows)
    {
        if (results.ItemCount == 0)
            return;

        results.SelectedIndex = Math.Abs(rows) == 1
            ? ((results.SelectedIndex + rows) % results.ItemCount + results.ItemCount) % results.ItemCount
            : Math.Clamp(results.SelectedIndex + rows, 0, results.ItemCount - 1);
        if (results.SelectedItem is { } selected)
            results.ScrollIntoView(selected);
    }

    private void Accept()
    {
        if (results.SelectedItem is QuickPickItem { IsEnabled: true } item)
            close(item, current);
    }

    private static Grid Row(QuickPickItem item)
    {
        var icon = PaletteChrome.IconTile(item.Icon, item.IconBrush, item.IsIconFilled);
        var title = PaletteChrome.Highlighted(item.Title, item.Matches);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1, Children = { title } };
        if (item.HasSubtitle)
            text.Children.Add(new TextBlock { Text = item.Subtitle, Classes = { "caption", "muted" }, TextTrimming = TextTrimming.CharacterEllipsis });

        var detail = new TextBlock { Text = item.Detail, Classes = { "caption", "muted" }, VerticalAlignment = VerticalAlignment.Center };
        var gesture = new ShortcutBadge { Text = item.Gesture, IsVisible = item.HasGesture, VerticalAlignment = VerticalAlignment.Center };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,12,*,Auto,12,Auto"), Opacity = item.IsEnabled ? 1 : 0.45 };
        Grid.SetColumn(text, 2);
        Grid.SetColumn(detail, 3);
        Grid.SetColumn(gesture, 5);
        row.Children.AddRange([icon, text, detail, gesture]);
        return row;
    }
}
