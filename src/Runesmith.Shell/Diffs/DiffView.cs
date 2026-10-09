using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Editor;
using Runesmith.Sdk.Documents;
using Runesmith.Text;

namespace Runesmith.Shell.Diffs;

/// <summary>Two versions of a text side by side: the older one on the left, read-only, and the newer one on the right, scrolling together, with
/// the changed lines tinted, the changed words highlighted, blank rows keeping the sides aligned, and a way to step through the differences.</summary>
internal sealed class DiffView : DockPanel, IDisposable
{
    private static readonly TimeSpan CompareDelay = TimeSpan.FromMilliseconds(150);

    private readonly TextBlock counter = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0), FontSize = 12 };
    private readonly Button previous;
    private readonly Button next;
    private readonly DispatcherTimer compareTimer;
    private CancellationTokenSource? request;
    private IReadOnlyList<DiffHunk> hunks = [];
    private int current = -1;
    private bool isSyncing;
    private bool hasScrolledToFirst;

    public DiffView(IDocument leftDocument, string leftTitle, IDocument rightDocument, string rightTitle, bool isRightEditable, EditorServices services, EditorOptions options)
    {
        Left = new TextEditor(leftDocument, services) { IsDecorated = false };
        Left.Area.IsReadOnly = true;
        Right = new TextEditor(rightDocument, services) { IsDecorated = false };
        ApplyOptions(options);
        Right.Area.IsReadOnly = !isRightEditable;

        previous = IconButton(Icons.ArrowUp, "Previous difference (Shift+F7)", () => Move(-1));
        next = IconButton(Icons.ArrowDown, "Next difference (F7)", () => Move(1));
        counter[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMutedBrush");
        var toolbar = new Border
        {
            Height = 32,
            Padding = new Thickness(8, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { previous, next, counter } },
        };

        var titles = Columns(Title(leftTitle, null), Title(rightTitle, isRightEditable ? "editable" : null));
        titles.Height = 26;
        var line = Divider(horizontal: true);
        foreach (var bar in (Control[])[toolbar, line, titles])
            SetDock(bar, Dock.Top);
        Children.AddRange([toolbar, line, titles, Columns(Left, Right)]);

        Left.Area.ScrollChanged += (_, _) => Follow(Left, Right);
        Right.Area.ScrollChanged += (_, _) => Follow(Right, Left);
        compareTimer = new DispatcherTimer { Interval = CompareDelay };
        compareTimer.Tick += (_, _) =>
        {
            compareTimer.Stop();
            _ = CompareAsync();
        };
        rightDocument.Buffer.Changed += OnRightChanged;
        AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _ = CompareAsync();
    }

    /// <summary>Sets how both sides look; the older side does not highlight the caret's line, since it is only read.</summary>
    public void ApplyOptions(EditorOptions options)
    {
        Left.Options = options with { HighlightCurrentLine = false };
        Right.Options = options;
    }

    /// <summary>Gets the editor of the older side.</summary>
    public TextEditor Left { get; }

    /// <summary>Gets the editor of the newer side, which edits the file when it is the working copy.</summary>
    public TextEditor Right { get; }

    public void Dispose()
    {
        compareTimer.Stop();
        Cancel();
        Right.Document.Buffer.Changed -= OnRightChanged;
        Left.Dispose();
        Right.Dispose();
    }

    /// <summary>Moves to the next or previous difference and scrolls it into the middle.</summary>
    public void Move(int direction)
    {
        if (hunks.Count == 0)
            return;

        current = Math.Clamp(current + direction, 0, hunks.Count - 1);
        var hunk = hunks[current];
        var snapshot = Right.Area.Snapshot;
        var line = Math.Min(hunk.NewStart, snapshot.LineCount - 1);
        Right.Area.Select(EditorSelection.At(snapshot.GetLine(line).Start), scrollIntoView: false);
        Right.Area.ScrollIntoView(snapshot.GetLine(line).Start, center: true);
        UpdateCounter();
    }

    private void OnRightChanged(object? sender, Text.TextChangedEventArgs e)
    {
        compareTimer.Stop();
        compareTimer.Start();
    }

    private void OnKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key != Avalonia.Input.Key.F7 || (e.KeyModifiers & ~Avalonia.Input.KeyModifiers.Shift) != 0)
            return;

        Move(e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift) ? -1 : 1);
        e.Handled = true;
    }

    private async Task CompareAsync()
    {
        Cancel();
        request = new CancellationTokenSource();
        var token = request.Token;
        var left = Left.Area.Snapshot;
        var right = Right.Area.Snapshot;
        try
        {
            var (found, decorations) = await Task.Run(() =>
            {
                var found = TextDiff.Lines(left.GetText(), right.GetText(), token);
                return (found, DiffLayout.Build(left, right, found));
            }, token);
            if (token.IsCancellationRequested || !ReferenceEquals(right, Right.Area.Snapshot))
                return;

            hunks = found;
            Left.Area.Diff = decorations.Left;
            Right.Area.Diff = decorations.Right;
            current = Math.Min(current, hunks.Count - 1);
            UpdateCounter();
            if (!hasScrolledToFirst && hunks.Count > 0)
            {
                hasScrolledToFirst = true;
                Dispatcher.UIThread.Post(() => Move(1), DispatcherPriority.Background);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Cancel()
    {
        request?.Cancel();
        request?.Dispose();
        request = null;
    }

    private void UpdateCounter()
    {
        counter.Text = hunks.Count == 0 ? "No differences"
            : current < 0 ? string.Create(CultureInfo.CurrentCulture, $"{hunks.Count} {(hunks.Count == 1 ? "difference" : "differences")}")
            : string.Create(CultureInfo.CurrentCulture, $"{current + 1} of {hunks.Count}");
        previous.IsEnabled = hunks.Count > 0 && current > 0;
        next.IsEnabled = hunks.Count > 0 && current < hunks.Count - 1;
    }

    // Both sides have as many rows, so they scroll by the same offset.
    private void Follow(TextEditor from, TextEditor to)
    {
        if (isSyncing)
            return;

        isSyncing = true;
        to.Area.ScrollOffset = from.Area.ScrollOffset;
        isSyncing = false;
    }

    private static Grid Columns(Control left, Control right)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,1,*") };
        var divider = Divider(horizontal: false);
        Grid.SetColumn(divider, 1);
        Grid.SetColumn(right, 2);
        grid.Children.AddRange([left, divider, right]);
        return grid;
    }

    private static Border Divider(bool horizontal)
    {
        var divider = horizontal ? new Border { Height = 1 } : new Border { Width = 1 };
        divider[!Border.BackgroundProperty] = new DynamicResourceExtension("BorderSubtleBrush");
        return divider;
    }

    private static StackPanel Title(string title, string? note)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(12, 0), VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        if (note is not null)
        {
            var hint = new TextBlock { Text = note, FontSize = 12 };
            hint[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMutedBrush");
            panel.Children.Add(hint);
        }

        return panel;
    }

    private static Button IconButton(Geometry icon, string tip, Action action)
    {
        var button = new Button { Classes = { "icon", "small" }, Focusable = false, Content = new SymbolIcon { Data = icon, Size = 14 } };
        button.Click += (_, _) => action();
        ToolTip.SetTip(button, tip);
        return button;
    }
}
