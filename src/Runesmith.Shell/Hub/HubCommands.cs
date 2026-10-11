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
        registry.Add(new CommandDefinition(ShowPlugins, Strings.PluginsShow, Strings.PluginsCategory) { Icon = "puzzle", Description = Strings.PluginsShowDescription },
            _ => Run(() => hub.Value.Show(PluginManagerTab.Installed)));
        registry.Add(new CommandDefinition(ShowUpdates, Strings.PluginsShowUpdates, Strings.PluginsCategory) { Icon = "refresh" }, _ => Run(() => hub.Value.Show(PluginManagerTab.Updates)));
        registry.Add(new CommandDefinition(CheckHub, Strings.PluginsCheckHub, Strings.PluginsCategory) { Icon = "refresh", Description = Strings.PluginsCheckHubDescription },
            _ => hub.Value.RefreshAsync());
        registry.Add(new CommandDefinition(RestartInSafeMode, Strings.PluginsRestartSafeMode, Strings.PluginsCategoryHelp)
        {
            Description = Strings.PluginsRestartSafeModeDescription,
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
