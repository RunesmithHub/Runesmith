using System.Reflection;
using System.Runtime.Loader;

namespace Runesmith.Composition;

/// <summary>Loads one plugin's implementation and the libraries it carries apart from other plugins, so plugins can carry different
/// versions of the same library.</summary>
/// <remarks>The plugin sees its own assemblies, its own contracts and those of the plugins it depends on, the host-shared assemblies such as
/// the SDK, Avalonia and HammerUI, and .NET; it gets a newer copy of a .NET library it carries itself. Plugins that ship with Runesmith, and
/// plugins with manifests in the older format, also see Runesmith's other assemblies. Another plugin's implementation, and the contracts of a
/// plugin it does not depend on, cannot be loaded.</remarks>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver resolver;
    private readonly string folder;
    private readonly ContractsLoadContext contracts;
    private readonly IReadOnlySet<string> visibleContracts;
    private readonly IReadOnlyDictionary<string, PluginInfo> pluginAssemblies;
    private readonly bool seesHost;

    /// <param name="visibleContracts">The names of the contracts assemblies the plugin may use: its own and its dependencies'.</param>
    /// <param name="pluginAssemblies">Every plugin's contracts and implementation assembly names, and the plugin each belongs to.</param>
    public PluginLoadContext(PluginInfo plugin, ContractsLoadContext contracts, IReadOnlySet<string> visibleContracts, IReadOnlyDictionary<string, PluginInfo> pluginAssemblies)
        : base(plugin.Manifest.Id)
    {
        Plugin = plugin;
        resolver = new AssemblyDependencyResolver(plugin.AssemblyPath);
        folder = Path.GetDirectoryName(plugin.AssemblyPath)!;
        this.contracts = contracts;
        this.visibleContracts = visibleContracts;
        this.pluginAssemblies = pluginAssemblies;
        seesHost = plugin.Manifest.IsLegacy;
    }

    /// <summary>Gets the plugin whose assemblies this context loads.</summary>
    public PluginInfo Plugin { get; }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is not { } name)
            return null;

        if (!string.IsNullOrEmpty(assemblyName.CultureName))
            return resolver.ResolveAssemblyToPath(assemblyName) is { } satellite ? LoadFromAssemblyPath(satellite) : null;

        if (HostRuntime.IsShared(name))
            return null;

        if (visibleContracts.Contains(name))
            return contracts.Get(name) ?? throw new FileNotFoundException($"{Plugin.Manifest.Name} needs the contracts {name}, which are not loaded.", name);

        if (pluginAssemblies.TryGetValue(name, out var owner) && !string.Equals(owner.Manifest.Id, Plugin.Manifest.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException(owner.ContractsPath is { } path && string.Equals(Path.GetFileNameWithoutExtension(path), name, StringComparison.OrdinalIgnoreCase)
                ? $"{Plugin.Manifest.Name} cannot use {name}: a plugin can only use the contracts of plugins it depends on, and it does not depend on {owner.Manifest.Name}."
                : $"{Plugin.Manifest.Name} cannot use {name}: it is the implementation of {owner.Manifest.Name}, which other plugins cannot use.", name);
        }

        var own = Own(assemblyName);
        var hostHasIt = HostRuntime.IsFramework(name) || (seesHost && HostRuntime.IsHostOnly(name));
        if (hostHasIt && (own is null || assemblyName.Version is not { } wanted || HostRuntime.VersionOf(name) is { } host && host >= wanted))
            return null;

        if (own is not null)
            return LoadFromAssemblyPath(own);

        if (HostRuntime.IsHostOnly(name))
            throw new FileNotFoundException($"{Plugin.Manifest.Name} cannot use {name}: plugins can use Runesmith's SDK and the libraries it shares, not the rest of Runesmith.", name);

        return null;
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? 0 : LoadUnmanagedDllFromPath(path);
    }

    private string? Own(AssemblyName assemblyName)
    {
        if (resolver.ResolveAssemblyToPath(assemblyName) is { } resolved)
            return resolved;

        var path = Path.Combine(folder, assemblyName.Name + ".dll");
        return File.Exists(path) ? path : null;
    }
}
