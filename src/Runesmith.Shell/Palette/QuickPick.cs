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

        query = new TextBox { Text = initialQuery, FontSize = 15, Padding = new Thickness(10, 12), BorderThickness = new Thickness(0), Classes = { "bare" } };
        query.Resources["TextControlBorderBrushFocused"] = Brushes.Transparent;
        query.Resources["TextControlBorderBrushPointerOver"] = Brushes.Transparent;
        query.Resources["TextControlBackgroundFocused"] = Brushes.Transparent;
        query.Resources["TextControlBackgroundPointerOver"] = Brushes.Transparent;
        query.Resources["TextControlBackground"] = Brushes.Transparent;
        query.FocusAdorner = null;
        query.TextChanged += (_, _) => Refresh();
        query.AddHandler(KeyDownEvent, OnQueryKeyDown, RoutingStrategies.Tunnel);

        var searchIcon = new SymbolIcon { Data = Icons.Search, Size = 18 };
        searchIcon[!SymbolIcon.ForegroundProperty] = new DynamicResourceExtension("TextMutedBrush");
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

        var nothingIcon = new SymbolIcon { Data = Icons.Search, Size = 22, HorizontalAlignment = HorizontalAlignment.Center };
        nothingIcon[!SymbolIcon.ForegroundProperty] = new DynamicResourceExtension("TextMutedBrush");
        nothing = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsVisible = false,
            Children = { nothingIcon, new TextBlock { Text = "Nothing matches", Classes = { "muted" }, HorizontalAlignment = HorizontalAlignment.Center } },
        };

        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(16, 8) };
        footer.Children.AddRange([new ShortcutBadge { Text = "↑" }, new ShortcutBadge { Text = "↓" }, Caption("Navigate", 16), new ShortcutBadge { Text = "Enter" }, Caption("Open", 16)]);
        foreach (var source in sources.Where(s => s.Prefix.Length > 0))
            footer.Children.AddRange([new ShortcutBadge { Text = source.Prefix }, Caption(source.Name, 14)]);

        var layout = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        var topLine = Divider();
        DockPanel.SetDock(topLine, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        var bottomLine = Divider();
        DockPanel.SetDock(bottomLine, Dock.Bottom);
        layout.Children.AddRange([header, topLine, footer, bottomLine, new Panel { Children = { results, nothing } }]);

        var card = new Border
        {
            Width = 660,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Child = layout,
        };
        card[!Border.BackgroundProperty] = new DynamicResourceExtension("Surface2Brush");
        card[!Border.BorderBrushProperty] = new DynamicResourceExtension("OutlineBrush");
        card[!Border.BoxShadowProperty] = new DynamicResourceExtension("ShadowPopup");
        Content = card;
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

    private static TextBlock Caption(string text, double rightMargin) =>
        new() { Text = text, Classes = { "caption", "muted" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, rightMargin, 0) };

    private static Border Divider() => new() { Classes = { "divider-h" }, Margin = new Thickness(0) };

    private static Grid Row(QuickPickItem item)
    {
        var icon = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(7), Child = new SymbolIcon { Data = item.Icon ?? Icons.ChevronRight, Size = 14, IsFilled = item.IsIconFilled } };
        if (item.IconBrush is { } brush)
            ((SymbolIcon)icon.Child).Foreground = brush;
        icon[!Border.BackgroundProperty] = new DynamicResourceExtension("SurfaceSunkenBrush");

        var title = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        var matched = item.Matches.ToHashSet();
        if (matched.Count == 0)
        {
            title.Text = item.Title;
        }
        else
        {
            for (var i = 0; i < item.Title.Length;)
            {
                var isMatch = matched.Contains(i);
                var end = i;
                while (end < item.Title.Length && matched.Contains(end) == isMatch)
                    end++;
                var run = new Run(item.Title[i..end]);
                if (isMatch)
                {
                    run.FontWeight = FontWeight.SemiBold;
                    run[!TextElement.ForegroundProperty] = new DynamicResourceExtension("AccentBrush");
                }

                title.Inlines!.Add(run);
                i = end;
            }
        }

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
