namespace Runesmith.Sdk.Commands;

/// <summary>The context menus plugins add items to with a <see cref="MenuItemDefinition"/>. Their commands receive a
/// <see cref="ContextMenuTarget"/> as the argument, and an item shows only while its command can run with that argument.</summary>
public static class ContextMenus
{
    /// <summary>The editor's context menu, for files saved on disk; the target has the file and the selected lines.</summary>
    public const string Editor = "#editor";

    /// <summary>The Explorer's context menu on a file or folder; the target has its path.</summary>
    public const string Explorer = "#explorer";

    /// <summary>Whether a menu name is a context menu's rather than one of the main menu's.</summary>
    public static bool IsContextMenu(string menu) => menu.StartsWith('#');
}

/// <summary>What a context menu was opened on, given to its commands as their argument.</summary>
/// <param name="Path">The full path of the file or folder.</param>
/// <param name="IsDirectory">Whether the path is a folder.</param>
public sealed record ContextMenuTarget(string Path, bool IsDirectory = false)
{
    /// <summary>Gets the first and last line the editor's selection covers, counted from 1, or null outside the editor. A selection that
    /// ends at the start of a line leaves that line out.</summary>
    public (int First, int Last)? Lines { get; init; }
}
