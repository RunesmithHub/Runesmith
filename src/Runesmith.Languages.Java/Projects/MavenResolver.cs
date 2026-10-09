using System.Text;

namespace Runesmith.Languages.Java.Projects;

/// <summary>A POM with its parents merged and its properties applied, as Maven's effective model.</summary>
internal sealed class EffectivePom
{
    public required string GroupId { get; init; }

    public required string ArtifactId { get; init; }

    public required string Version { get; init; }

    public required string? Packaging { get; init; }

    public required IReadOnlyDictionary<string, string> Properties { get; init; }

    /// <summary>Gets the managed dependencies by their management key, imported BOMs included.</summary>
    public required IReadOnlyDictionary<string, PomDependency> DependencyManagement { get; init; }

    public required IReadOnlyList<PomDependency> Dependencies { get; init; }

    public required IReadOnlyList<string> Modules { get; init; }

    public required string? SourceDirectory { get; init; }

    public required string? TestSourceDirectory { get; init; }

    public required string? CompilerRelease { get; init; }

    public required bool CompilerEnablesPreview { get; init; }

    public string Key => GroupId + ":" + ArtifactId;
}

/// <summary>Builds effective POMs and resolves dependencies to JARs, transitively and with Maven's nearest-wins mediation, from repositories
/// on disk only; it never downloads and never runs a build.</summary>
internal sealed class MavenResolver(IReadOnlyList<IArtifactRepository> repositories, int maximumLibraries)
{
    private const int MaximumParentDepth = 20;

    private readonly Dictionary<string, EffectivePom?> repositoryPoms = new(StringComparer.Ordinal);

    /// <summary>Builds the effective POM of a file.</summary>
    public EffectivePom Load(string pomPath) => Build(pomPath, PomModel.Read(pomPath), 0);

    /// <summary>Resolves a project's dependencies to JAR paths, direct ones first, then transitive ones by depth.</summary>
    /// <param name="isReactorModule">Whether a dependency is another project of the same build, which is not resolved to a JAR.</param>
    /// <param name="reactorReferences">Receives the keys of the dependencies on other projects of the build.</param>
    public IReadOnlyList<string> Resolve(EffectivePom project, Func<string, bool> isReactorModule, List<string> reactorReferences)
    {
        var jars = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { project.Key };
        var queue = new Queue<(PomDependency Dependency, HashSet<string> Exclusions)>();
        foreach (var dependency in project.Dependencies)
            queue.Enqueue((dependency, [.. dependency.Exclusions]));

        while (queue.Count > 0 && jars.Count < maximumLibraries)
        {
            var (dependency, exclusions) = queue.Dequeue();
            if (!seen.Add(dependency.Key))
                continue;

            if (isReactorModule(dependency.Key))
            {
                reactorReferences.Add(dependency.Key);
                continue;
            }

            // The project's managed versions win over those of the libraries it uses, as in Maven.
            var managed = project.DependencyManagement.GetValueOrDefault(dependency.ManagementKey);
            var version = managed?.Version ?? dependency.Version;
            if (string.IsNullOrEmpty(version) || version.Contains("${", StringComparison.Ordinal))
                continue;

            if (dependency.Scope == "system")
            {
                if (dependency.SystemPath is { } systemPath && File.Exists(systemPath))
                    jars.Add(systemPath);
                continue;
            }

            var coordinate = new ArtifactCoordinate(dependency.GroupId, dependency.ArtifactId, NormalizeVersion(version), Classifier(dependency));
            if (dependency.Type is null or "jar" or "bundle" or "test-jar" or "maven-plugin" && FindJar(coordinate) is { } jar)
                jars.Add(jar);

            if (LoadFromRepository(coordinate) is not { } pom)
                continue;

            foreach (var transitive in pom.Dependencies)
            {
                if (transitive.Optional || transitive.Scope is "test" or "provided" or "system" or "import" || exclusions.Contains(transitive.Key)
                    || exclusions.Contains(transitive.GroupId + ":*"))
                {
                    continue;
                }

                var resolved = transitive.Version is null && pom.DependencyManagement.GetValueOrDefault(transitive.ManagementKey) is { } inner
                    ? transitive with { Version = inner.Version }
                    : transitive;
                queue.Enqueue((resolved, [.. exclusions, .. transitive.Exclusions]));
            }
        }

        return jars;
    }

