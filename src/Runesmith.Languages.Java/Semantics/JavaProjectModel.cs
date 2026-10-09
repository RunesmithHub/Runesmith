using System.Collections.Concurrent;
using Runesmith.Languages.Java.ClassFiles;
using Runesmith.Languages.Java.Jdk;
using Runesmith.Languages.Java.Libraries;
using Runesmith.Languages.Java.Syntax;

namespace Runesmith.Languages.Java.Semantics;

/// <summary>A Java project as the analyzer keeps it: its release, its libraries and its sources, which change one file at a time.</summary>
/// <remarks>Every change makes a new <see cref="ProjectSnapshot"/>; requests work on the snapshot they start with.</remarks>
internal sealed class JavaProjectModel
{
    private readonly Lock gate = new();
    private readonly Lazy<PackageIndex> libraryPackages;
    private readonly ConcurrentDictionary<string, bool> libraryFullyKnown = new(StringComparer.Ordinal);
    private SourceLibrary sources;
    private ProjectSnapshot? snapshot;

    /// <param name="sourceRoots">The folders whose files belong to the project; none for the project of files outside every project.</param>
    /// <param name="jdk">The JDK's API for the release, or null when no JDK was found.</param>
    /// <param name="libraries">The project's JARs, in class path order.</param>
    public JavaProjectModel(string name, IReadOnlyList<string> sourceRoots, JavaVersion version, JdkApi? jdk, IReadOnlyList<IClassLibrary> libraries,
        bool mayMissTypes = false)
    {
        Name = name;
        SourceRoots = sourceRoots;
        Version = version;
        ParseOptions = new JavaParseOptions { Version = version };
        Jdk = jdk;
        Libraries = libraries;
        MayMissTypes = mayMissTypes;
        sources = SourceLibrary.Empty(Build);
        libraryPackages = new(() => PackageIndex.Create([.. (jdk is null ? libraries : libraries.Prepend(jdk)).SelectMany(l => l.Packages)]));
    }

    public string Name { get; }

    public IReadOnlyList<string> SourceRoots { get; }

    public JavaVersion Version { get; }

    public JavaParseOptions ParseOptions { get; }

    public JdkApi? Jdk { get; }

    public IReadOnlyList<IClassLibrary> Libraries { get; }

    /// <summary>Gets whether types or members may exist that the analyzer cannot see, such as those annotation processors like Lombok
    /// generate, or those of a project of the same build it could not find, so missing ones are not errors it can be sure of.</summary>
    public bool MayMissTypes { get; private set; }

    /// <summary>Gets the other projects of the same build whose sources this one compiles against, directly or through others.</summary>
    public IReadOnlyList<JavaProjectModel> References { get; private set; } = [];

    /// <summary>Gets which types of the JDK and the JARs have only known supertypes, as learned so far.</summary>
    public ConcurrentDictionary<string, bool> LibraryFullyKnown => libraryFullyKnown;

    /// <summary>Gets the packages of the JDK and the JARs.</summary>
    public PackageIndex LibraryPackages => libraryPackages.Value;

    /// <summary>Gets the project as it is now.</summary>
    public ProjectSnapshot Snapshot
    {
        get
        {
            lock (gate)
                return snapshot ??= new ProjectSnapshot(this, sources);
        }
    }

    /// <summary>Gets whether a file lies under one of the project's source roots.</summary>
    public bool Owns(string path) => SourceRoots.Any(root => IsUnder(path, root));

    /// <summary>Gets the length of the longest source root a file lies under, to choose the most specific project, or -1.</summary>
    public int Specificity(string path) => SourceRoots.Where(root => IsUnder(path, root)).Select(root => root.Length).DefaultIfEmpty(-1).Max();

    public void SetFile(SourceFile file)
    {
        lock (gate)
        {
            sources = sources.WithFile(file);
            snapshot = null;
        }
    }

    public void SetFiles(IEnumerable<SourceFile> files)
    {
        lock (gate)
        {
            sources = sources.WithFiles(files);
            snapshot = null;
        }
    }

