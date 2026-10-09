using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;

namespace Runesmith.Editor.Popups;

/// <summary>A popup of the editor that shows information next to a span of text without taking the keyboard focus.</summary>
internal sealed class EditorPopup : Popup
{
    private readonly ContentControl content = new();

    /// <param name="above">Whether the popup opens above the text, as signature help does, rather than below.</param>
    public EditorPopup(Control placementTarget, bool above)
    {
        PlacementTarget = placementTarget;
        Placement = PlacementMode.AnchorAndGravity;
        PlacementAnchor = above ? PopupAnchor.TopLeft : PopupAnchor.BottomLeft;
        PlacementGravity = above ? PopupGravity.TopRight : PopupGravity.BottomRight;
        PlacementConstraintAdjustment = PopupPositionerConstraintAdjustment.FlipY | PopupPositionerConstraintAdjustment.SlideX;
        IsLightDismissEnabled = false;
        Focusable = false;
        Child = new Border
        {
            Classes = { "floating" },
            Padding = new Thickness(12, 8),
            MaxWidth = 560,
            Child = new ScrollViewer { MaxHeight = 320, Content = content },
        };
    }

    /// <summary>Shows content next to a rectangle of the placement target.</summary>
    public void Show(Control control, Rect anchor)
    {
        content.Content = control;
        PlacementRect = anchor;
        IsOpen = true;
    }

    public void Hide() => IsOpen = false;
}
