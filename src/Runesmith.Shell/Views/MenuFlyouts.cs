using Avalonia.Controls;

namespace Runesmith.Shell.Views;

/// <summary>Menu flyouts whose items are made each time they open.</summary>
internal static class MenuFlyouts
{
    /// <summary>Creates a menu flyout that clears its items and fills them again as it opens.</summary>
    public static MenuFlyout Dynamic(Action<MenuFlyout> fill, PlacementMode? placement = null)
    {
        var menu = new MenuFlyout();
        if (placement is { } mode)
            menu.Placement = mode;
        // A menu flyout whose items were never set shows none of the items added when it opens.
        menu.Items.Add(new Separator());
        menu.Items.Clear();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            fill(menu);
        };
        return menu;
    }
}
