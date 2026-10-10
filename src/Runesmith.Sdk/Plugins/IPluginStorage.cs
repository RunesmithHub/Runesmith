namespace Runesmith.Sdk.Plugins;

/// <summary>Gives each plugin a folder of its own for the files it keeps, such as caches and downloads.</summary>
public interface IPluginStorage
{
    /// <summary>Gets the calling plugin's folder, named after its id in Runesmith's data folder, and creates it when it does not exist; a local
    /// copy of a plugin that comes with Runesmith or from the hub gets a folder apart from that plugin's.</summary>
    /// <exception cref="InvalidOperationException">The caller is not a plugin.</exception>
    string GetFolder();
}
