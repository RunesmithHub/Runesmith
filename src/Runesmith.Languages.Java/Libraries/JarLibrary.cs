using System.Globalization;
using System.IO.Compression;
using Runesmith.Languages.Java.ClassFiles;

namespace Runesmith.Languages.Java.Libraries;

/// <summary>The classes of a JAR, as a project targeting a Java release sees them: in a multi-release JAR, the newest version of each class
/// for that release.</summary>
public sealed class JarLibrary : ArchiveClassLibrary
{
    private const string VersionsFolder = "META-INF/versions/";

    private JarLibrary(string path, LibraryIndex index)
        : base(System.IO.Path.GetFileName(path), path, index)
    {
        Path = path;
    }

    /// <summary>Gets the JAR's path.</summary>
    public string Path { get; }

    /// <summary>Opens a JAR, using a cached index when <paramref name="cacheDirectory"/> has one for this file and release.</summary>
    /// <exception cref="IOException">The file cannot be read or is not a zip archive.</exception>
    public static JarLibrary Open(string path, int release, string? cacheDirectory = null)
    {
        var key = LibraryIndex.KeyFor(path, "jar|" + release.ToString(CultureInfo.InvariantCulture));
        if (LibraryIndex.Load(cacheDirectory, key) is not { } index)
        {
            index = BuildIndex(path, release);
            index.Save(cacheDirectory, key);
        }

        return new JarLibrary(path, index);
    }

    private static LibraryIndex BuildIndex(string path, int release)
    {
        using var archive = OpenArchive(path);
        var multiRelease = IsMultiRelease(archive);
        var chosen = new Dictionary<string, (string EntryName, int Version)>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (!name.EndsWith(".class", StringComparison.Ordinal))
                continue;

            var version = 0;
            var classPath = name;
            if (name.StartsWith(VersionsFolder, StringComparison.Ordinal))
            {
                var slash = name.IndexOf('/', VersionsFolder.Length);
                if (!multiRelease || slash < 0 || !int.TryParse(name.AsSpan(VersionsFolder.Length, slash - VersionsFolder.Length), CultureInfo.InvariantCulture, out version)
                    || version > release)
                {
                    continue;
                }

                classPath = name[(slash + 1)..];
            }
            else if (name.StartsWith("META-INF/", StringComparison.Ordinal))
            {
                continue;
            }

            var binaryName = ClassNames.FromInternal(classPath[..^".class".Length]);
            if (binaryName.EndsWith("module-info", StringComparison.Ordinal) || binaryName.EndsWith("package-info", StringComparison.Ordinal)
                || ClassNames.IsAnonymousOrLocal(binaryName))
            {
                continue;
            }

            if (!chosen.TryGetValue(binaryName, out var existing) || existing.Version < version)
                chosen[binaryName] = (name, version);
        }

        return new LibraryIndex([.. chosen.Select(pair => (pair.Key, pair.Value.EntryName))]);
    }

    private static ZipArchive OpenArchive(string path)
    {
        try
        {
            return ZipFile.OpenRead(path);
        }
        catch (InvalidDataException exception)
        {
            throw new IOException($"{path} is not a JAR: {exception.Message}", exception);
        }
    }

    private static bool IsMultiRelease(ZipArchive archive)
    {
        if (archive.GetEntry("META-INF/MANIFEST.MF") is not { } manifest)
            return false;

        using var reader = new StreamReader(manifest.Open());
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith("Multi-Release:", StringComparison.OrdinalIgnoreCase))
                return line["Multi-Release:".Length..].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
