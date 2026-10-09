using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Editor;

namespace Runesmith.Shell.Running;

/// <summary>One run's tab: a toolbar with Rerun, Stop, Clear and the run's state, the console, and a line for the program's input.</summary>
internal sealed class RunConsoleView : DockPanel
{
    private readonly RunSession session;
    private const double ListPadding = 4;

    private readonly ConsoleLineList lines = [];
    private readonly ListBox list;
    private readonly Button stop;
    private readonly TextBox input;
    private readonly TextBlock status = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), FontSize = 12 };
    private readonly SymbolIcon statusIcon = new() { Size = 14, VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private int dirty = 1;
    private bool drainedAll;

    public RunConsoleView(RunSession session, ConsoleLinkResolver resolver, Action<string, int, int> open, Action<RunSession> rerun)
    {
        this.session = session;
        list = new ListBox
        {
            Classes = { "rows", "console" },
            ItemsSource = lines,
            FontFamily = new FontFamily(EditorOptions.BundledFontFamily),
            FontSize = 12.5,
            SelectionMode = SelectionMode.Single,
            Padding = new Thickness(0, ListPadding),
            ItemTemplate = new FuncDataTemplate<ConsoleLine>((line, _) => line is null ? new TextBlock() : new ConsoleLineView(line, resolver, open)),
        };
        list.ContainerPrepared += (_, e) =>
        {
            e.Container.MinHeight = 0;
            if (e.Container is ListBoxItem item)
                item.Padding = new Thickness(10, 0);
        };
        list.KeyDown += OnListKeyDown;
        list.SizeChanged += (_, _) => AlignLines();
        list.ContextMenu = new ContextMenu
        {
            Items =
            {
                MenuItem("Copy Line", Icons.Copy, () => _ = CopyAsync(list.SelectedItem is ConsoleLine line ? line.Text : null)),
                MenuItem("Copy All", null, () => _ = CopyAsync(session.Console.GetText())),
                new Separator(),
                MenuItem("Clear", Icons.Trash, session.Console.Clear),
            },
        };

        var rerunButton = ToolButton(Icons.RotateCw, "Rerun", () => rerun(session));
        stop = ToolButton(Icons.Stop, "Stop", session.Stop);
        stop.Classes.Add("danger-icon");
        var clear = ToolButton(Icons.Trash, "Clear", session.Console.Clear);
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Margin = new Thickness(6, 4, 8, 4),
            Children = { rerunButton, stop, clear, new Border { Width = 1, Height = 16, Margin = new Thickness(6, 0), Classes = { "divider" } }, statusIcon, status },
        };
        DockPanel.SetDock(bar, Dock.Top);

        input = new TextBox { PlaceholderText = "Type input for the program and press Enter", Classes = { "small" }, Margin = new Thickness(8, 4, 8, 6) };
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                var text = input.Text ?? "";
                input.Text = "";
                _ = session.SendInputAsync(text);
            }
        };
        DockPanel.SetDock(input, Dock.Bottom);
        Children.AddRange([bar, input, list]);

        session.Console.Changed += (_, _) => Interlocked.Exchange(ref dirty, 1);
        session.StateChanged += (_, _) => Interlocked.Exchange(ref dirty, 1);
        timer.Tick += (_, _) => Flush();
        AttachedToVisualTree += (_, _) =>
        {
            Flush();
            timer.Start();
        };
        DetachedFromVisualTree += (_, _) => timer.Stop();
        Flush();
    }

    public RunSession Session => session;

    /// <summary>Moves the keyboard focus to the input line while the program runs.</summary>
    public void FocusInput()
    {
        if (input.IsEnabled)
            input.Focus();
    }

    private void Flush()
    {
        UpdateStatus();
        if (Interlocked.Exchange(ref dirty, 0) == 0)
            return;

        var atEnd = IsScrolledToEnd();
        var changed = lines.Apply(session.Console.Drain(all: !drainedAll));
        drainedAll = true;
        AlignLines();
        if (changed && atEnd && lines.Count > 0)
        {
            list.ScrollIntoView(lines.Count - 1);
            list.FindDescendantOfType<ScrollViewer>()?.ScrollToEnd();
        }
    }

    // Pads the bottom so that, scrolled to the end, the top line shows whole instead of cut off by the toolbar.
    private void AlignLines()
    {
        if (list.ContainerFromIndex(0) is not { Bounds.Height: > 0 and var height })
            return;

        var rest = (list.Bounds.Height - ListPadding) % height;
        var bottom = ListPadding + (rest < 0 ? 0 : rest);
        if (Math.Abs(list.Padding.Bottom - bottom) > 0.5)
            list.Padding = new Thickness(0, ListPadding, 0, bottom);
    }

    private bool IsScrolledToEnd() =>
        list.Scroll is not { } scroll || scroll.Offset.Y + scroll.Viewport.Height >= scroll.Extent.Height - 24;

    private void UpdateStatus()
    {
        var running = session.State != RunState.Finished;
        stop.IsEnabled = running;
        input.IsEnabled = session.State == RunState.Running;
        var time = session.Elapsed.TotalSeconds.ToString(session.Elapsed.TotalSeconds < 60 ? "0.0" : "0", CultureInfo.CurrentCulture) + " s";
        string brush;
        if (session.State == RunState.Preparing)
        {
            (statusIcon.Data, brush, status.Text) = (Icons.RotateCw, "TextMutedBrush", $"Preparing  {time}");
            statusIcon.Classes.Set("spin", true);
        }
        else if (session.State == RunState.Running)
        {
            (statusIcon.Data, brush, status.Text) = (Icons.Play, "SuccessBrush", $"Running  {time}");
            statusIcon.Classes.Set("spin", false);
        }
        else
        {
            statusIcon.Classes.Set("spin", false);
            (statusIcon.Data, brush, status.Text) = session switch
            {
                { WasStopped: true } => (Icons.Stop, "TextMutedBrush", $"Stopped after {time}"),
                { ExitCode: null } => (Icons.AlertCircle, "DangerBrush", "Did not start"),
                { ExitCode: 0 } => (Icons.CheckCircle, "SuccessBrush", $"Exit code 0  {time}"),
                _ => (Icons.AlertCircle, "DangerBrush", string.Create(CultureInfo.CurrentCulture, $"Exit code {session.ExitCode}  {time}")),
            };
            timer.Interval = TimeSpan.FromMilliseconds(250);
        }

        if (this.TryFindResource(brush, ActualThemeVariant, out var value) && value is IBrush color)
            statusIcon.Foreground = color;
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control) && list.SelectedItem is ConsoleLine line)
        {
            e.Handled = true;
            _ = CopyAsync(line.Text);
        }
    }

    private async Task CopyAsync(string? text)
    {
        if (text is not null && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    private static MenuItem MenuItem(string header, Geometry? icon, Action action)
    {
        var item = new MenuItem { Header = header, Icon = icon is null ? null : new SymbolIcon { Data = icon, Size = 14 } };
        item.Click += (_, _) => action();
        return item;
    }

    private static Button ToolButton(Geometry icon, string tip, Action action)
    {
        var button = new Button { Classes = { "icon", "small" }, Content = new SymbolIcon { Data = icon, Size = 14 } };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => action();
        return button;
    }
}
