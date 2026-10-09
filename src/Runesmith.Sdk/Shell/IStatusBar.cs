namespace Runesmith.Sdk.Shell;

/// <summary>Which end of the status bar an item sits at.</summary>
public enum StatusBarAlignment
{
    Left,
    Right,
}

/// <summary>What an item's icon shows.</summary>
public enum StatusBarState
{
    Normal,
    Busy,
    Success,
    Warning,
    Error,
}

/// <summary>An item in the status bar that its owner updates; disposing it removes it.</summary>
/// <remarks>Its members can be set from any thread.</remarks>
public interface IStatusBarItem : IDisposable
{
    string Text { get; set; }

    string? Icon { get; set; }

    string? ToolTip { get; set; }

    StatusBarState State { get; set; }

    /// <summary>Gets or sets the command run when the item is clicked; without one the item is not clickable.</summary>
    string? CommandId { get; set; }

    bool IsVisible { get; set; }
}

/// <summary>Adds items to the status bar.</summary>
public interface IStatusBar
{
    /// <param name="id">A unique id for the item.</param>
    /// <param name="priority">Items with a higher priority sit further toward the outer end of their side.</param>
    IStatusBarItem AddItem(string id, StatusBarAlignment alignment, int priority = 0);
}
