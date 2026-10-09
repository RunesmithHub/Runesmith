using System.Composition;
using Avalonia.Controls;
using Runesmith.Sdk.ToolWindows;
using Runesmith.Shell.Pages;
using Runesmith.Shell.Services;

namespace Runesmith.Shell.Hub;

/// <summary>The Plugins tool window: the plugin manager beside the editor, the same one the welcome screen's Plugins page shows.</summary>
[Export(typeof(IToolWindowProvider))]
[method: ImportingConstructor]
public sealed class PluginsToolWindow(Lazy<HubService> hub, NotificationService notifications, OutputService output, Lazy<PageService> pages) : IToolWindowProvider
{
    public ToolWindowDefinition Definition { get; } = new(HubService.ToolWindowId, "Plugins", "puzzle", DockSide.Right) { Order = 20 };

    public Control CreateContent() =>
        new PluginManagerView(hub.Value, notifications, output, compact: true, () => pages.Value.ShowSettings(HubSettings.Category));
}