    private EffectivePom Build(string? pomPath, PomModel model, int depth)
    {
        var raw = Merge(pomPath, model, depth);
        var properties = new Dictionary<string, string>(raw.Properties, StringComparer.Ordinal)
        {
            ["project.groupId"] = raw.GroupId,
            ["project.artifactId"] = raw.ArtifactId,
            ["project.version"] = raw.Version,
            ["pom.version"] = raw.Version,
            ["version"] = raw.Version,
            ["groupId"] = raw.GroupId,
        };
        if (raw.ParentVersion is { } parentVersion)
        {
            properties["project.parent.version"] = parentVersion;
            properties["parent.version"] = parentVersion;
            properties["project.parent.groupId"] = raw.ParentGroupId ?? "";
        }

        string? Expand(string? text) => Interpolate(text, properties);
        PomDependency ExpandDependency(PomDependency d) => d with
        {
            GroupId = Expand(d.GroupId)!,
            ArtifactId = Expand(d.ArtifactId)!,
            Version = Expand(d.Version),
            Scope = Expand(d.Scope),
            Type = Expand(d.Type),
            Classifier = Expand(d.Classifier),
            SystemPath = Expand(d.SystemPath),
        };

        var management = new Dictionary<string, PomDependency>(StringComparer.Ordinal);
        var imports = new List<PomDependency>();
        foreach (var managed in raw.DependencyManagement.Select(ExpandDependency))
        {
            if (managed.Scope == "import" && managed.Type == "pom")
                imports.Add(managed);
            else
                management[managed.ManagementKey] = managed;
        }

        // Imported BOMs only fill in what the project and its parents do not manage themselves.
        foreach (var import in imports)
        {
            if (import.Version is { } bomVersion && LoadFromRepository(new ArtifactCoordinate(import.GroupId, import.ArtifactId, bomVersion), depth + 1) is { } bom)
            {
                foreach (var (key, value) in bom.DependencyManagement)
                    management.TryAdd(key, value);
            }
        }

        var dependencies = new Dictionary<string, PomDependency>(StringComparer.Ordinal);
        foreach (var declared in raw.Dependencies.Select(ExpandDependency))
        {
            var withManaged = management.GetValueOrDefault(declared.ManagementKey) is { } m
                ? declared with { Version = declared.Version ?? m.Version, Scope = declared.Scope ?? m.Scope, Exclusions = [.. declared.Exclusions, .. m.Exclusions] }
                : declared;
            dependencies[withManaged.ManagementKey] = withManaged;
        }

        properties.TryGetValue("maven.compiler.release", out var propertyRelease);
        properties.TryGetValue("maven.compiler.source", out var propertySource);
        properties.TryGetValue("maven.compiler.target", out var propertyTarget);
        return new EffectivePom
        {
            GroupId = raw.GroupId,
            ArtifactId = raw.ArtifactId,
            Version = raw.Version,
            Packaging = model.Packaging,
            Properties = properties,
            DependencyManagement = management,
            Dependencies = [.. dependencies.Values],
            Modules = model.Modules,
            SourceDirectory = Expand(raw.SourceDirectory),
            TestSourceDirectory = Expand(raw.TestSourceDirectory),
            CompilerRelease = Expand(raw.CompilerRelease ?? propertyRelease ?? propertySource ?? propertyTarget),
            CompilerEnablesPreview = raw.CompilerEnablesPreview,
        };
    }

    // Maven merges the whole parent chain first and interpolates once, so a child's property also changes values its parents declare.
    private RawPom Merge(string? pomPath, PomModel model, int depth)
    {
        RawPom? parent = null;
        if (model.Parent is { } parentReference && depth < MaximumParentDepth)
        {
            var relative = parentReference.RelativePath ?? "../pom.xml";
            var candidate = pomPath is null || relative.Length == 0 ? null : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(pomPath)!, relative));
            if (candidate is not null && Directory.Exists(candidate))
                candidate = Path.Combine(candidate, "pom.xml");

