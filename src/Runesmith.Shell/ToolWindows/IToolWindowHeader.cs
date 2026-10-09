using Avalonia.Controls;

namespace Runesmith.Shell.ToolWindows;

/// <summary>Content of a tool window that puts its own title and actions in its area's header, instead of a header of its own.</summary>
internal interface IToolWindowHeader
{
    /// <summary>Gets the title to show, or null to show the window's title.</summary>
    string? HeaderTitle { get; }

    /// <summary>Gets the buttons to show before the area's menu and hide buttons.</summary>
    Control? HeaderActions { get; }

    /// <summary>Raised when <see cref="HeaderTitle"/> changes.</summary>
    event EventHandler? HeaderChanged;
}
