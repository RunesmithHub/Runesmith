using System.Collections.Immutable;
using Runesmith.Languages.Java.ClassFiles;
using Runesmith.Languages.Java.Libraries;

namespace Runesmith.Languages.Java.Semantics;

/// <summary>The types a project declares in its sources, as a class library: an immutable snapshot, updated one file at a time.</summary>
/// <remarks>The symbols of the types are built by <see cref="SourceSymbols"/> the first time they are asked for, against the project as it is
/// then, and kept by the file's <see cref="SourceType"/>s, which later snapshots share while their file does not change.</remarks>
internal sealed class SourceLibrary : IClassLibrary
{
    private readonly ImmutableDictionary<string, SourceFile> files;
    private readonly ImmutableDictionary<string, SourceType> types;
    private readonly ImmutableDictionary<string, ImmutableArray<string>> packages;
    private readonly ImmutableDictionary<string, ImmutableArray<string>> simpleNames;
    private readonly Func<SourceType, ClassSymbol?> build;

    private SourceLibrary(ImmutableDictionary<string, SourceFile> files, ImmutableDictionary<string, SourceType> types,
        ImmutableDictionary<string, ImmutableArray<string>> packages, ImmutableDictionary<string, ImmutableArray<string>> simpleNames,
        Func<SourceType, ClassSymbol?> build, int typeSetVersion)
    {
        this.files = files;
        this.types = types;
        this.packages = packages;
        this.simpleNames = simpleNames;
        this.build = build;
        TypeSetVersion = typeSetVersion;
    }

    public static SourceLibrary Empty(Func<SourceType, ClassSymbol?> build) => new(
        ImmutableDictionary.Create<string, SourceFile>(PathComparer),
        ImmutableDictionary.Create<string, SourceType>(StringComparer.Ordinal),
        ImmutableDictionary.Create<string, ImmutableArray<string>>(StringComparer.Ordinal),
        ImmutableDictionary.Create<string, ImmutableArray<string>>(StringComparer.Ordinal),
        build,
        0);

    public static StringComparer PathComparer => OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    public string Name => "sources";

    /// <summary>Gets a number that changes whenever a type is added or removed, but not when a file changes inside its types.</summary>
    public int TypeSetVersion { get; }

    public IReadOnlyCollection<string> Packages => packages.Keys.ToList();

    public IEnumerable<string> TypeNames => types.Keys;

    public IEnumerable<SourceFile> Files => files.Values;

    public IReadOnlyList<string> GetTypes(string packageName) => packages.TryGetValue(packageName, out var names) ? names : [];

    public bool Contains(string binaryName) => types.ContainsKey(binaryName);

    public bool HasPackage(string packageName) => packages.ContainsKey(packageName);

    public ClassSymbol? FindClass(string binaryName) => types.TryGetValue(binaryName, out var type) ? build(type) : null;

    public IReadOnlyList<string> FindBySimpleName(string simpleName) => simpleNames.TryGetValue(simpleName, out var names) ? names : [];

    public SourceType? FindType(string binaryName) => types.GetValueOrDefault(binaryName);

    public SourceFile? FindFile(string path) => files.GetValueOrDefault(path);

    /// <summary>Gets the library with a file added, or replacing the file of the same path.</summary>
    public SourceLibrary WithFile(SourceFile file)
    {
        var old = files.GetValueOrDefault(file.Path);
        var sameTypes = old is not null && old.Types.Count == file.Types.Count
            && old.Types.Zip(file.Types).All(pair => pair.First.BinaryName == pair.Second.BinaryName);
        var typeBuilder = types.ToBuilder();
        if (old is not null)
        {
            foreach (var type in old.Types)
                typeBuilder.Remove(type.BinaryName);
        }

        foreach (var type in file.Types)
            typeBuilder[type.BinaryName] = type;

        if (sameTypes)
            return new SourceLibrary(files.SetItem(file.Path, file), typeBuilder.ToImmutable(), packages, simpleNames, build, TypeSetVersion);

        var (packageIndex, nameIndex) = (packages, simpleNames);
        if (old is not null)
            (packageIndex, nameIndex) = Unindex(packageIndex, nameIndex, old.Types);
        (packageIndex, nameIndex) = Index(packageIndex, nameIndex, file.Types);
        return new SourceLibrary(files.SetItem(file.Path, file), typeBuilder.ToImmutable(), packageIndex, nameIndex, build, TypeSetVersion + 1);
    }

