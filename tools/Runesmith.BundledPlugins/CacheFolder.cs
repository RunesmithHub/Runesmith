namespace Runesmith.BundledPlugins;

/// <summary>
/// The build's folder for bundled plugins: the trust store, the downloaded packages named by their SHA-256, and <c>plugins/&lt;id&gt;</c> with
/// each plugin unpacked. A plugin's stamp, written last, holds the hash of the package its folder came from.
/// </summary>
internal sealed class CacheFolder(string path)
{
    private string Plugins => Path.Combine(path, "plugins");

    private string Stamps => Path.Combine(path, "stamps");

    public string TrustStore => Path.Combine(path, "index");

    public string PackagePath(string sha256) => Path.Combine(Directory.CreateDirectory(Path.Combine(path, "packages")).FullName, sha256 + ".rsplugin");

    public bool HasUnpacked(Pin pin)
    {
        var stamp = Path.Combine(Stamps, pin.Id);
        return Directory.Exists(Path.Combine(Plugins, pin.Id)) && File.Exists(stamp)
            && string.Equals(File.ReadAllText(stamp), pin.Sha256, StringComparison.Ordinal);
    }

    /// <summary>Unpacks into a staging folder with <paramref name="unpack"/>, which throws if what it unpacked is wrong, then puts it in place.</summary>
    public void Unpack(Pin pin, Action<string> unpack)
    {
        Remove(pin.Id);
        var staging = Path.Combine(path, "staging", pin.Id);
        DeleteFolder(staging);
        try
        {
            unpack(staging);
            Directory.CreateDirectory(Plugins);
            Directory.Move(staging, Path.Combine(Plugins, pin.Id));
        }
        finally
        {
            DeleteFolder(staging);
        }

        File.WriteAllText(Path.Combine(Directory.CreateDirectory(Stamps).FullName, pin.Id), pin.Sha256);
    }

    public void Remove(string id)
    {
        if (File.Exists(Path.Combine(Stamps, id)))
            File.Delete(Path.Combine(Stamps, id));
        DeleteFolder(Path.Combine(Plugins, id));
    }

    public void RemoveAllBut(IEnumerable<string> ids)
    {
        if (!Directory.Exists(Plugins))
            return;
        var keep = ids.ToHashSet(StringComparer.Ordinal);
        foreach (var folder in Directory.EnumerateDirectories(Plugins).Where(folder => !keep.Contains(Path.GetFileName(folder))))
            Remove(Path.GetFileName(folder));
    }

    private static void DeleteFolder(string folder)
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }
}
