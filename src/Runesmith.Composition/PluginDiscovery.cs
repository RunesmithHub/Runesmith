using System.Reflection;
using Runesmith.Sdk.Plugins;
using RunesmithHub.Protocol.Versioning;

namespace Runesmith.Composition;

/// <summary>Finds plugins in folders and checks that each can be loaded.</summary>
/// <remarks>Every subfolder of a plugins folder that holds a <c>plugin.json</c> is a plugin.</remarks>
public static class PluginDiscovery
{
    /// <summary>Gets the plugin API version this Runesmith provides.</summary>
    public static SemanticVersion ApiVersion { get; } = ToSemanticVersion(RunesmithApi.Version);

    /// <summary>Gets the oldest plugin API version this Runesmith runs plugins for.</summary>
    public static SemanticVersion OldestSupportedApiVersion { get; } = ToSemanticVersion(RunesmithApi.OldestSupported);

    /// <summary>Finds the plugins in the given folders; earlier folders come first, and a plugin found again later is reported as a
    /// duplicate, except that a newer copy the hub installed, or a local copy at least as new, replaces the one Runesmith ships.</summary>
    /// <param name="disabledIds">The ids of plugins the user turned off.</param>
    /// <param name="hubIds">The ids of the plugins in a local folder that the plugin hub installed.</param>
    /// <param name="withheld">The plugins Runesmith keeps from loading, with why.</param>
    public static IReadOnlyList<PluginInfo> Discover(
        IEnumerable<(string Path, PluginSource Source)> folders, IReadOnlySet<string> disabledIds, IReadOnlySet<string>? hubIds = null,
        IReadOnlyDictionary<string, string>? withheld = null)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(disabledIds);
        var plugins = new List<PluginInfo>();
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (folder, folderSource) in folders)
        {
            if (!Directory.Exists(folder))
                continue;

            foreach (var directory in Directory.EnumerateDirectories(folder).Order(StringComparer.Ordinal))
            {
                var manifestPath = Path.Combine(directory, PluginManifest.FileName);
                if (!File.Exists(manifestPath))
                    continue;

                var source = folderSource == PluginSource.Local && hubIds?.Contains(Path.GetFileName(directory)) == true ? PluginSource.Hub : folderSource;
                PluginInfo plugin;
                try
                {
                    plugin = Validate(new PluginInfo(PluginManifest.Read(manifestPath), directory, source), disabledIds, withheld);
                }
                catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    var name = Path.GetFileName(directory);
                    plugin = new PluginInfo(new PluginManifest(name, name, "?"), directory, source).Fail(exception.Message);
                }

                if (seen.TryGetValue(plugin.Manifest.Id, out var earlier))
                {
                    if (ReplacesBundled(plugin, plugins[earlier]))
                    {
                        plugins[earlier] = plugins[earlier] with { State = PluginState.Replaced, Error = $"Version {plugin.Manifest.Version} {Origin(plugin)} runs instead." };
                        seen[plugin.Manifest.Id] = plugins.Count;
                    }
                    else if (plugin.Source is PluginSource.Hub or PluginSource.Local && plugins[earlier].Source == PluginSource.Bundled)
                    {
                        var instead = $"Runesmith ships version {plugins[earlier].Manifest.Version}, which runs instead.";
                        plugin = plugin with { State = PluginState.Replaced, Error = plugin.Error is { } error ? $"Version {plugin.Manifest.Version} {Origin(plugin)} could not load: {error} {instead}" : instead };
                    }
                    else
                    {
                        plugin = plugin.Fail($"Another plugin with the id {plugin.Manifest.Id} was loaded first.");
                    }
                }
                else
                {
                    seen[plugin.Manifest.Id] = plugins.Count;
                }

                plugins.Add(plugin);
            }
        }

        return plugins;
    }

    // A copy replaces the bundled one only when it can run and is newer, so the bundled copy stays the fallback; a local copy, as a plugin's
    // developer installs it, may also have the same version.
    private static bool ReplacesBundled(PluginInfo copy, PluginInfo bundled) =>
        copy.Source is PluginSource.Hub or PluginSource.Local && bundled.Source == PluginSource.Bundled && copy.State == PluginState.Loaded
        && SemanticVersion.TryParse(copy.Manifest.Version, out var version) && SemanticVersion.TryParse(bundled.Manifest.Version, out var shipped)
        && (version > shipped || (copy.Source == PluginSource.Local && version == shipped));

    private static string Origin(PluginInfo plugin) => plugin.Source == PluginSource.Hub ? "from the hub" : "installed locally";

    /// <summary>Gets whether a plugin that works with the API versions in <paramref name="range"/> runs on an API: the API's version is in
    /// the range, and the range's lowest version, the one the plugin was built against, is at least the oldest version the API supports.</summary>
    public static bool IsApiCompatible(VersionRange range, SemanticVersion api, SemanticVersion oldestSupported)
    {
        ArgumentNullException.ThrowIfNull(range);
        return range.Contains(api) && range.Minimum >= oldestSupported;
    }

    private static PluginInfo Validate(PluginInfo plugin, IReadOnlySet<string> disabledIds, IReadOnlyDictionary<string, string>? withheld)
    {
        var manifest = plugin.Manifest;
        if (plugin.Source != PluginSource.Bundled && withheld?.TryGetValue(manifest.Id, out var reason) == true)
            return plugin with { State = PluginState.Disabled, Error = reason };
        if (disabledIds.Contains(manifest.Id))
            return plugin with { State = PluginState.Disabled };

        if (ApiProblem(manifest) is { } problem)
            return plugin.Fail(problem);

        var folder = Path.GetFullPath(plugin.Directory) + Path.DirectorySeparatorChar;
        foreach (var (relative, path) in (ReadOnlySpan<(string?, string?)>)[(manifest.Assembly, plugin.AssemblyPath), (manifest.ContractsAssembly, plugin.ContractsPath)])
        {
            if (relative is null || path is null)
                continue;
            if (!path.StartsWith(folder, StringComparison.Ordinal))
                return plugin.Fail("The assembly must be inside the plugin's folder.");
            if (!File.Exists(path))
                return plugin.Fail($"The assembly {relative} does not exist.");
            if (plugin.Source != PluginSource.Bundled && !IsAssembly(path))
                return plugin.Fail($"{relative} is not a .NET assembly.");
        }

        return plugin;
    }

    private static bool IsAssembly(string path)
    {
        try
        {
            AssemblyName.GetAssemblyName(path);
            return true;
        }
        catch (Exception exception) when (exception is BadImageFormatException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? ApiProblem(PluginManifest manifest)
    {
        if (manifest.RunesmithApi is { } range)
        {
            if (IsApiCompatible(range, ApiVersion, OldestSupportedApiVersion))
                return null;

            return range.Minimum < OldestSupportedApiVersion
                ? $"The plugin was built for plugin API {range.Minimum}, and this Runesmith runs plugins built for {OldestSupportedApiVersion} or later."
                : $"The plugin works with plugin API {range}, and this Runesmith has API {ApiVersion}.";
        }

        if (!Version.TryParse(manifest.ApiVersion, out var apiVersion))
            return $"\"{manifest.ApiVersion}\" is not a version; apiVersion looks like {RunesmithApi.Version.ToString(2)}.";

        return RunesmithApi.IsCompatible(apiVersion) ? null : $"The plugin was built for plugin API {apiVersion}, and this Runesmith has API {ApiVersion}.";
    }

    private static SemanticVersion ToSemanticVersion(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));
}
