namespace Runesmith.Sdk.Commands;

/// <summary>A top-level menu of a plugin's own in the main menu, added with <see cref="ICommandRegistry.AddMenu"/>.</summary>
/// <remarks>Plugin menus come after <see cref="Menus.Build"/> and before <see cref="Menus.Tools"/>, by <see cref="Order"/> and then by
/// title. A menu shows only while it has an item whose command exists.</remarks>
/// <param name="Id">The name <see cref="MenuItemDefinition.Menu"/> places items in, such as <c>Docker</c>; it cannot contain <c>/</c> or be
/// one of <see cref="Menus.All"/>.</param>
/// <param name="Title">The title shown in the main menu.</param>
public sealed record MenuDefinition(string Id, string Title)
{
    /// <summary>Gets the menu's position among the plugin menus; lower comes first.</summary>
    public int Order { get; init; }
}