            if (candidate is not null && File.Exists(candidate) && TryRead(candidate) is { } local
                && local.ArtifactId == parentReference.ArtifactId && (local.GroupId ?? local.Parent?.GroupId) == parentReference.GroupId)
            {
                parent = Merge(candidate, local, depth + 1);
            }
            else
            {
                parent = LoadRawFromRepository(new ArtifactCoordinate(parentReference.GroupId, parentReference.ArtifactId, parentReference.Version), depth + 1);
            }
        }

        var properties = new Dictionary<string, string>(parent?.Properties ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        foreach (var (key, value) in model.Properties)
            properties[key] = value;

        var dependencies = new Dictionary<string, PomDependency>(StringComparer.Ordinal);
        foreach (var dependency in (parent?.Dependencies ?? []).Concat(model.Dependencies))
            dependencies[dependency.ManagementKey] = dependency;

        return new RawPom
        {
            GroupId = model.GroupId ?? model.Parent?.GroupId ?? parent?.GroupId ?? "",
            ArtifactId = model.ArtifactId ?? "",
            Version = model.Version ?? model.Parent?.Version ?? parent?.Version ?? "",
            ParentGroupId = model.Parent?.GroupId,
            ParentVersion = model.Parent?.Version,
            Properties = properties,
            DependencyManagement = [.. parent?.DependencyManagement ?? [], .. model.DependencyManagement],
            Dependencies = [.. dependencies.Values],
            SourceDirectory = model.SourceDirectory ?? parent?.SourceDirectory,
            TestSourceDirectory = model.TestSourceDirectory ?? parent?.TestSourceDirectory,
            CompilerRelease = model.CompilerRelease ?? parent?.CompilerRelease,
            CompilerEnablesPreview = model.CompilerEnablesPreview || parent?.CompilerEnablesPreview == true,
        };
    }

    private RawPom? LoadRawFromRepository(ArtifactCoordinate coordinate, int depth)
    {
        foreach (var repository in repositories)
        {
            if (repository.FindPom(coordinate) is { } path && TryRead(path) is { } model)
                return Merge(path, model, depth);
        }

        return null;
    }

    /// <summary>A POM merged with its parents, before its properties are applied.</summary>
    private sealed class RawPom
    {
        public required string GroupId { get; init; }

        public required string ArtifactId { get; init; }

        public required string Version { get; init; }

        public required string? ParentGroupId { get; init; }

        public required string? ParentVersion { get; init; }

        public required Dictionary<string, string> Properties { get; init; }

        public required List<PomDependency> DependencyManagement { get; init; }

        public required List<PomDependency> Dependencies { get; init; }

        public required string? SourceDirectory { get; init; }

        public required string? TestSourceDirectory { get; init; }

        public required string? CompilerRelease { get; init; }

        public required bool CompilerEnablesPreview { get; init; }
    }

    private EffectivePom? LoadFromRepository(ArtifactCoordinate coordinate, int depth = 0)
    {
        var key = coordinate.ToString();
        if (repositoryPoms.TryGetValue(key, out var cached))
            return cached;

        repositoryPoms[key] = null;
        EffectivePom? pom = null;
        foreach (var repository in repositories)
        {
            if (repository.FindPom(coordinate) is { } path && TryRead(path) is { } model)
            {
                pom = Build(path, model, depth);
                break;
            }
        }

        repositoryPoms[key] = pom;
        return pom;
    }

    private string? FindJar(ArtifactCoordinate coordinate)
    {
        foreach (var repository in repositories)
        {
            if (repository.FindJar(coordinate) is { } jar)
                return jar;
        }

        return null;
    }

    private static PomModel? TryRead(string path)
    {
        try
        {
            return PomModel.Read(path);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? Classifier(PomDependency dependency) =>
        dependency.Type == "test-jar" ? "tests" : dependency.Classifier;

    // A version range such as [1.2,2.0) cannot be resolved without metadata; its lower bound is the best guess on disk.
    private static string NormalizeVersion(string version)
    {
        if (version.Length > 0 && version[0] is '[' or '(')
        {
            var inner = version.Trim('[', ']', '(', ')');
            var comma = inner.IndexOf(',');
            return (comma < 0 ? inner : inner[..comma]).Trim();
        }

        return version;
    }

    private static string? Interpolate(string? text, Dictionary<string, string> properties)
    {
        if (text is null || !text.Contains("${", StringComparison.Ordinal))
            return text;

        for (var pass = 0; pass < 10 && text.Contains("${", StringComparison.Ordinal); pass++)
        {
            var result = new StringBuilder();
            var position = 0;
            var changed = false;
            while (position < text.Length)
            {
                var start = text.IndexOf("${", position, StringComparison.Ordinal);
                var end = start < 0 ? -1 : text.IndexOf('}', start);
                if (start < 0 || end < 0)
                {
                    result.Append(text, position, text.Length - position);
                    break;
                }

                result.Append(text, position, start - position);
                var name = text[(start + 2)..end];
                if (properties.TryGetValue(name, out var value))
                {
                    result.Append(value);
                    changed = true;
                }
                else
                {
                    result.Append(text, start, end - start + 1);
                }

                position = end + 1;
            }

            text = result.ToString();
            if (!changed)
                break;
        }

        return text;
    }
}
