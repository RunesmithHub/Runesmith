using System.Reflection;

namespace Runesmith.Composition;

/// <summary>What to compose.</summary>
/// <param name="HostAssemblies">Runesmith's own assemblies that export parts, such as the shell.</param>
/// <param name="PluginFolders">The folders to look for plugins in, and where the plugins in each come from.</param>
/// <param name="CacheDirectory">Where the composition is cached between runs, or null to compose from scratch every time.</param>
public sealed record CompositionOptions(
    IReadOnlyList<Assembly> HostAssemblies,
    IReadOnlyList<(string Path, PluginSource Source)> PluginFolders,
    string? CacheDirectory)
{
    /// <summary>Gets the ids of plugins the user turned off.</summary>
    public IReadOnlySet<string> DisabledPlugins { get; init; } = new HashSet<string>();

    /// <summary>Gets the ids of the plugins in the user's folder that the plugin hub installed.</summary>
    public IReadOnlySet<string> HubPlugins { get; init; } = new HashSet<string>();

    /// <summary>Gets the plugins Runesmith keeps from loading, with why, such as a plugin whose files changed since it was installed.</summary>
    public IReadOnlyDictionary<string, string> WithheldPlugins { get; init; } = new Dictionary<string, string>();
}