    /// <summary>Sets the projects this one depends on; when some could not be found, their types are among those the analyzer cannot see.</summary>
    public void SetReferences(IReadOnlyList<JavaProjectModel> references, bool allFound)
    {
        lock (gate)
        {
            References = references;
            MayMissTypes |= !allFound;
            snapshot = null;
        }
    }

    public void RemoveFile(string path)
    {
        lock (gate)
        {
            sources = sources.WithoutFile(path);
            snapshot = null;
        }
    }

    private ClassSymbol? Build(SourceType type) => type.GetSymbol(t => SourceSymbols.Build(Snapshot.ModelFor(t.File), t));

    private static bool IsUnder(string path, string root)
    {
        // An editor path may use the other separator on Windows; the full forms use the system's.
        path = Path.GetFullPath(path);
        root = Path.GetFullPath(root);
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return path.StartsWith(root, comparison) && (path.Length == root.Length || path[root.Length] == Path.DirectorySeparatorChar || root.EndsWith(Path.DirectorySeparatorChar));
    }
}

/// <summary>A project at one moment: its class path of sources, the JDK and libraries, and what the analyzer learned about it so far.</summary>
internal sealed class ProjectSnapshot
{
    private readonly ConcurrentDictionary<string, SemanticModel> models = new(SourceLibrary.PathComparer);
    private readonly ConcurrentDictionary<string, bool> fullyKnown = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>?> modulePackages = new(StringComparer.Ordinal);
    private readonly Lazy<PackageIndex> sourcePackages;

    public ProjectSnapshot(JavaProjectModel model, SourceLibrary sources)
    {
        Model = model;
        Sources = sources;
        IClassLibrary[] referenced = [.. model.References.Select(r => new ReferencedSources(r))];
        ClassPath = new ClassPath(model.Jdk is null ? [sources, .. referenced, .. model.Libraries] : [sources, .. referenced, model.Jdk, .. model.Libraries]);
        sourcePackages = new(() => PackageIndex.Create([.. sources.Packages]));
    }

    public JavaProjectModel Model { get; }

    public SourceLibrary Sources { get; }

    public ClassPath ClassPath { get; }

    public JavaVersion Version => Model.Version;

    /// <summary>Gets whether the JDK's API is on the class path; without it, nothing can be called unknown.</summary>
    public bool HasJdk => Model.Jdk is not null;

    /// <summary>Gets the packages of this project's sources.</summary>
    public PackageIndex SourcePackages => sourcePackages.Value;

    public bool Contains(string binaryName) => ClassPath.Contains(binaryName);

    public ClassSymbol? FindClass(string binaryName) => ClassPath.FindClass(binaryName);

    /// <summary>Finds a type declared in the sources of this project or of a project it depends on.</summary>
    public SourceType? FindSourceType(string binaryName) =>
        Sources.FindType(binaryName) ?? Model.References.Select(r => r.Snapshot.Sources.FindType(binaryName)).FirstOrDefault(t => t is not null);

    /// <summary>Gets whether a name is a package, or the start of one, such as <c>java</c>.</summary>
    public bool IsPackage(string name) => Model.LibraryPackages.IsPackageOrPrefix(name) || SourcePackages.IsPackageOrPrefix(name)
        || Model.References.Any(r => r.Snapshot.SourcePackages.IsPackageOrPrefix(name));

    /// <summary>Gets whether a package holds types.</summary>
    public bool HasTypesIn(string packageName) => Model.LibraryPackages.Has(packageName) || Sources.HasPackage(packageName)
        || Model.References.Any(r => r.Snapshot.Sources.HasPackage(packageName));

    /// <summary>Gets the packages one level below a package, or the top-level ones for an empty name.</summary>
    public IEnumerable<string> SubPackages(string prefix) => Model.LibraryPackages.Children(prefix).Concat(SourcePackages.Children(prefix))
        .Concat(Model.References.SelectMany(r => r.Snapshot.SourcePackages.Children(prefix))).Distinct(StringComparer.Ordinal);

