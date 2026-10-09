using System.Composition;
using Runesmith.Sdk.Commands;

namespace Runesmith.Shell.Hub;

/// <summary>The commands of the plugin manager and safe mode.</summary>
[Export(typeof(ICommandContributor))]
[method: ImportingConstructor]
public sealed class HubCommands(Lazy<HubService> hub) : ICommandContributor
{
    public const string ShowPlugins = "plugins.show";
    public const string ShowUpdates = "plugins.updates";
    public const string CheckHub = "plugins.checkHub";
    public const string RestartInSafeMode = "help.restartInSafeMode";

    public void Contribute(ICommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Add(new CommandDefinition(ShowPlugins, "Plugins", "Plugins") { Icon = "puzzle", Description = "Show the plugin manager: installed plugins, updates and the plugin hub." },
            _ => Run(() => hub.Value.Show(PluginManagerTab.Installed)));
        registry.Add(new CommandDefinition(ShowUpdates, "Plugin Updates", "Plugins") { Icon = "refresh" }, _ => Run(() => hub.Value.Show(PluginManagerTab.Updates)));
        registry.Add(new CommandDefinition(CheckHub, "Check the Plugin Hub", "Plugins") { Icon = "refresh", Description = "Check the plugin hub for updates and changes to your plugins now." },
            _ => hub.Value.RefreshAsync());
        registry.Add(new CommandDefinition(RestartInSafeMode, "Restart in Safe Mode", "Help")
        {
            Description = "Start Runesmith again with only the plugins that come with it, to find a plugin that causes problems.",
        }, _ => Run(() => hub.Value.Restart(safeMode: true)));
        registry.AddMenuItem(new MenuItemDefinition(Menus.Tools, ShowPlugins, "1-plugins", 0));
        registry.AddMenuItem(new MenuItemDefinition(Menus.Help, RestartInSafeMode, "2-docs", 100));
    }

    private static Task Run(Action action)
    {
        action();
        return Task.CompletedTask;
    }
}
