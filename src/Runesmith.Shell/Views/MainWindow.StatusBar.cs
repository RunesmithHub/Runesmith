using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Editor;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Shell;
using Runesmith.Shell.Palette;
using Runesmith.Shell.Services;
using Runesmith.Text;

namespace Runesmith.Shell.Views;

/// <summary>The status bar: the active file's path, the background task indicator, the editor's state and the notification bell.</summary>
public partial class MainWindow
{
    private readonly DispatcherTimer taskRefresh = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly PathBreadcrumbs? statusPath;
    private TextEditor? watched;
    private bool watchedModified;

    private void WireStatusBar()
    {
        CaretButton.Click += (_, _) => _ = commands!.ExecuteAsync(CommandIds.GoToLine);
        ToolTip.SetTip(CaretButton, Tip("Go to line", CommandIds.GoToLine));
        LineEndingButton.Click += (_, _) => _ = ChangeLineEndingAsync();
        IndentButton.Click += (_, _) => pages!.ShowSettings("Editor");
        LanguageButton.Click += (_, _) => _ = ChangeLanguageAsync();
        EncodingButton.IsHitTestVisible = false;

        // Tasks report often and from any thread; the indicator catches up a few times a second.
        taskRefresh.Tick += (_, _) =>
        {
            taskRefresh.Stop();
            UpdateTasks();
        };
        tasks!.Changed += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (!taskRefresh.IsEnabled)
                taskRefresh.Start();
        });
        var taskFlyout = new Flyout { Placement = PlacementMode.TopEdgeAlignedLeft, ShowMode = FlyoutShowMode.Standard };
        taskFlyout.Opening += (_, _) => taskFlyout.Content = TaskList();
        TaskIndicator.Flyout = taskFlyout;
        ToolTip.SetTip(TaskIndicator, "Show the tasks that run");

        var history = new Flyout { Placement = PlacementMode.TopEdgeAlignedRight, ShowMode = FlyoutShowMode.Standard };
        history.Opening += (_, _) =>
        {
            history.Content = NotificationList(history);
            notifications!.MarkRead();
        };
        BellButton.Flyout = history;
        notifications!.HistoryChanged += (_, _) => UnreadDot.IsVisible = notifications.HasUnread;
        UpdateTasks();
    }

    private void OnStatusItemClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is StatusBarItem item)
            item.Click();
    }

    private void FollowActiveEditor()
    {
        if (watched is not null)
        {
            watched.CaretMoved -= OnCaretMoved;
            watched.Document.PropertyChanged -= OnDocumentChanged;
        }

        watched = editors!.Active;
        if (watched is not null)
        {
            watched.CaretMoved += OnCaretMoved;
            watched.Document.PropertyChanged += OnDocumentChanged;
            watchedModified = watched.Document.IsModified;
        }

        UpdateTitle();
        UpdateEditorInfo();
        UpdatePath();
    }

    private void OnCaretMoved(object? sender, EventArgs e) => UpdateCaret();

    // The document reports IsModified after every edit; only a change of the unsaved state, the name or the language reaches the window.
    private void OnDocumentChanged(object? sender, PropertyChangedEventArgs e) => UiThread.Run(() =>
    {
        if (watched is null)
            return;

        if (e.PropertyName == nameof(Sdk.Documents.IDocument.IsModified))
        {
            if (watched.Document.IsModified == watchedModified)
                return;
            watchedModified = watched.Document.IsModified;
            UpdateTitle();
            return;
        }

        UpdateTitle();
        UpdateEditorInfo();
        UpdatePath();
    });

    private void UpdateTitle()
    {
        var document = watched?.Document;
        var name = document is null ? null : document.IsModified ? document.Name + " *" : document.Name;
        string[] parts = [.. new[] { name, workspace!.Name, "Runesmith" }.Where(p => !string.IsNullOrEmpty(p))!];
        Title = string.Join(" - ", parts);
    }

    private void UpdatePath()
    {
        var showsUnderTabs = settings!.Get<bool>(ShellSettings.Breadcrumbs);
        var document = watched?.Document;
        statusPath?.Show(showsUnderTabs ? null : document?.FilePath, workspace!.RootPath);
    }

    private void UpdateEditorInfo()
    {
        EditorInfo.IsVisible = watched is not null;
        if (watched is null)
            return;

        var document = watched.Document;
        var options = watched.Options;
        IndentText.Text = options.InsertSpaces ? $"{options.TabSize} spaces" : $"Tab size {options.TabSize}";
        var encoding = document.Encoding;
        EncodingText.Text = encoding.WebName.ToUpperInvariant() + (encoding.GetPreamble().Length > 0 && encoding is System.Text.UTF8Encoding ? " with BOM" : "");
        LineEndingText.Text = document.LineEnding switch
        {
            LineEnding.CrLf => "CRLF",
            LineEnding.Cr => "CR",
            _ => "LF",
        };
        LanguageText.Text = languages!.Find(document.LanguageId)?.Name ?? document.LanguageId;
        UpdateCaret();
    }

    private void UpdateCaret()
    {
        if (watched is null)
            return;

        var snapshot = watched.Area.Snapshot;
        var position = snapshot.GetPosition(watched.CaretOffset);
        var selected = watched.Selection.Length;
        CaretText.Text = $"{position.Line + 1}:{position.Column + 1}" + (selected > 0 ? $" ({selected} selected)" : "");
    }

    private void UpdateTasks()
    {
        var latest = tasks!.Latest;
        TaskIndicator.IsVisible = latest is not null;
        if (latest is null)
            return;

        var count = tasks.Running.Count;
        TaskTitle.Text = count > 1 ? $"{latest.Title} (+{count - 1})" : latest.Title;
        TaskDetail.Text = latest.Detail;
        TaskDetail.IsVisible = !string.IsNullOrEmpty(latest.Detail);
        TaskProgress.IsIndeterminate = latest.Fraction is null;
        TaskProgress.Value = (latest.Fraction ?? 0) * 100;
    }

    private StackPanel TaskList()
    {
        var list = new StackPanel { Spacing = 8, Width = 320 };
        list.Children.Add(new TextBlock { Text = "Running tasks", FontWeight = FontWeight.SemiBold });
        var running = tasks!.Running.OfType<BackgroundTaskService.BackgroundTask>().Reverse().ToList();
        if (running.Count == 0)
            list.Children.Add(new TextBlock { Text = "Nothing runs now.", Classes = { "muted" } });

        foreach (var task in running)
        {
            var text = new StackPanel { Spacing = 2, Children = { new TextBlock { Text = task.Title, TextTrimming = TextTrimming.CharacterEllipsis } } };
            if (!string.IsNullOrEmpty(task.Detail))
                text.Children.Add(new TextBlock { Text = task.Detail, Classes = { "caption" }, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new ProgressBar { Classes = { "task" }, Width = double.NaN, Margin = new Thickness(0, 4, 0, 0), IsIndeterminate = task.Fraction is null, Value = (task.Fraction ?? 0) * 100 });

            var row = new DockPanel();
            if (task.CanCancel)
            {
                var cancel = new Button { Classes = { "icon", "small" }, Content = new SymbolIcon { Data = Icons.X, Size = 16 }, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 0, 0, 0) };
                ToolTip.SetTip(cancel, "Cancel");
                cancel.Click += (_, _) =>
                {
                    task.Cancel();
                    cancel.IsEnabled = false;
                };
                DockPanel.SetDock(cancel, Dock.Right);
                row.Children.Add(cancel);
            }

            row.Children.Add(text);
            list.Children.Add(row);
        }

        return list;
    }

    private StackPanel NotificationList(Flyout flyout)
    {
        var clear = new Button { Classes = { "subtle", "small" }, Content = "Clear all", IsEnabled = notifications!.History.Count > 0 };
        clear.Click += (_, _) =>
        {
            notifications.ClearHistory();
            flyout.Hide();
        };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(clear, Dock.Right);
        header.Children.AddRange([clear, new TextBlock { Text = "Notifications", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }]);

        var rows = new StackPanel { Spacing = 4 };
        if (notifications.History.Count == 0)
            rows.Children.Add(new TextBlock { Text = "No notifications yet.", Classes = { "muted" }, Margin = new Thickness(0, 4) });

        foreach (var entry in notifications.History)
        {
            var (icon, brush) = entry.Kind switch
            {
                NotificationKind.Error => (Icons.AlertCircle, "DangerBrush"),
                NotificationKind.Warning => (Icons.AlertTriangle, "WarningBrush"),
                NotificationKind.Success => (Icons.CheckCircle, "SuccessBrush"),
                _ => (Icons.Info, "InfoBrush"),
            };
            var symbol = new SymbolIcon { Data = icon, Size = 16, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 10, 0) };
            symbol[!SymbolIcon.ForegroundProperty] = new DynamicResourceExtension(brush);
            var text = new StackPanel { Spacing = 2 };
            var title = new DockPanel();
            var time = new TextBlock { Text = entry.Time.ToString("t", CultureInfo.CurrentCulture), Classes = { "caption" }, Margin = new Thickness(8, 0, 0, 0) };
            DockPanel.SetDock(time, Dock.Right);
            title.Children.AddRange([time, new TextBlock { Text = entry.Title, FontWeight = FontWeight.Medium, TextWrapping = TextWrapping.Wrap }]);
            text.Children.Add(title);
            if (!string.IsNullOrEmpty(entry.Message))
                text.Children.Add(new TextBlock { Text = entry.Message, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
            var row = new DockPanel { Margin = new Thickness(0, 4) };
            DockPanel.SetDock(symbol, Dock.Left);
            row.Children.AddRange([symbol, text]);
            rows.Children.Add(row);
        }

        return new StackPanel
        {
            Width = 360,
            Children = { header, new ScrollViewer { MaxHeight = 420, Content = rows } },
        };
    }

    private async Task ChangeLineEndingAsync()
    {
        if (watched?.Document is not { } document)
            return;

        if (await notifications!.ChooseAsync("Line endings", $"Which line endings should {document.Name} be saved with?", ["LF (Linux, macOS)", "CRLF (Windows)"]) is { } choice)
            document.LineEnding = choice == 0 ? LineEnding.Lf : LineEnding.CrLf;
    }

    private async Task ChangeLanguageAsync()
    {
        if (watched?.Document is not { } document)
            return;

        var items = languages!.Languages
            .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .Select(l => new QuickPickItem(l.Name, l.Id) { Detail = l.Id == document.LanguageId ? "Current" : null, Icon = Icons.Find(l.Icon) ?? Icons.File })
            .ToList();
        if (await controller!.PickAsync($"Choose the language of {document.Name}", items) is { Value: string id })
            document.LanguageId = id;
    }
}
