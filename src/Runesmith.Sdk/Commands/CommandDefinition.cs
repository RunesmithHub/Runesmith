namespace Runesmith.Sdk.Commands;

/// <summary>Describes a command: what it is called, where it is listed and the key it is bound to by default.</summary>
/// <param name="Id">A unique id, conventionally <c>area.action</c> such as <c>editor.toggleComment</c>; key bindings and menus refer to it.</param>
/// <param name="Title">The name shown in menus and the command palette, such as "Toggle Line Comment".</param>
/// <param name="Category">The palette's group, such as "Editor" or "File".</param>
public sealed record CommandDefinition(string Id, string Title, string Category)
{
    /// <summary>Gets the name of the icon shown beside the command, such as <c>save</c>; see HammerUI's <c>Icons.Find</c>.</summary>
    public string? Icon { get; init; }

    /// <summary>Gets the key gesture the command is bound to unless the user rebinds it, such as <c>Ctrl+Shift+P</c>.</summary>
    public string? KeyBinding { get; init; }

    /// <summary>Gets a short explanation shown in the command palette and in tooltips.</summary>
    public string? Description { get; init; }

    /// <summary>Gets whether the command palette lists the command; commands that only make sense from a menu or a key can opt out.</summary>
    public bool ShowInPalette { get; init; } = true;
}
