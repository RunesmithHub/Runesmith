namespace Runesmith.Sdk.Commands;

/// <summary>Adds commands and menu items. Export it with <c>[Export(typeof(ICommandContributor))]</c>.</summary>
public interface ICommandContributor
{
    /// <summary>Registers the contributor's commands and menu items; called once, on the UI thread, while the main window is created.</summary>
    void Contribute(ICommandRegistry registry);
}

/// <summary>Collects commands and menu items while the commands are set up.</summary>
public interface ICommandRegistry
{
    /// <summary>Adds a command.</summary>
    /// <param name="command">What the command is called and where it is listed.</param>
    /// <param name="execute">Runs the command, on the UI thread, with the argument it was invoked with, if any.</param>
    /// <param name="canExecute">Whether the command can run now; asked again whenever menus or the palette show it.</param>
    void Add(CommandDefinition command, Func<object?, Task> execute, Func<object?, bool>? canExecute = null);

    /// <summary>Places a command in a menu of the app bar; a command already in that menu stays where it is.</summary>
    void AddMenuItem(MenuItemDefinition item);

    /// <summary>Adds a top-level menu of the plugin's own; a menu whose id is already taken keeps its first definition.</summary>
    void AddMenu(MenuDefinition menu);
}
