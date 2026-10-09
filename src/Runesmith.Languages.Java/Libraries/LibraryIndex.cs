using System.Security.Cryptography;
using System.Text;

namespace Runesmith.Languages.Java.Libraries;

/// <summary>The types of a library and where their class files are: the part of a library that is slow to build and quick to load.</summary>
/// <param name="Entries">Each type's binary name and the name of its class file's entry in the archive.</param>
internal sealed record LibraryIndex(IReadOnlyList<(string BinaryName, string EntryName)> Entries)
{
    private const string Magic = "RSJIDX";
    private const int FormatVersion = 1;

    /// <summary>Gets a cache key for a file: its path, size and last write time, plus anything else that changes the index.</summary>
    public static string KeyFor(string path, string extra)
    {
        var file = new FileInfo(path);
        return $"{Path.GetFullPath(path)}|{file.Length}|{file.LastWriteTimeUtc.Ticks}|{extra}";
    }

    /// <summary>Reads a cached index, or returns null when there is none for the key or it cannot be read.</summary>
    public static LibraryIndex? Load(string? cacheDirectory, string key)
    {
        if (cacheDirectory is null || !File.Exists(PathFor(cacheDirectory, key)))
            return null;

        try
        {
            using var reader = new BinaryReader(File.OpenRead(PathFor(cacheDirectory, key)), Encoding.UTF8);
            if (reader.ReadString() != Magic || reader.ReadInt32() != FormatVersion || reader.ReadString() != key)
                return null;

            var count = reader.ReadInt32();
            var entries = new (string, string)[count];
            for (var i = 0; i < count; i++)
                entries[i] = (reader.ReadString(), reader.ReadString());
            return new LibraryIndex(entries);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or EndOfStreamException)
        {
            return null;
        }
    }

    /// <summary>Writes the index to the cache; a failure only means the next load builds it again.</summary>
    public void Save(string? cacheDirectory, string key)
    {
        if (cacheDirectory is null)
            return;

        try
        {
            Directory.CreateDirectory(cacheDirectory);
            var path = PathFor(cacheDirectory, key);
            var temporary = path + "." + Environment.ProcessId + ".tmp";
            using (var writer = new BinaryWriter(File.Create(temporary), Encoding.UTF8))
            {
                writer.Write(Magic);
                writer.Write(FormatVersion);
                writer.Write(key);
                writer.Write(Entries.Count);
                foreach (var (binaryName, entryName) in Entries)
                {
                    writer.Write(binaryName);
                    writer.Write(entryName);
                }
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string PathFor(string cacheDirectory, string key) =>
        Path.Combine(cacheDirectory, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..24] + ".idx");
}
