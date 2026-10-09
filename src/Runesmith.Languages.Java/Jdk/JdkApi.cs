using System.Collections.Frozen;
using System.Globalization;
using System.IO.Compression;
using Runesmith.Languages.Java.ClassFiles;
using Runesmith.Languages.Java.Libraries;

namespace Runesmith.Languages.Java.Jdk;

/// <summary>The public API of one Java release, as a JDK's <c>lib/ct.sym</c> records it: exactly the classes of that release, even when the
/// JDK is newer.</summary>
/// <remarks>
/// <c>ct.sym</c> is a zip whose top-level folders are named after the releases they apply to, one character per release (<c>8</c>, <c>9</c>,
/// then <c>A</c> for 10 up to <c>P</c> for 25), such as <c>GHIJK</c>. Inside, each module's classes are stored as <c>.sig</c> class files,
/// and each module's <c>module-info.sig</c> lists the packages it exports. A JDK's <c>ct.sym</c> includes its own release, so the JDK's
/// runtime image is never needed. JDK 8 has no such data; its <c>rt.jar</c> serves release 8.
/// </remarks>
public sealed class JdkApi : ArchiveClassLibrary
{
    private readonly Lazy<FrozenDictionary<string, ModuleInfo>> modules;

    private JdkApi(JdkInstallation jdk, int release, string archivePath, LibraryIndex index)
        : base($"JDK {release}", archivePath, index)
    {
        Jdk = jdk;
        Release = release;
        modules = new(() => File.Exists(jdk.CtSymPath) && jdk.FeatureVersion > 8 ? ReadModules(jdk.CtSymPath, release) : FrozenDictionary<string, ModuleInfo>.Empty);
    }

    public JdkInstallation Jdk { get; }

    /// <summary>Gets the names of the release's modules; release 8 has none.</summary>
    public IReadOnlyCollection<string> ModuleNames => modules.Value.Keys;

    /// <summary>Gets what a module of the release declares, or null when it has no such module. The first call reads every module's
    /// declaration.</summary>
    public ModuleInfo? FindModule(string name) => modules.Value.GetValueOrDefault(name);

    /// <summary>Gets the release whose API this is.</summary>
    public int Release { get; }

    /// <summary>Opens a release's API from a JDK, using a cached index when <paramref name="cacheDirectory"/> has one.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The JDK cannot give the API of the release.</exception>
    /// <exception cref="IOException">The JDK's API data cannot be read.</exception>
    public static JdkApi Open(JdkInstallation jdk, int release, string? cacheDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(jdk);
        if (!jdk.CanServe(release))
            throw new ArgumentOutOfRangeException(nameof(release), release, $"JDK {jdk.Version} gives the API of releases 8 to {jdk.FeatureVersion}.");

        var useRuntimeJar = jdk.FeatureVersion == 8 || !File.Exists(jdk.CtSymPath);
        var archivePath = useRuntimeJar ? jdk.RuntimeJarPath : jdk.CtSymPath;
        var key = LibraryIndex.KeyFor(archivePath, "jdk|" + release.ToString(CultureInfo.InvariantCulture));
        if (LibraryIndex.Load(cacheDirectory, key) is not { } index)
        {
            index = useRuntimeJar ? BuildRuntimeJarIndex(archivePath) : BuildIndex(archivePath, release);
            index.Save(cacheDirectory, key);
        }

        return new JdkApi(jdk, release, archivePath, index);
    }

    /// <summary>Gets the character <c>ct.sym</c> names a release with: <c>8</c>, <c>9</c>, then <c>A</c> for 10 and so on.</summary>
    public static char ReleaseLetter(int release) => release <= 9 ? (char)('0' + release) : (char)('A' + release - 10);

    private static LibraryIndex BuildIndex(string ctSymPath, int release)
    {
        var letter = ReleaseLetter(release);
        using var archive = OpenArchive(ctSymPath);
        var classes = new List<(string Module, string BinaryName, string EntryName)>();
        var exports = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            var firstSlash = name.IndexOf('/');
            if (firstSlash <= 0 || !name.EndsWith(".sig", StringComparison.Ordinal) || name.AsSpan(0, firstSlash).IndexOf(letter) < 0)
                continue;

            var secondSlash = name.IndexOf('/', firstSlash + 1);
            if (secondSlash < 0)
                continue;

            var module = name[(firstSlash + 1)..secondSlash];
            var classPath = name[(secondSlash + 1)..^".sig".Length];
            if (classPath == "module-info")
            {
                exports[module] = ReadExports(entry);
                continue;
            }

            var binaryName = ClassNames.FromInternal(classPath);
            if (!ClassNames.IsAnonymousOrLocal(binaryName) && !binaryName.EndsWith("package-info", StringComparison.Ordinal))
                classes.Add((module, binaryName, name));
        }

        // Only packages a module exports to everyone are API; release 8 has no modules, so all of its classes count.
        return new LibraryIndex([.. classes
            .Where(c => !exports.TryGetValue(c.Module, out var packages) || packages.Contains(ClassNames.PackageName(c.BinaryName)))
            .Select(c => (c.BinaryName, c.EntryName))]);
    }

    private static FrozenDictionary<string, ModuleInfo> ReadModules(string ctSymPath, int release)
    {
        var letter = ReleaseLetter(release);
        using var archive = OpenArchive(ctSymPath);
        var found = new Dictionary<string, ModuleInfo>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            var firstSlash = name.IndexOf('/');
            if (firstSlash <= 0 || !name.EndsWith("/module-info.sig", StringComparison.Ordinal) || name.AsSpan(0, firstSlash).IndexOf(letter) < 0)
                continue;

            var bytes = new byte[entry.Length];
            using (var stream = entry.Open())
                stream.ReadExactly(bytes);
            if (ClassSymbol.Read(bytes).Module is { } module)
                found[module.Name] = module;
        }

        return found.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static HashSet<string> ReadExports(ZipArchiveEntry entry)
    {
        var bytes = new byte[entry.Length];
        using (var stream = entry.Open())
            stream.ReadExactly(bytes);

        var module = ClassSymbol.Read(bytes).Module;
        return module is null ? [] : [.. module.Exports.Where(e => !e.IsQualified).Select(e => e.PackageName)];
    }

    private static LibraryIndex BuildRuntimeJarIndex(string runtimeJarPath)
    {
        using var archive = OpenArchive(runtimeJarPath);
        return new LibraryIndex([.. archive.Entries
            .Where(e => e.FullName.EndsWith(".class", StringComparison.Ordinal) && (e.FullName.StartsWith("java/", StringComparison.Ordinal) || e.FullName.StartsWith("javax/", StringComparison.Ordinal)))
            .Select(e => (ClassNames.FromInternal(e.FullName[..^".class".Length]), e.FullName))
            .Where(e => !ClassNames.IsAnonymousOrLocal(e.Item1))]);
    }

    private static ZipArchive OpenArchive(string path)
    {
        try
        {
            return ZipFile.OpenRead(path);
        }
        catch (InvalidDataException exception)
        {
            throw new IOException($"{path} cannot be read: {exception.Message}", exception);
        }
    }
}
