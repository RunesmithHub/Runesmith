namespace Runesmith.Hub;

/// <summary>Where the hub client keeps its files, all in one folder that Runesmith manages, apart from the user's plugins folder.</summary>
/// <param name="Root">The hub's folder in Runesmith's data folder.</param>
public sealed record HubPaths(string Root)
{
    /// <summary>Gets the folder hub plugins are installed in, one folder per plugin id.</summary>
    public string Plugins => Path.Combine(Root, "plugins");

    /// <summary>Gets the installed state: which hub plugins are installed, at which version, and what their files are.</summary>
    public string StateFile => Path.Combine(Root, "state.json");

    /// <summary>Gets the folder installs are prepared in before they take effect.</summary>
    public string Staging => Path.Combine(Root, "staging");

    /// <summary>Gets the folder of the verified index.</summary>
    public string TrustStore => Path.Combine(Root, "index");

    /// <summary>Gets the folder of plugin icons, each named by its SHA-256.</summary>
    public string Icons => Path.Combine(Root, "icons");

    /// <summary>Gets the folder blocked plugins are moved to; each stays 30 days.</summary>
    public string Quarantine => Path.Combine(Root, "quarantine");

    /// <summary>Gets a hub plugin's folder.</summary>
    public string PluginFolder(string id) => Path.Combine(Plugins, id);
}
