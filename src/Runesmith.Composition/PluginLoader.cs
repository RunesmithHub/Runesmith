using System.Reflection;
using RunesmithHub.Protocol.Versioning;

namespace Runesmith.Composition;

/// <summary>Loads plugins in dependency order, each into its own load context, with every plugin's contracts in one shared context.</summary>
/// <remarks>A plugin loads after the plugins it depends on, and does not load when one of them is missing, turned off, of a version
/// outside its range or failed; its <see cref="PluginInfo.Error"/> says which.</remarks>
internal sealed class PluginLoader
{
    private readonly List<PluginInfo> plugins;
    private readonly Dictionary<string, int> byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PluginInfo> pluginAssemblies = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="plugins">The plugins discovery found; the loader records in it which fail.</param>
    public PluginLoader(List<PluginInfo> plugins)
    {
        this.plugins = plugins;
        for (var i = 0; i < plugins.Count; i++)
        {
            var plugin = plugins[i];
            if (plugin.State is PluginState.Replaced or PluginState.Refused)
                continue;

            byId.TryAdd(plugin.Manifest.Id, i);
            if (plugin.Manifest.Assembly.Length == 0)
                continue;

            pluginAssemblies.TryAdd(Path.GetFileNameWithoutExtension(plugin.Manifest.Assembly), plugin);
            if (plugin.Manifest.ContractsAssembly is { } contracts)
                pluginAssemblies.TryAdd(Path.GetFileNameWithoutExtension(contracts), plugin);
        }
    }

    /// <summary>Gets the context that holds every plugin's contracts.</summary>
    public ContractsLoadContext Contracts { get; } = new();

    /// <summary>Loads every plugin that can load.</summary>
    /// <returns>The index of each loaded plugin and its implementation assembly, dependencies first.</returns>
    public List<(int Index, Assembly Assembly)> Load()
    {
        var loaded = new List<(int, Assembly)>();
        foreach (var index in DependencyOrder())
        {
            if (Blocker(plugins[index]) is { } blocker)
            {
                plugins[index] = plugins[index].Fail(blocker);
                continue;
            }

            try
            {
                if (LoadPlugin(plugins[index]) is { } assembly)
                    loaded.Add((index, assembly));
            }
            catch (Exception exception) when (exception is IOException or BadImageFormatException or UnauthorizedAccessException)
            {
                plugins[index] = plugins[index].Fail($"The assembly could not be loaded: {exception.Message}");
            }
        }

        return loaded;
    }

    /// <summary>Gets why a plugin cannot run because of one of its dependencies, or null when they all run.</summary>
    public string? Blocker(PluginInfo plugin)
    {
        foreach (var dependency in plugin.Manifest.Dependencies)
        {
            if (!byId.TryGetValue(dependency.Id, out var index))
                return $"It needs {dependency.Id} {dependency.Range}, which is not installed.";

            var found = plugins[index];
            var name = found.Manifest.Name;
            if (found.State == PluginState.Disabled)
                return $"It needs {name}, which is turned off.";
            if (found.State == PluginState.Failed)
                return $"It needs {name}, which could not load.";
            if (found.Manifest.IsLegacy)
                return $"It needs {name}, whose manifest has no contracts for other plugins.";
            if (!SemanticVersion.TryParse(found.Manifest.Version, out var version) || !dependency.Range.Contains(version))
                return $"It needs {name} {dependency.Range}, and version {found.Manifest.Version} is installed.";
        }

        return null;
    }

    private Assembly? LoadPlugin(PluginInfo plugin)
    {
        var dependencyContracts = plugin.Manifest.Dependencies
            .Select(d => Path.GetFileNameWithoutExtension(plugins[byId[d.Id]].Manifest.ContractsAssembly!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visible = new HashSet<string>(dependencyContracts, StringComparer.OrdinalIgnoreCase);
        if (plugin.Manifest.ContractsAssembly is { } own)
        {
            if (Contracts.Add(plugin, dependencyContracts) is { } problem)
            {
                plugins[byId[plugin.Manifest.Id]] = plugin.Fail(problem);
                return null;
            }

            visible.Add(Path.GetFileNameWithoutExtension(own));
        }

        return new PluginLoadContext(plugin, Contracts, visible, pluginAssemblies).LoadFromAssemblyPath(plugin.AssemblyPath);
    }

    private List<int> DependencyOrder()
    {
        var pending = Enumerable.Range(0, plugins.Count).Where(i => plugins[i].State == PluginState.Loaded).ToList();
        var order = new List<int>();
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (pending.Count > 0)
        {
            var ready = pending.Where(i => plugins[i].Manifest.Dependencies.All(d => placed.Contains(d.Id) || !pending.Any(p => SameId(p, d.Id)))).ToList();
            if (ready.Count == 0)
            {
                var cycle = string.Join(", ", pending.Select(i => plugins[i].Manifest.Name));
                foreach (var index in pending)
                    plugins[index] = plugins[index].Fail($"Its dependencies form a cycle among {cycle}.");
                break;
            }

            foreach (var index in ready)
            {
                order.Add(index);
                placed.Add(plugins[index].Manifest.Id);
                pending.Remove(index);
            }
        }

        return order;
    }

    private bool SameId(int index, string id) => string.Equals(plugins[index].Manifest.Id, id, StringComparison.OrdinalIgnoreCase);
}