    /// <summary>Gets the names of the modules a module import can name: the JDK's and those declared in source.</summary>
    public IEnumerable<string> ModuleNames =>
        (Model.Jdk?.ModuleNames ?? []).Concat(Sources.Files.Where(f => f.Module is not null).Select(f => f.Module!.Name)).Distinct(StringComparer.Ordinal);

    /// <summary>Gets the packages <c>import module</c> imports: those the module exports to everyone, and those of the modules it requires
    /// transitively; null when the module is not known.</summary>
    public IReadOnlyList<string>? PackagesOfModule(string moduleName) => modulePackages.GetOrAdd(moduleName, name =>
    {
        if (FindModule(name) is null)
            return null;

        var packages = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([name]);
        while (pending.TryPop(out var next))
        {
            if (!visited.Add(next) || FindModule(next) is not { } module)
                continue;
            packages.UnionWith(module.Exports.Where(e => !e.IsQualified).Select(e => e.PackageName));
            foreach (var required in module.TransitiveRequires)
                pending.Push(required);
        }

        return [.. packages];
    });

    /// <summary>Gets the semantic model of a file of this snapshot, made once per file.</summary>
    public SemanticModel ModelFor(SourceFile file)
    {
        if (models.TryGetValue(file.Path, out var model) && model.File == file)
            return model;

        model = new SemanticModel(this, file);
        models[file.Path] = model;
        return model;
    }

    /// <summary>Gets whether a type and all of its supertypes are known, and its source cannot hide generated members, so a member it lacks
    /// is certainly missing.</summary>
    public bool IsFullyKnown(string binaryName)
    {
        if (fullyKnown.TryGetValue(binaryName, out var known) || Model.LibraryFullyKnown.TryGetValue(binaryName, out known))
            return known;

        var (result, onlyLibraries) = ComputeFullyKnown(binaryName);
        if (onlyLibraries)
            Model.LibraryFullyKnown[binaryName] = result;
        else
            fullyKnown[binaryName] = result;
        return result;
    }

    // An answer that involves no source type holds for every snapshot of the project, so the project keeps it.
    private (bool Known, bool OnlyLibraries) ComputeFullyKnown(string binaryName)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([binaryName]);
        var onlyLibraries = true;
        while (pending.TryPop(out var name))
        {
            if (!visited.Add(name))
                continue;
            var source = FindSourceType(name);
            onlyLibraries &= source is null;
            if (FindClass(name) is not { } symbol || source is { MayHaveHiddenMembers: true })
                return (false, onlyLibraries);
            if (symbol.SuperClass is { } superClass)
                pending.Push(superClass.BinaryName);
            foreach (var implemented in symbol.Interfaces)
                pending.Push(implemented.BinaryName);
        }

        return (true, onlyLibraries);
    }

    private ModuleInfo? FindModule(string name) =>
        Model.Jdk?.FindModule(name) ?? Sources.Files.FirstOrDefault(f => f.Module?.Name == name)?.Module;
}

/// <summary>A set of package names that also answers which names start packages, such as <c>java</c> for <c>java.util</c>.</summary>
internal sealed class PackageIndex
{
    private readonly HashSet<string> packages;
    private readonly Dictionary<string, List<string>> children = new(StringComparer.Ordinal);

    private PackageIndex(HashSet<string> packages)
    {
        this.packages = packages;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var package in packages)
        {
            var name = package;
            while (name.Length > 0 && seen.Add(name))
            {
                var dot = name.LastIndexOf('.');
                var parent = dot < 0 ? "" : name[..dot];
                if (!children.TryGetValue(parent, out var list))
                    children[parent] = list = [];
                list.Add(name);
                name = parent;
            }
        }
    }

    public static PackageIndex Create(IEnumerable<string> packages) => new(packages.Where(p => p.Length > 0).ToHashSet(StringComparer.Ordinal));

    public bool Has(string packageName) => packages.Contains(packageName);

    public bool IsPackageOrPrefix(string name) => packages.Contains(name) || children.ContainsKey(name);

    public IEnumerable<string> Children(string prefix) => children.TryGetValue(prefix, out var list) ? list : [];
}
