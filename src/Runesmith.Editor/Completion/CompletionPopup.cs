using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using HammerUI.Controls;
using Runesmith.Editor.Popups;
using Runesmith.Sdk.Languages;

namespace Runesmith.Editor.Completion;

/// <summary>A suggestion as listed: the item, its score against the typed word and which characters matched.</summary>
internal sealed record CompletionEntry(CompletionItem Item, int Score, IReadOnlyList<int> Matched);

/// <summary>The completion list under the caret, with the selected item's documentation beside it.</summary>
internal sealed class CompletionPopup : Popup
{
    private const int VisibleRows = 12;

    private readonly CompletionListView list;
    private readonly ScrollViewer details;
    private readonly ContentControl detailsContent = new();

    public CompletionPopup(Control placementTarget)
    {
        PlacementTarget = placementTarget;
        Placement = PlacementMode.AnchorAndGravity;
        PlacementAnchor = Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.BottomLeft;
        PlacementGravity = Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.BottomRight;
        PlacementConstraintAdjustment = Avalonia.Controls.Primitives.PopupPositioning.PopupPositionerConstraintAdjustment.FlipY
            | Avalonia.Controls.Primitives.PopupPositioning.PopupPositionerConstraintAdjustment.SlideX;
        IsLightDismissEnabled = false;
        Focusable = false;

        list = new CompletionListView { Width = 380, VisibleRows = VisibleRows };
        list.SelectedChanged += (_, _) => SelectedChanged?.Invoke(this, EventArgs.Empty);
        list.Accepted += (_, _) => Accepted?.Invoke(this, EventArgs.Empty);

        details = new ScrollViewer
        {
            Width = 340,
            MaxHeight = VisibleRows * CompletionListView.RowHeight + 8,
            Padding = new Thickness(12, 10),
            Content = detailsContent,
            IsVisible = false,
        };
        var separator = new Border { Width = 1 };
        separator[!Border.BackgroundProperty] = new DynamicResourceExtension("BorderSubtleBrush");
        separator[!IsVisibleProperty] = details[!IsVisibleProperty];

        var layout = new DockPanel();
        DockPanel.SetDock(list, Dock.Left);
        DockPanel.SetDock(separator, Dock.Left);
        layout.Children.Add(list);
        layout.Children.Add(separator);
        layout.Children.Add(details);
        Child = new Border { Classes = { "floating" }, Child = layout, ClipToBounds = true };
    }

    public event EventHandler? SelectedChanged;

    public event EventHandler? Accepted;

    public CompletionEntry? Selected => list.Selected;

    public int Count => list.Count;

    public void SetEntries(IReadOnlyList<CompletionEntry> entries, int selectedIndex) => list.SetEntries(entries, selectedIndex);

    /// <summary>Moves the selection by some rows, wrapping around at the ends when moving one row.</summary>
    public void MoveSelection(int rows) => list.MoveSelection(rows);

    public void ShowDetails(CompletionItem? item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Detail) && string.IsNullOrWhiteSpace(item.Documentation))
        {
            details.IsVisible = false;
            return;
        }

        var panel = new StackPanel { Spacing = 8 };
        if (!string.IsNullOrWhiteSpace(item.Detail))
        {
            panel.Children.Add(new SelectableTextBlock
            {
                Text = item.Detail,
                FontFamily = new FontFamily(EditorOptions.BundledFontFamily),
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        if (!string.IsNullOrWhiteSpace(item.Documentation))
            panel.Children.Add(MarkdownView.Create(item.Documentation));

        detailsContent.Content = panel;
        details.IsVisible = true;
    }
}