    /// <summary>Gets the library without a file.</summary>
    public SourceLibrary WithoutFile(string path)
    {
        if (!files.TryGetValue(path, out var old))
            return this;

        var typeBuilder = types.ToBuilder();
        foreach (var type in old.Types)
            typeBuilder.Remove(type.BinaryName);
        var (packageIndex, nameIndex) = Unindex(packages, simpleNames, old.Types);
        return new SourceLibrary(files.Remove(path), typeBuilder.ToImmutable(), packageIndex, nameIndex, build, TypeSetVersion + 1);
    }

    /// <summary>Gets the library with many files added at once, as loading a project does.</summary>
    public SourceLibrary WithFiles(IEnumerable<SourceFile> added)
    {
        var fileBuilder = files.ToBuilder();
        var typeBuilder = types.ToBuilder();
        var packageLists = packages.ToDictionary(p => p.Key, p => p.Value.ToList(), StringComparer.Ordinal);
        var nameLists = simpleNames.ToDictionary(p => p.Key, p => p.Value.ToList(), StringComparer.Ordinal);
        foreach (var file in added)
        {
            if (fileBuilder.ContainsKey(file.Path))
                continue;

            fileBuilder[file.Path] = file;
            foreach (var type in file.Types)
            {
                if (!typeBuilder.TryAdd(type.BinaryName, type))
                    continue;
                Add(packageLists, ClassNames.PackageName(type.BinaryName), type.BinaryName);
                Add(nameLists, type.SimpleName, type.BinaryName);
            }
        }

        return new SourceLibrary(fileBuilder.ToImmutable(), typeBuilder.ToImmutable(),
            packageLists.ToImmutableDictionary(p => p.Key, p => p.Value.ToImmutableArray(), StringComparer.Ordinal),
            nameLists.ToImmutableDictionary(p => p.Key, p => p.Value.ToImmutableArray(), StringComparer.Ordinal),
            build, TypeSetVersion + 1);

        static void Add(Dictionary<string, List<string>> lists, string key, string value)
        {
            if (!lists.TryGetValue(key, out var list))
                lists[key] = list = [];
            list.Add(value);
        }
    }

    private static (ImmutableDictionary<string, ImmutableArray<string>>, ImmutableDictionary<string, ImmutableArray<string>>) Index(
        ImmutableDictionary<string, ImmutableArray<string>> packageIndex, ImmutableDictionary<string, ImmutableArray<string>> nameIndex,
        IEnumerable<SourceType> added)
    {
        foreach (var type in added)
        {
            var package = ClassNames.PackageName(type.BinaryName);
            packageIndex = packageIndex.SetItem(package, packageIndex.GetValueOrDefault(package, []).Add(type.BinaryName));
            nameIndex = nameIndex.SetItem(type.SimpleName, nameIndex.GetValueOrDefault(type.SimpleName, []).Add(type.BinaryName));
        }

        return (packageIndex, nameIndex);
    }

    private static (ImmutableDictionary<string, ImmutableArray<string>>, ImmutableDictionary<string, ImmutableArray<string>>) Unindex(
        ImmutableDictionary<string, ImmutableArray<string>> packageIndex, ImmutableDictionary<string, ImmutableArray<string>> nameIndex,
        IEnumerable<SourceType> removed)
    {
        foreach (var type in removed)
        {
            var package = ClassNames.PackageName(type.BinaryName);
            if (packageIndex.TryGetValue(package, out var inPackage))
            {
                inPackage = inPackage.Remove(type.BinaryName);
                packageIndex = inPackage.IsEmpty ? packageIndex.Remove(package) : packageIndex.SetItem(package, inPackage);
            }

            if (nameIndex.TryGetValue(type.SimpleName, out var named))
            {
                named = named.Remove(type.BinaryName);
                nameIndex = named.IsEmpty ? nameIndex.Remove(type.SimpleName) : nameIndex.SetItem(type.SimpleName, named);
            }
        }

        return (packageIndex, nameIndex);
    }
}
