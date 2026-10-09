using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;

namespace Runesmith.Composition;

/// <summary>Holds every plugin's contracts assembly, once, so every plugin that uses a contract sees the same types.</summary>
internal sealed class ContractsLoadContext() : AssemblyLoadContext("Plugin contracts")
{
    private readonly ConcurrentDictionary<string, Assembly> byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Assembly, PluginInfo> owners = new();

    /// <summary>Loads a plugin's contracts, which may use .NET, the host-shared assemblies and the contracts of the plugins it depends on.</summary>
    /// <param name="dependencyContracts">The names of the contracts assemblies of the plugins it depends on.</param>
    /// <returns>Why the contracts cannot be loaded, or null when they were.</returns>
    /// <exception cref="IOException">The assembly cannot be read.</exception>
    /// <exception cref="BadImageFormatException">The file is not an assembly.</exception>
    public string? Add(PluginInfo plugin, IReadOnlySet<string> dependencyContracts)
    {
        var path = plugin.ContractsPath ?? throw new ArgumentException("The plugin has no contracts.", nameof(plugin));
        foreach (var reference in References(path))
        {
            if (!HostRuntime.IsFramework(reference) && !HostRuntime.IsShared(reference) && !dependencyContracts.Contains(reference))
                return $"Its contracts use {reference}, which is not .NET, a library Runesmith shares or the contracts of a plugin it depends on.";
        }

        var assembly = LoadFromAssemblyPath(path);
        byName[assembly.GetName().Name!] = assembly;
        owners[assembly] = plugin;
        return null;
    }

    /// <summary>Gets a loaded contracts assembly by name, or null.</summary>
    public Assembly? Get(string name) => byName.TryGetValue(name, out var assembly) ? assembly : null;

    /// <summary>Gets the plugin whose contracts an assembly is, or null.</summary>
    public PluginInfo? OwnerOf(Assembly assembly) => owners.TryGetValue(assembly, out var plugin) ? plugin : null;

    protected override Assembly? Load(AssemblyName assemblyName) => assemblyName.Name is { } name ? Get(name) : null;

    private static List<string> References(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        if (!reader.HasMetadata)
            throw new BadImageFormatException($"{path} is not an assembly.", path);

        var metadata = reader.GetMetadataReader();
        return [.. metadata.AssemblyReferences.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))];
    }
}
