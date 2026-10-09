namespace Runesmith.Sdk.Commands;

/// <summary>Places a command in one of the main menu's menus, or in a context menu of <see cref="ContextMenus"/>.</summary>
/// <param name="Menu">The menu, such as <see cref="Menus.File"/>, a menu of the plugin's own such as <c>Git</c>, or a context menu such as
/// <see cref="ContextMenus.Editor"/>; a path such as <c>View/Appearance</c> places it in a submenu.</param>
/// <param name="CommandId">The command the item runs.</param>
/// <param name="Group">Items of one group stay together; groups are separated by a line and sorted by name.</param>
/// <param name="Order">The item's position within its group.</param>
public sealed record MenuItemDefinition(string Menu, string CommandId, string Group = "default", int Order = 0);

/// <summary>The names of the main menu's menus.</summary>
public static class Menus
{
    public const string File = "File";
    public const string Edit = "Edit";
    public const string Selection = "Selection";
    public const string View = "View";
    public const string Go = "Go";
    public const string Run = "Run";
    public const string Build = "Build";
    public const string Tools = "Tools";
    public const string Help = "Help";

    /// <summary>Runesmith's menus in the order the main menu shows them. Menus that plugins name otherwise, such as <c>Git</c>, come after
    /// <see cref="Build"/> and before <see cref="Tools"/>, ordered as <see cref="MenuDefinition"/> describes.</summary>
    public static IReadOnlyList<string> All { get; } = [File, Edit, Selection, View, Go, Run, Build, Tools, Help];
}
