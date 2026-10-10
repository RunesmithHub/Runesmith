using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Shell.Appearance;
using Runesmith.Shell.Palette;

namespace Runesmith.Shell.QuickInput;

/// <summary>How a question in the quick input box ended.</summary>
internal enum QuickInputOutcome
{
    Accepted,
    Cancelled,
    Back,
}

/// <summary>Where a question stands in steps, and who asked it.</summary>
/// <param name="Title">The title above the box, or null.</param>
/// <param name="Step">The step's number, or null outside steps.</param>
/// <param name="Asker">The name of the plugin that asked, or null for Runesmith.</param>
internal sealed record QuickInputFrame(string? Title, int? Step = null, int? StepCount = null, bool CanGoBack = false, string? Asker = null);

/// <summary>The quick input box: a quick pick or an input box in the palette's card, with a title, the step and a Back button for questions
/// in steps.</summary>
internal sealed class QuickInputView : UserControl
{
    private readonly Lazy<FileIcons>? icons;
    private readonly TextBox query;
    private readonly ListBox results;
    private readonly StackPanel nothing;
    private readonly TextBlock nothingText;
    private readonly ProgressBar busy;
    private readonly Grid titleBar;
    private readonly TextBlock title;
    private readonly TextBlock stepText;
    private readonly Button back;
    private readonly CheckBox all;
    private readonly SymbolIcon searchIcon;
    private readonly StackPanel message;
    private readonly TextBlock prompt;
    private readonly TextBlock error;
    private readonly StackPanel hints;
    private readonly TextBlock asker;
    private readonly Button ok;
    private readonly Border card;
    private PickModel? pick;
    private InputModel? input;
    private Action<QuickInputOutcome>? done;
    private bool isSyncing;
    private bool isWaiting;
    private DialogLayer? layer;
    private Window? window;

