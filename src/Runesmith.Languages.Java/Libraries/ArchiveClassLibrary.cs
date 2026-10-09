using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.IO.Compression;
using Runesmith.Languages.Java.ClassFiles;

namespace Runesmith.Languages.Java.Libraries;

/// <summary>A class library whose class files are entries of a zip archive, found through an index.</summary>
/// <remarks>The archive is opened only when the first class file is read, so a library loaded from a cached index costs one small file read.
/// Reading entries is serialized, because a zip archive is not safe to read from several threads.</remarks>
public abstract class ArchiveClassLibrary : IClassLibrary, IDisposable
{
    private readonly string archivePath;
    private readonly FrozenDictionary<string, string> entries;
    private readonly Lazy<FrozenDictionary<string, string[]>> packages;
    private readonly Lazy<FrozenDictionary<string, string[]>> simpleNames;
    private readonly ConcurrentDictionary<string, ClassSymbol?> symbols = new(StringComparer.Ordinal);
    private readonly Lock archiveGate = new();
    private ZipArchive? archive;

    private protected ArchiveClassLibrary(string name, string archivePath, LibraryIndex index)
    {
        Name = name;
        this.archivePath = archivePath;
        entries = index.Entries.ToFrozenDictionary(e => e.BinaryName, e => e.EntryName, StringComparer.Ordinal);
        packages = new(() => entries.Keys.GroupBy(ClassNames.PackageName).ToFrozenDictionary(g => g.Key, g => g.Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal));
        simpleNames = new(() => entries.Keys.GroupBy(ClassNames.SimpleName).ToFrozenDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal));
    }

    public string Name { get; }

    public IReadOnlyCollection<string> Packages => packages.Value.Keys;

    public IEnumerable<string> TypeNames => entries.Keys;

    public IReadOnlyList<string> GetTypes(string packageName) => packages.Value.TryGetValue(packageName, out var types) ? types : [];

    public bool Contains(string binaryName) => entries.ContainsKey(binaryName);

    public IReadOnlyList<string> FindBySimpleName(string simpleName) => simpleNames.Value.TryGetValue(simpleName, out var names) ? names : [];

    public ClassSymbol? FindClass(string binaryName) =>
        entries.TryGetValue(binaryName, out var entryName) ? symbols.GetOrAdd(binaryName, _ => Decode(entryName)) : null;

    public void Dispose()
    {
        lock (archiveGate)
        {
            archive?.Dispose();
            archive = null;
        }

        GC.SuppressFinalize(this);
    }

    private ClassSymbol? Decode(string entryName)
    {
        byte[] bytes;
        lock (archiveGate)
        {
            archive ??= ZipFile.OpenRead(archivePath);
            if (archive.GetEntry(entryName) is not { } entry)
                return null;

            bytes = new byte[entry.Length];
            using var stream = entry.Open();
            stream.ReadExactly(bytes);
        }

        try
        {
            return ClassSymbol.Read(bytes);
        }
        catch (ClassFileFormatException)
        {
            return null;
        }
    }
}
