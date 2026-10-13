namespace Runesmith.Composition;

/// <summary>Finds assemblies a plugin was built against in a newer version than the one Runesmith loads for it.</summary>
/// <remarks>Such a reference only fails when the code that uses it first runs, often while VS-MEF scans the plugin's types, with an error
/// that names neither version. Reading the references up front lets the plugin fail with one message that says what is out of date.</remarks>
internal static class HostVersionCheck
{
    /// <summary>Gets why a plugin cannot run with the assemblies Runesmith has, or null when every assembly it gets from Runesmith is at
    /// least the version it was built against.</summary>
    /// <remarks>Reads the implementation, the contracts and the other assemblies beside the implementation, since a library the plugin
    /// carries is built against Runesmith's assemblies too.</remarks>
    /// <exception cref="IOException">An assembly cannot be read.</exception>
    /// <exception cref="UnauthorizedAccessException">An assembly cannot be read.</exception>
    public static string? Problem(PluginInfo plugin)
    {
        var folder = Path.GetDirectoryName(plugin.AssemblyPath)!;
        var paths = new List<string> { plugin.AssemblyPath };
        if (plugin.ContractsPath is { } contracts)
            paths.Add(contracts);
        paths.AddRange(Directory.EnumerateFiles(folder, "*.dll").Where(path => !paths.Contains(path, StringComparer.Ordinal)).Order(StringComparer.Ordinal));

        foreach (var path in paths)
        {
            foreach (var (name, wanted) in AssemblyReferences.Read(path))
            {
                if (!FromHost(name, folder, plugin.Manifest.IsLegacy) || HostRuntime.VersionOf(name) is not { } host || host >= wanted)
                    continue;

                var file = Path.GetFileName(path);
                return $"{file} was built against {name} {wanted}, and this Runesmith has {name} {host}. "
                    + "Update Runesmith; if you built Runesmith yourself, rebuild it so every project is current.";
            }
        }

        return null;
    }

    private static bool FromHost(string name, string folder, bool seesHost)
    {
        if (HostRuntime.IsShared(name))
            return true;

        // A plugin's own copy of a .NET or Runesmith library loads in place of an older one from the host.
        var hostHasIt = HostRuntime.IsFramework(name) || (seesHost && HostRuntime.IsHostOnly(name));
        return hostHasIt && !File.Exists(Path.Combine(folder, name + ".dll"));
    }
}