    public QuickInputView(Lazy<FileIcons>? icons = null)
    {
        this.icons = icons;

        query = PaletteChrome.QueryBox();
        query.TextChanged += (_, _) => OnQueryChanged();
        query.AddHandler(KeyDownEvent, OnQueryKeyDown, RoutingStrategies.Tunnel);

        searchIcon = PaletteChrome.MutedIcon(Icons.Search, 18);
        all = new CheckBox { VerticalAlignment = VerticalAlignment.Center, IsVisible = false, Margin = new Thickness(2, 0, 0, 0), MinHeight = 0 };
        AutomationProperties.SetName(all, "Check all");
        ToolTip.SetTip(all, "Check all");
        all.IsCheckedChanged += (_, _) =>
        {
            if (!isSyncing)
                pick?.SetAllChecked(all.IsChecked == true);
        };

        var escape = new ShortcutBadge { Text = "Esc", VerticalAlignment = VerticalAlignment.Center };
        busy = new ProgressBar { IsIndeterminate = true, Height = 2, MinHeight = 2, IsVisible = false, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0) };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(16, 4, 12, 4) };
        var leading = new Panel { Children = { searchIcon, all }, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(query, 1);
        Grid.SetColumn(escape, 2);
        header.Children.AddRange([leading, query, escape]);

        back = new Button { Classes = { "icon", "small" }, Content = new SymbolIcon { Data = Icons.ArrowLeft, Size = 15 }, IsVisible = false, Margin = new Thickness(0, 0, 6, 0) };
        ToolTip.SetTip(back, "Back (Alt+Left)");
        AutomationProperties.SetName(back, "Back");
        back.Click += (_, _) => Finish(QuickInputOutcome.Back);
        title = new TextBlock { FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        stepText = new TextBlock { Classes = { "caption", "muted" }, VerticalAlignment = VerticalAlignment.Center };
        titleBar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(12, 10, 16, 0), IsVisible = false };
        Grid.SetColumn(title, 1);
        Grid.SetColumn(stepText, 2);
        titleBar.Children.AddRange([back, title, stepText]);

        results = new ListBox
        {
            Classes = { "sidebar" },
            Margin = new Thickness(6),
            MaxHeight = 420,
            Background = Brushes.Transparent,
            ItemTemplate = new FuncDataTemplate<PickRow?>((row, _) => row is null ? new Panel() : Row(row)),
        };
        AutomationProperties.SetName(results, "Results");
        results.ContainerPrepared += (_, e) => PrepareContainer(e.Container);
        results.SelectionChanged += (_, _) =>
        {
            if (!isSyncing && pick is not null && results.SelectedItem is PickRow row)
                pick.Activate(row);
        };
        results.DoubleTapped += (_, _) =>
        {
            if (pick is { CanPickMany: false })
                _ = AcceptAsync();
        };
        results.Tapped += (_, e) =>
        {
            if (pick is { CanPickMany: true } && e.Source is Visual source && source.FindAncestorOfType<CheckBox>(includeSelf: true) is null
                && source.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is PickRow row)
                pick.Toggle(row);
        };
        results.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);
        results.AddHandler(TextInputEvent, OnListTextInput, RoutingStrategies.Tunnel);

        nothingText = new TextBlock { Text = "Nothing matches", Classes = { "muted" }, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
        var nothingIcon = PaletteChrome.MutedIcon(Icons.Search, 22);
        nothingIcon.HorizontalAlignment = HorizontalAlignment.Center;
        nothing = new StackPanel { Margin = new Thickness(24), Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, IsVisible = false, Children = { nothingIcon, nothingText } };

        prompt = new TextBlock { Classes = { "muted" }, TextWrapping = TextWrapping.Wrap };
        error = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
        error[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("DangerBrush");
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive);
        message = new StackPanel { Margin = new Thickness(18, 12, 18, 14), Spacing = 6, IsVisible = false, Children = { prompt, error } };

        hints = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        asker = new TextBlock { Classes = { "caption", "muted" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(12, 0, 0, 0) };
        ok = new Button { Content = "OK", Classes = { "accent", "small" }, IsVisible = false, Margin = new Thickness(10, 0, 0, 0), MinWidth = 56 };
        ok.Click += (_, _) => _ = AcceptAsync();
        var footer = new DockPanel { Margin = new Thickness(16, 8, 10, 8) };
        DockPanel.SetDock(ok, Dock.Right);
        DockPanel.SetDock(asker, Dock.Right);
        footer.Children.AddRange([ok, asker, hints]);

        var layout = new DockPanel();
        var topLine = PaletteChrome.Divider();
        var bottomLine = PaletteChrome.Divider();
        DockPanel.SetDock(titleBar, Dock.Top);
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(topLine, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        DockPanel.SetDock(bottomLine, Dock.Bottom);
        var body = new Panel { Children = { results, nothing, message } };
        layout.Children.AddRange([titleBar, header, topLine, footer, bottomLine, new Panel { Children = { body, busy } }]);
        card = PaletteChrome.Card(layout);
        Content = card;
    }

    /// <summary>Gets the question's text box, for the tests.</summary>
    internal TextBox Query => query;

    /// <summary>Gets the list of rows, for the tests.</summary>
    internal ListBox Results => results;

    /// <summary>Gets whether the question closes when the focus leaves it.</summary>
    public bool ClosesOnFocusLost { get; private set; } = true;

    /// <summary>Shows a quick pick.</summary>
    public void ShowPick(PickModel model, QuickInputFrame frame, Action<QuickInputOutcome> onDone)
    {
        Detach();
        pick = model;
        done = onDone;
        ClosesOnFocusLost = !model.Options.KeepOpenOnFocusLost;
        model.Changed += OnPickChanged;
        model.ActiveChanged += OnActiveChanged;
        model.PickedChanged += OnPickedChanged;
        ShowFrame(frame);

        searchIcon.IsVisible = !model.CanPickMany;
        all.IsVisible = model.CanPickMany;
        ok.IsVisible = model.CanPickMany;
        query.PasswordChar = default;
        query.PlaceholderText = model.Options.Placeholder ?? (model.CanPickMany ? "Type to filter, check the rows to pick" : "Type to filter");
        AutomationProperties.SetName(query, frame.Title ?? query.PlaceholderText);
        message.IsVisible = false;
        results.IsVisible = true;
        nothingText.Text = model.Options.EmptyText ?? "Nothing matches";
        SetHints(model.CanPickMany
            ? [("↑↓", "Navigate"), ("Space", "Check"), ("Enter", "OK")]
            : [("↑↓", "Navigate"), ("Enter", "Pick")], frame.CanGoBack);
        SetQueryText(model.Options.Value ?? "");
        _ = model.SetQueryAsync(query.Text ?? "");
        FocusQuery();
    }

    /// <summary>Shows an input box.</summary>
    public void ShowInput(InputModel model, QuickInputFrame frame, Action<QuickInputOutcome> onDone)
    {
        Detach();
        input = model;
        done = onDone;
        ClosesOnFocusLost = !model.Options.KeepOpenOnFocusLost;
        model.Changed += OnInputChanged;
        ShowFrame(frame);

        searchIcon.IsVisible = false;
        all.IsVisible = false;
        ok.IsVisible = false;
        results.IsVisible = false;
        nothing.IsVisible = false;
        busy.IsVisible = false;
        query.PasswordChar = model.Options.IsPassword ? '●' : default;
        query.PlaceholderText = model.Options.Placeholder;
        AutomationProperties.SetName(query, frame.Title ?? model.Options.Prompt ?? model.Options.Placeholder ?? "Input");
        AutomationProperties.SetHelpText(query, model.Options.Prompt);
        prompt.Text = model.Options.Prompt ?? "Press Enter to confirm or Esc to cancel.";
        error.IsVisible = false;
        message.IsVisible = true;
        SetHints([("Enter", "Confirm")], frame.CanGoBack);
        SetQueryText(model.Text);
        if (model.Options.ValueSelection is { } selection)
        {
            query.SelectionStart = Math.Clamp(selection.Start, 0, model.Text.Length);
            query.SelectionEnd = Math.Clamp(selection.End, 0, model.Text.Length);
        }
        else
        {
            query.SelectAll();
        }

        if (model.Text.Length > 0)
            _ = model.SetTextAsync(model.Text, immediate: true);
        FocusQuery();
    }

    /// <summary>Keeps the box on screen between steps, greyed out, while the plugin works on the answer.</summary>
    public void ShowWaiting()
    {
        isWaiting = true;
        Detach();
        busy.IsVisible = true;
        card.Opacity = 0.85;
        query.IsReadOnly = true;
    }

    /// <summary>Closes the question as if cancelled, such as when the focus leaves it.</summary>
    public void Cancel() => Finish(QuickInputOutcome.Cancelled);

    /// <summary>Accepts the question as the user would with Enter, for the tests.</summary>
    internal Task AcceptAsync() => AcceptCoreAsync();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        layer = this.FindAncestorOfType<DialogLayer>();
        layer?.AddHandler(PointerPressedEvent, OnLayerPointerPressed, RoutingStrategies.Tunnel);
        window = TopLevel.GetTopLevel(this) as Window;
        if (window is not null)
            window.Deactivated += OnWindowDeactivated;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        layer?.RemoveHandler(PointerPressedEvent, OnLayerPointerPressed);
        layer = null;
        if (window is not null)
            window.Deactivated -= OnWindowDeactivated;
        window = null;
    }

    private void ShowFrame(QuickInputFrame frame)
    {
        isWaiting = false;
        card.Opacity = 1;
        query.IsReadOnly = false;
        title.Text = frame.Title;
        stepText.Text = frame.Step is { } step && frame.StepCount is { } count ? $"Step {step} of {count}" : null;
        back.IsVisible = frame.CanGoBack;
        titleBar.IsVisible = frame.Title is not null || frame.Step is not null;
        asker.Text = frame.Asker is null ? null : $"Asked by {frame.Asker}";
        asker.IsVisible = frame.Asker is not null;
    }

    private void SetHints(IReadOnlyList<(string Key, string Text)> keys, bool canGoBack)
    {
        hints.Children.Clear();
        foreach (var (key, text) in keys)
        {
            foreach (var part in key == "↑↓" ? ["↑", "↓"] : new[] { key })
                hints.Children.Add(new ShortcutBadge { Text = part });
            hints.Children.Add(PaletteChrome.Caption(text, 16));
        }

        if (canGoBack)
            hints.Children.AddRange([new ShortcutBadge { Text = "Alt+←" }, PaletteChrome.Caption("Back", 16)]);
    }

    private void SetQueryText(string text)
    {
        isSyncing = true;
        query.Text = text;
        query.CaretIndex = text.Length;
        isSyncing = false;
    }

    private void FocusQuery() => Dispatcher.UIThread.Post(() => query.Focus(), DispatcherPriority.Background);

    private void Detach()
    {
        if (pick is not null)
        {
            pick.Changed -= OnPickChanged;
            pick.ActiveChanged -= OnActiveChanged;
            pick.PickedChanged -= OnPickedChanged;
            pick.Dispose();
        }

        if (input is not null)
        {
            input.Changed -= OnInputChanged;
            input.Dispose();
        }

        pick = null;
        input = null;
        done = null;
    }

    private void Finish(QuickInputOutcome outcome)
    {
        if (done is not { } callback || isWaiting)
            return;

        if (outcome == QuickInputOutcome.Back && !back.IsVisible)
            return;

        done = null;
        callback(outcome);
    }

    private async Task AcceptCoreAsync()
    {
        if (input is { } model)
        {
            await model.SetTextAsync(query.Text ?? "", immediate: true);
            if (ReferenceEquals(model, input) && await model.TryAcceptAsync() && ReferenceEquals(model, input))
                Finish(QuickInputOutcome.Accepted);
            return;
        }

        if (pick?.Accept() is { Count: > 0 })
            Finish(QuickInputOutcome.Accepted);
    }

    private void OnQueryChanged()
    {
        if (isSyncing)
            return;

        if (pick is not null)
            _ = pick.SetQueryAsync(query.Text ?? "");
        else if (input is not null)
        {
            error.IsVisible = false;
            _ = input.SetTextAsync(query.Text ?? "");
        }
    }

    private void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        if (HandleCommonKey(e))
            return;

        switch (e.Key)
        {
            case Key.Down when pick is not null:
                pick.Move(1);
                break;
            case Key.Up when pick is not null:
                pick.Move(-1);
                break;
            case Key.PageDown when pick is not null:
                pick.Move(10);
                break;
            case Key.PageUp when pick is not null:
                pick.Move(-10);
                break;
            case Key.Space when pick is { CanPickMany: true } && (e.KeyModifiers == KeyModifiers.Control || string.IsNullOrEmpty(query.Text)):
                pick.Toggle();
                break;
            case Key.Tab when pick is not null && e.KeyModifiers == KeyModifiers.None && results.ItemCount > 0:
                FocusActiveRow();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (HandleCommonKey(e))
            return;

        switch (e.Key)
        {
            case Key.Space when pick is { CanPickMany: true }:
                pick.Toggle();
                break;
            case Key.Tab when e.KeyModifiers == KeyModifiers.Shift:
            case Key.Back:
                query.Focus();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnListTextInput(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text) || e.Text == " ")
            return;

        query.Focus();
        query.Text = (query.Text ?? "") + e.Text;
        query.CaretIndex = query.Text.Length;
        e.Handled = true;
    }

    private bool HandleCommonKey(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                _ = AcceptAsync();
                break;
            case Key.Escape:
                Finish(QuickInputOutcome.Cancelled);
                break;
            case Key.Left when e.KeyModifiers == KeyModifiers.Alt && back.IsVisible:
                Finish(QuickInputOutcome.Back);
                break;
            default:
                return false;
        }

        e.Handled = true;
        return true;
    }

    private void FocusActiveRow()
    {
        if (results.SelectedIndex < 0)
            return;

        results.ScrollIntoView(results.SelectedIndex);
        (results.ContainerFromIndex(results.SelectedIndex) as InputElement)?.Focus(NavigationMethod.Tab);
    }

    private void OnPickChanged(object? sender, EventArgs e)
    {
        if (pick is not { } model)
            return;

        isSyncing = true;
        results.ItemsSource = model.Rows;
        isSyncing = false;
        busy.IsVisible = model.IsBusy;
        var isEmpty = !model.Rows.Any(r => !r.IsSeparator);
        nothing.IsVisible = isEmpty && !model.IsBusy;
        nothingText.Text = model.Error is { } problem ? $"The list could not be loaded: {problem}" : model.Options.EmptyText ?? "Nothing matches";
        SyncAll();
    }

    private void OnActiveChanged(object? sender, EventArgs e)
    {
        if (pick is not { } model)
            return;

        isSyncing = true;
        results.SelectedIndex = model.ActiveIndex;
        isSyncing = false;
        if (model.ActiveIndex >= 0)
            results.ScrollIntoView(model.ActiveIndex);
    }

    private void OnPickedChanged(object? sender, EventArgs e)
    {
        SyncAll();
        foreach (var container in results.GetRealizedContainers())
            PrepareContainer(container);
    }

    private void SyncAll()
    {
        if (pick is not { CanPickMany: true } model)
            return;

        var rows = model.Rows.Where(r => !r.IsSeparator).ToList();
        isSyncing = true;
        all.IsChecked = rows.Count > 0 && rows.All(r => r.IsChecked) ? true : rows.Any(r => r.IsChecked) ? null : false;
        isSyncing = false;
        ok.Content = model.Picked.Count > 0 ? $"OK ({model.Picked.Count})" : "OK";
    }

    private void OnInputChanged(object? sender, EventArgs e)
    {
        if (input is not { } model)
            return;

        busy.IsVisible = model.IsValidating;
        error.Text = model.Error;
        error.IsVisible = model.Error is not null;
    }

    private void OnLayerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ClosesOnFocusLost && e.Source is Visual source && !card.IsVisualAncestorOf(source) && !ReferenceEquals(source, card))
            Cancel();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        if (ClosesOnFocusLost)
            Cancel();
    }

    private void PrepareContainer(Control container)
    {
        if (container is not ListBoxItem item || item.DataContext is not PickRow row)
            return;

        item.IsEnabled = !row.IsSeparator;
        item.Focusable = !row.IsSeparator;
        item.MinHeight = row.IsSeparator ? 0 : 28;
        item.Padding = row.IsSeparator ? new Thickness(10, 8, 10, 2) : new Thickness(10, 6);
        var parts = new List<string> { row.Label };
        if (!string.IsNullOrEmpty(row.Description))
            parts.Add(row.Description);
        if (!string.IsNullOrEmpty(row.Detail))
            parts.Add(row.Detail);
        if (row.IsSeparator)
            parts[0] = $"Group {row.Label}";
        else if (pick is { CanPickMany: true })
            parts.Add(row.IsChecked ? "checked" : "not checked");
        AutomationProperties.SetName(item, string.Join(", ", parts));
    }

    private Control Row(PickRow row)
    {
        if (row.IsSeparator)
        {
            var label = new TextBlock { Text = row.Label, Classes = { "caption", "muted" }, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var line = PaletteChrome.Divider();
            line.VerticalAlignment = VerticalAlignment.Center;
            var group = new DockPanel { IsHitTestVisible = false };
            DockPanel.SetDock(label, Dock.Left);
            group.Children.AddRange(row.Label.Length > 0 ? [label, line] : [line]);
            return group;
        }

        var columns = pick is { CanPickMany: true } ? "Auto,8,Auto,12,*" : "0,0,Auto,12,*";
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(columns) };
        if (pick is { CanPickMany: true })
        {
            var box = new CheckBox { IsChecked = row.IsChecked, VerticalAlignment = VerticalAlignment.Center, MinHeight = 0, Focusable = false };
            AutomationProperties.SetName(box, row.Label);
            box.IsCheckedChanged += (_, _) =>
            {
                if (box.IsChecked != row.IsChecked)
                    pick?.Toggle(row);
            };
            PropertyChangedEventHandler follow = (_, _) => box.IsChecked = row.IsChecked;
            row.PropertyChanged += follow;
            box.DetachedFromVisualTree += (_, _) => row.PropertyChanged -= follow;
            box.AttachedToVisualTree += (_, _) =>
            {
                row.PropertyChanged -= follow;
                row.PropertyChanged += follow;
                box.IsChecked = row.IsChecked;
            };
            grid.Children.Add(box);
        }

        var tile = PaletteChrome.IconTile(Icons.Find(row.Icon) ?? Icons.ChevronRight);
        if (row.Icon is null && row.IconPath is { } path && icons is not null)
        {
            var themed = icons.Value.For(path, Directory.Exists(path));
            themed.ApplyTo((SymbolIcon)tile.Child!, "TextSecondaryBrush");
        }

        Grid.SetColumn(tile, 2);
        var titleLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var label2 = PaletteChrome.Highlighted(row.Label, row.Matches);
        titleLine.Children.Add(label2);
        if (!string.IsNullOrEmpty(row.Description))
            titleLine.Children.Add(new TextBlock { Text = row.Description, Classes = { "caption", "muted" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1, Children = { titleLine } };
        if (!string.IsNullOrEmpty(row.Detail))
            text.Children.Add(new TextBlock { Text = row.Detail, Classes = { "caption", "muted" }, TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(text, 4);
        grid.Children.AddRange([tile, text]);
        return grid;
    }
}
