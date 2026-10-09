using System.Xml;
using System.Xml.Linq;

namespace Runesmith.Languages.Java.Projects;

/// <summary>A dependency as a POM declares it, before properties and managed versions are applied.</summary>
internal sealed record PomDependency(
    string GroupId,
    string ArtifactId,
    string? Version,
    string? Scope,
    string? Type,
    string? Classifier,
    bool Optional,
    IReadOnlyList<string> Exclusions,
    string? SystemPath)
{
    public string Key => GroupId + ":" + ArtifactId;

    /// <summary>Gets the key dependency management matches on.</summary>
    public string ManagementKey => $"{GroupId}:{ArtifactId}:{Type ?? "jar"}:{Classifier}";
}

/// <summary>What Runesmith reads from one <c>pom.xml</c>, without its parents.</summary>
internal sealed class PomModel
{
    public string? GroupId { get; init; }

    public string? ArtifactId { get; init; }

    public string? Version { get; init; }

    public string? Packaging { get; init; }

    public (string GroupId, string ArtifactId, string Version, string? RelativePath)? Parent { get; init; }

    public Dictionary<string, string> Properties { get; init; } = new(StringComparer.Ordinal);

    public List<PomDependency> DependencyManagement { get; init; } = [];

    public List<PomDependency> Dependencies { get; init; } = [];

    public List<string> Modules { get; init; } = [];

    public string? SourceDirectory { get; init; }

    public string? TestSourceDirectory { get; init; }

    /// <summary>Gets the compiler plugin's <c>release</c>, <c>source</c> or <c>target</c>, in that order of preference.</summary>
    public string? CompilerRelease { get; init; }

    public bool CompilerEnablesPreview { get; init; }

    /// <summary>Reads a POM file.</summary>
    /// <exception cref="InvalidDataException">The file is not XML.</exception>
    public static PomModel Read(string path)
    {
        XElement root;
        try
        {
            root = XDocument.Load(path).Root ?? throw new InvalidDataException($"{path} is empty.");
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException($"{path} is not valid XML: {exception.Message}", exception);
        }

        var parent = Child(root, "parent");
        var build = Child(root, "build");
        var compiler = CompilerPlugin(build);
        var configuration = Child(compiler, "configuration");
        var compilerArgs = Children(Child(configuration, "compilerArgs"), "arg").Select(e => e.Value.Trim())
            .Append(Text(configuration, "compilerArgument") ?? "");

        return new PomModel
        {
            GroupId = Text(root, "groupId"),
            ArtifactId = Text(root, "artifactId"),
            Version = Text(root, "version"),
            Packaging = Text(root, "packaging"),
            Parent = parent is null || Text(parent, "groupId") is not { } parentGroup || Text(parent, "artifactId") is not { } parentArtifact
                ? null
                : (parentGroup, parentArtifact, Text(parent, "version") ?? "", Text(parent, "relativePath")),
            Properties = Child(root, "properties")?.Elements().ToDictionary(e => e.Name.LocalName, e => e.Value.Trim(), StringComparer.Ordinal)
                ?? new Dictionary<string, string>(StringComparer.Ordinal),
            DependencyManagement = ReadDependencies(Child(Child(root, "dependencyManagement"), "dependencies")),
            Dependencies = ReadDependencies(Child(root, "dependencies")),
            Modules = [.. Children(Child(root, "modules"), "module").Select(e => e.Value.Trim())],
            SourceDirectory = Text(build, "sourceDirectory"),
            TestSourceDirectory = Text(build, "testSourceDirectory"),
            CompilerRelease = Text(configuration, "release") ?? Text(configuration, "source") ?? Text(configuration, "target"),
            CompilerEnablesPreview = compilerArgs.Contains("--enable-preview", StringComparer.Ordinal)
                || string.Equals(Text(configuration, "enablePreview"), "true", StringComparison.OrdinalIgnoreCase),
        };
    }

    private static XElement? CompilerPlugin(XElement? build)
    {
        var plugins = Children(Child(build, "plugins"), "plugin").Concat(Children(Child(Child(build, "pluginManagement"), "plugins"), "plugin"));
        return plugins.FirstOrDefault(p => Text(p, "artifactId") == "maven-compiler-plugin");
    }

    private static List<PomDependency> ReadDependencies(XElement? element) =>
    [
        .. Children(element, "dependency")
            .Where(d => Text(d, "groupId") is not null && Text(d, "artifactId") is not null)
            .Select(d => new PomDependency(
                Text(d, "groupId")!,
                Text(d, "artifactId")!,
                Text(d, "version"),
                Text(d, "scope"),
                Text(d, "type"),
                Text(d, "classifier"),
                string.Equals(Text(d, "optional"), "true", StringComparison.OrdinalIgnoreCase),
                [.. Children(Child(d, "exclusions"), "exclusion").Select(e => $"{Text(e, "groupId")}:{Text(e, "artifactId")}")],
                Text(d, "systemPath"))),
    ];

    private static XElement? Child(XElement? element, string name) => element?.Elements().FirstOrDefault(e => e.Name.LocalName == name);

    private static IEnumerable<XElement> Children(XElement? element, string name) =>
        element?.Elements().Where(e => e.Name.LocalName == name) ?? [];

    private static string? Text(XElement? element, string name) => Child(element, name)?.Value.Trim() is { Length: > 0 } text ? text : null;
}
