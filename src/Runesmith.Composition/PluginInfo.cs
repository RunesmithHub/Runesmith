namespace Runesmith.Composition;

/// <summary>Whether a plugin runs, and if not, why.</summary>
public enum PluginState
{
    Loaded,

    /// <summary>The user turned the plugin off.</summary>
    Disabled,

    /// <summary>The plugin could not be loaded; <see cref="PluginInfo.Error"/> says why.</summary>
    Failed,

    /// <summary>Another copy of the plugin runs instead, such as a newer version from the hub in place of the one Runesmith ships.</summary>
    Replaced,

    /// <summary>A copy in the user's plugins folder of a plugin Runesmith ships or the hub installed, which does not load until the user lets
    /// local copies replace plugins; <see cref="PluginInfo.Error"/> says how.</summary>
    Refused,
}

/// <summary>Where a plugin came from.</summary>
public enum PluginSource
{
    /// <summary>It ships with Runesmith.</summary>
    Bundled,

    /// <summary>The user put it in their plugins folder.</summary>
    Local,

    /// <summary>The plugin hub installed it in its own folder in Runesmith's data folder.</summary>
    Hub,
}

/// <summary>A plugin that was found, and what became of it.</summary>
/// <param name="Directory">The folder that holds the plugin's manifest.</param>
public sealed record PluginInfo(PluginManifest Manifest, string Directory, PluginSource Source)
{
    /// <summary>Gets whether the plugin ships with Runesmith.</summary>
    public bool IsBuiltIn => Source == PluginSource.Bundled;

    /// <summary>Gets whether this is a copy in the user's plugins folder of a plugin Runesmith ships or the hub installed; such a copy keeps
    /// its secrets and storage apart from that plugin's.</summary>
    public bool IsLocalCopy { get; init; }

    public PluginState State { get; init; } = PluginState.Loaded;

    /// <summary>Gets why the plugin failed, for the user.</summary>
    public string? Error { get; init; }

    /// <summary>Gets the full path of the plugin's implementation assembly.</summary>
    public string AssemblyPath => Path.GetFullPath(Path.Combine(Directory, Manifest.Assembly));

    /// <summary>Gets the full path of the plugin's contracts assembly, or null for a manifest in the older format.</summary>
    public string? ContractsPath => Manifest.ContractsAssembly is { } contracts ? Path.GetFullPath(Path.Combine(Directory, contracts)) : null;

    internal PluginInfo Fail(string error) => this with { State = PluginState.Failed, Error = error };
}
