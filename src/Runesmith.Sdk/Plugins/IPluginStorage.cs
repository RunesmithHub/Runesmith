namespace Runesmith.Sdk.Plugins;

/// <summary>Gives each plugin a folder of its own for the files it keeps, such as caches and downloads.</summary>
public interface IPluginStorage
{
    /// <summary>Gets the calling plugin's folder, named after its id in Runesmith's data folder, and creates it when it does not exist.</summary>
    /// <exception cref="InvalidOperationException">The caller is not a plugin.</exception>
    string GetFolder();
}
