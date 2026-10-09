namespace Runesmith.Hub;

/// <summary>Where the hub client keeps its files.</summary>
/// <param name="UserPlugins">The user's plugins folder, where hub plugins are installed, one folder per plugin id.</param>
/// <param name="Data">Runesmith's data folder, for the trusted index, the icon cache and the quarantine.</param>
public sealed record HubPaths(string UserPlugins, string Data)
{
    /// <summary>Gets the installed state: which hub plugins are installed, at which version, and what their files are.</summary>
    public string StateFile => Path.Combine(UserPlugins, "hub-state.json");

    /// <summary>Gets the folder installs are prepared in before they take effect; discovery skips it, as it has no manifest.</summary>
    public string Staging => Path.Combine(UserPlugins, ".hub-staging");

    /// <summary>Gets the folder of the verified index.</summary>
    public string TrustStore => Path.Combine(Data, "hub", "index");

    /// <summary>Gets the folder of plugin icons, each named by its SHA-256.</summary>
    public string Icons => Path.Combine(Data, "hub", "icons");

    /// <summary>Gets the folder blocked plugins are moved to; each stays 30 days.</summary>
    public string Quarantine => Path.Combine(Data, "plugins-quarantine");

    /// <summary>Gets a plugin's folder in the user's plugins folder.</summary>
    public string PluginFolder(string id) => Path.Combine(UserPlugins, id);
}
