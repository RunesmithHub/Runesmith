using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using HammerUI;
using HammerUI.Controls;
using HammerUI.Theming;
using Runesmith.Text;

namespace Runesmith.Editor.Popups;

/// <summary>Shows what a run of changed lines was in the base, under the run, with Rollback, the previous and next change, and the diff.</summary>
internal sealed class ChangePopup : Popup
{
    private readonly TextArea area;
    private readonly TextBlock caption = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 16, 0) };
    private readonly Border oldText = new() { Padding = new Thickness(TextArea.TextPadding - 1, 4), MaxHeight = 320, IsVisible = false };
    private readonly Button rollback;
    private DiffHunk shown;

    public ChangePopup(TextArea area)
    {
        this.area = area;
        PlacementTarget = area;
        Placement = PlacementMode.AnchorAndGravity;
        PlacementAnchor = PopupAnchor.BottomLeft;
        PlacementGravity = PopupGravity.BottomRight;
        PlacementConstraintAdjustment = PopupPositionerConstraintAdjustment.FlipY | PopupPositionerConstraintAdjustment.SlideX;
        IsLightDismissEnabled = true;
        caption[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMutedBrush");

        rollback = TextAction(Icons.Undo, "Rollback", "Put the lines back as they were", () => area.Rollback([shown]));
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Margin = new Thickness(6, 4),
            Children =
            {
                caption,
                IconAction(Icons.ArrowUp, "Previous change", () => Move(-1)),
                IconAction(Icons.ArrowDown, "Next change", () => Move(1)),
                rollback,
                TextAction(Icons.GitCompare, "Show Diff", "Show the file and its base side by side", () => ShowDiffRequested?.Invoke(this, EventArgs.Empty)),
            },
        };
        var divider = new Border { Height = 1 };
        divider[!Border.BackgroundProperty] = new DynamicResourceExtension("BorderSubtleBrush");
        var layout = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        DockPanel.SetDock(divider, Dock.Top);
        layout.Children.AddRange([bar, divider, oldText]);
        Child = new Border { Classes = { "floating" }, Padding = new Thickness(0), MaxWidth = 760, ClipToBounds = true, Child = layout };

        area.LineChangesChanged += (_, _) =>
        {
            if (IsOpen && !area.LineChanges.Contains(shown))
                Hide();
        };
    }

    /// <summary>Raised when the user asks to see the whole file's changes.</summary>
    public event EventHandler? ShowDiffRequested;

    public void Show(DiffHunk change)
    {
        shown = change;
        var count = change.IsDeletion ? change.OldLength : change.NewLength;
        caption.Text = change.IsAddition ? Lines(count, "added")
            : change.IsDeletion ? Lines(count, "deleted")
            : Lines(count, "changed");
        ToolTip.SetTip(rollback, change.IsAddition ? "Delete the added lines" : "Put the lines back as they were");

        var lines = area.GetBaseLines(change);
        oldText.IsVisible = lines.Count > 0;
        if (lines.Count > 0)
        {
            oldText.Background = RemovedTint();
            oldText.Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = new SelectableTextBlock
                {
                    Text = string.Join('\n', lines),
                    FontFamily = area.Formatting.FontFamily,
                    FontSize = area.Formatting.FontSize,
                    LineHeight = area.Formatting.LineHeight,
                },
            };
        }

        var marker = area.GetChangeMarkerRect(change);
        PlacementRect = new Rect(area.GutterWidth, marker.Bottom, 1, 1);
        IsOpen = true;
    }

    public void Hide() => IsOpen = false;

    private static string Lines(int count, string what) => count == 1 ? $"1 line {what}" : $"{count} lines {what}";

    private IBrush RemovedTint() =>
        area.TryFindResource(ThemeKeys.DangerColor, area.ActualThemeVariant, out var value) && value is Color color
            ? new ImmutableSolidColorBrush(color, 0.1)
            : Brushes.Transparent;

    private void Move(int direction)
    {
        var changes = area.LineChanges;
        if (changes.Count == 0)
            return;

        var index = changes.ToList().IndexOf(shown);
        var next = changes[((index < 0 ? 0 : index) + direction + changes.Count) % changes.Count];
        area.GoToChange(next);
        Show(next);
    }

    private static Button IconAction(Geometry icon, string tip, Action action)
    {
        var button = new Button { Classes = { "icon", "small" }, Focusable = false, Content = new SymbolIcon { Data = icon, Size = 14 } };
        button.Click += (_, _) => action();
        ToolTip.SetTip(button, tip);
        return button;
    }

    private Button TextAction(Geometry icon, string text, string tip, Action action)
    {
        var button = new Button
        {
            Classes = { "small" },
            Focusable = false,
            Margin = new Thickness(4, 0, 0, 0),
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new SymbolIcon { Data = icon, Size = 14 }, new TextBlock { Text = text } } },
        };
        button.Click += (_, _) =>
        {
            Hide();
            action();
            area.Focus();
        };
        ToolTip.SetTip(button, tip);
        return button;
    }
}
