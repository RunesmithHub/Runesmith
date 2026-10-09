using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.VisualStudio.Composition;

namespace Runesmith.Composition;

/// <summary>Finds assemblies by name for VS-MEF, including plugin assemblies that live in their own load contexts.</summary>
internal sealed class PluginAssemblyLoader : IAssemblyLoader
{
    private readonly ConcurrentDictionary<string, Assembly> loaded = new(StringComparer.OrdinalIgnoreCase);

    public void Add(Assembly assembly) => loaded[assembly.GetName().Name!] = assembly;

    public Assembly LoadAssembly(AssemblyName assemblyName) =>
        assemblyName.Name is { } name && loaded.TryGetValue(name, out var assembly) ? assembly : Assembly.Load(assemblyName);

    public Assembly LoadAssembly(string assemblyFullName, string? codeBasePath) => LoadAssembly(new AssemblyName(assemblyFullName));
}
