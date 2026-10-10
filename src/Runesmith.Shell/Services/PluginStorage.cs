using System.Composition;
using Runesmith.Composition;
using Runesmith.Sdk;
using Runesmith.Sdk.Plugins;
using RunesmithHub.Protocol;

namespace Runesmith.Shell.Services;

/// <summary>Gives each plugin the folder <c>plugin-data/&lt;id&gt;</c> in Runesmith's data folder, and a local copy of a plugin Runesmith ships or
/// the hub installed <c>plugin-data/local/&lt;id&gt;</c>, apart from that plugin's.</summary>
[Export(typeof(IPluginStorage))]
[Shared]
public sealed class PluginStorage : IPluginStorage
{
    /// <summary>The folder local copies' storage folders are in; no plugin id can name it, as ids have a dot.</summary>
    public const string LocalCopies = "local";

    private readonly string root;
    private readonly Func<PluginInfo?> caller;

    public PluginStorage()
        : this(Path.Combine(RunesmithPaths.Data, "plugin-data"), PluginCallers.Current)
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

        var folder = plugin.IsLocalCopy ? Path.Combine(root, LocalCopies, plugin.Manifest.Id) : Path.Combine(root, plugin.Manifest.Id);
        Directory.CreateDirectory(folder);
        return folder;
    }
}
