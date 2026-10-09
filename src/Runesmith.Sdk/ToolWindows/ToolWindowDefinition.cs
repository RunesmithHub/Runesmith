namespace Runesmith.Sdk.ToolWindows;

/// <summary>Where a tool window docks the first time it opens, before the user moves it.</summary>
public enum DockSide
{
    Left,
    Right,
    Bottom,
}

/// <summary>Describes a tool window: a panel such as Explorer or Problems that docks around the editors.</summary>
/// <param name="Id">A unique id, such as <c>explorer</c>; saved layouts refer to it.</param>
/// <param name="Title">The tab's title.</param>
/// <param name="Icon">The name of the tab's icon, such as <c>folder-open</c>.</param>
/// <param name="Side">Where the window docks the first time.</param>
public sealed record ToolWindowDefinition(string Id, string Title, string Icon, DockSide Side)
{
    /// <summary>Gets the window's position among the windows on the same side; lower comes first.</summary>
    public int Order { get; init; }

    /// <summary>Gets whether the window is part of the default layout; others open from the View menu or their command.</summary>
    public bool IsVisibleByDefault { get; init; }

    /// <summary>Gets the key gesture of the command that shows the window, such as <c>Ctrl+Shift+E</c>.</summary>
    public string? KeyBinding { get; init; }
}
