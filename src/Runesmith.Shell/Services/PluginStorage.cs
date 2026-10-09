using System.Composition;
using Runesmith.Composition;
using Runesmith.Sdk;
using Runesmith.Sdk.Plugins;
using RunesmithHub.Protocol;

namespace Runesmith.Shell.Services;

/// <summary>Gives each plugin the folder <c>plugins/&lt;id&gt;</c> in Runesmith's data folder.</summary>
[Export(typeof(IPluginStorage))]
[Shared]
public sealed class PluginStorage : IPluginStorage
{
    private readonly string root;
    private readonly Func<PluginInfo?> caller;

    public PluginStorage()
        : this(Path.Combine(RunesmithPaths.Data, "plugins"), PluginCallers.Current)
    {
    }

    internal PluginStorage(string root, Func<PluginInfo?> caller)
    {
        this.root = root;
        this.caller = caller;
    }

    public string GetFolder()
    {
        var plugin = caller() ?? throw new InvalidOperationException("Only plugins have a storage folder.");
        if (!PluginId.IsValid(plugin.Manifest.Id))
            throw new InvalidOperationException($"{plugin.Manifest.Id} is not a plugin id that can name a folder; ids look like publisher.name.");

        var folder = Path.Combine(root, plugin.Manifest.Id);
        Directory.CreateDirectory(folder);
        return folder;
    }
}
