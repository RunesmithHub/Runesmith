using System.Text.RegularExpressions;

namespace Runesmith.Languages.Java.Projects;

/// <summary>Reads a Gradle build by reading its scripts, not running them: the projects <c>settings.gradle</c> includes, each project's Java
/// version and dependencies, and the version catalog.</summary>
/// <remarks>Build scripts are programs, so this understands the common, declarative forms only; anything computed is left out rather than
/// guessed.</remarks>
internal static partial class GradleProjectReader
{
    private static readonly string[] SettingsFiles = ["settings.gradle.kts", "settings.gradle"];
    private static readonly string[] BuildFiles = ["build.gradle.kts", "build.gradle"];

    public static bool IsGradleBuild(string folder) =>
        SettingsFiles.Concat(BuildFiles).Any(file => File.Exists(Path.Combine(folder, file)));

    public static IReadOnlyList<JavaProject> Read(string rootPath, JavaProjectLoaderOptions options)
    {
        var root = Path.GetFullPath(rootPath);
        var settings = ReadFirst(root, SettingsFiles);
        var properties = ReadProperties(Path.Combine(root, "gradle.properties"));
        var catalog = VersionCatalog.Read(Path.Combine(root, "gradle", "libs.versions.toml"));
        var rootName = settings is not null && RootNamePattern().Match(settings) is { Success: true } nameMatch ? nameMatch.Groups[1].Value : Path.GetFileName(root);

        var modules = new List<(string Name, string Folder)> { (rootName, root) };
        if (settings is not null)
        {
            foreach (Match include in IncludePattern().Matches(settings))
            {
                foreach (Match quoted in QuotedPattern().Matches(include.Groups[1].Value))
                {
                    var path = quoted.Groups[1].Value.TrimStart(':');
                    if (path.Length > 0)
                        modules.Add((path.Split(':')[^1], Path.Combine(root, path.Replace(':', Path.DirectorySeparatorChar))));
                }
            }
        }

        var rootScript = ReadFirst(root, BuildFiles) ?? "";
        var defaultRelease = ReadRelease(rootScript) ?? options.DefaultRelease;
        var defaultPreview = rootScript.Contains("--enable-preview", StringComparison.Ordinal);
        var resolver = new MavenResolver([new GradleCacheRepository(options.GradleCache), new MavenLocalRepository(options.MavenRepository)], options.MaximumLibraries);
        var names = modules.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        var projects = new List<JavaProject>();
        foreach (var (name, folder) in modules)
        {
            var script = ReadFirst(folder, BuildFiles);
            if (script is null && folder != root)
                continue;

            script ??= "";
            var sourceRoots = SourceRoots(folder, script);
            if (folder == root && modules.Count > 1 && sourceRoots.Count == 0)
                continue;

            var dependencies = new List<PomDependency>();
            var references = new List<string>();
            foreach (var coordinate in Dependencies(script, properties, catalog))
                dependencies.Add(new PomDependency(coordinate.GroupId, coordinate.ArtifactId, coordinate.Version, null, null, null, false, [], null));
            foreach (Match reference in ProjectDependencyPattern().Matches(script))
            {
                var referenced = reference.Groups[1].Value.TrimStart(':').Split(':')[^1];
                if (names.Contains(referenced))
                    references.Add(referenced);
            }

            var pom = new EffectivePom
            {
                GroupId = "",
                ArtifactId = name,
                Version = "",
                Packaging = null,
                Properties = new Dictionary<string, string>(),
                DependencyManagement = new Dictionary<string, PomDependency>(),
                Dependencies = dependencies,
                Modules = [],
                SourceDirectory = null,
                TestSourceDirectory = null,
                CompilerRelease = null,
                CompilerEnablesPreview = false,
            };
            var jars = resolver.Resolve(pom, _ => false, []);
            projects.Add(new JavaProject(name, folder, JavaProjectKind.Gradle, sourceRoots, ReadRelease(script) ?? defaultRelease,
                defaultPreview || script.Contains("--enable-preview", StringComparison.Ordinal), jars, references));
        }

        return projects;
    }

    /// <summary>Reads the Java release a script sets: the toolchain's language version, <c>options.release</c>, or
    /// <c>sourceCompatibility</c>.</summary>
    internal static int? ReadRelease(string script)
    {
        foreach (var pattern in new[] { ToolchainPattern(), ReleaseOptionPattern(), SourceCompatibilityPattern() })
        {
            if (pattern.Match(script) is { Success: true } match && JavaRelease.Parse(match.Groups[1].Value) is { } release)
                return release;
        }

        return null;
    }

    private static IEnumerable<ArtifactCoordinate> Dependencies(string script, Dictionary<string, string> properties, VersionCatalog catalog)
    {
        foreach (Match match in StringDependencyPattern().Matches(script))
        {
            var version = Substitute(match.Groups[4].Value, properties);
            if (version is not null)
                yield return new ArtifactCoordinate(match.Groups[2].Value, match.Groups[3].Value, version);
        }

        foreach (Match match in MapDependencyPattern().Matches(script))
        {
            var version = Substitute(match.Groups[4].Value, properties);
            if (version is not null)
                yield return new ArtifactCoordinate(match.Groups[2].Value, match.Groups[3].Value, version);
        }

        foreach (Match match in CatalogDependencyPattern().Matches(script))
        {
            if (catalog.Find(match.Groups[2].Value) is { } coordinate)
                yield return coordinate;
        }
    }

    // Gradle scripts interpolate "$name" and "${name}"; only names from gradle.properties can be known without running the script.
    private static string? Substitute(string text, Dictionary<string, string> properties)
    {
        var result = InterpolationPattern().Replace(text, m => properties.TryGetValue(m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : m.Groups[2].Value, out var value) ? value : m.Value);
        return result.Contains('$', StringComparison.Ordinal) ? null : result;
    }

    private static List<string> SourceRoots(string folder, string script)
    {
        var candidates = new List<string> { "src/main/java", "src/test/java" };
        foreach (Match match in SourceDirectoryPattern().Matches(script))
        {
            foreach (Match quoted in QuotedPattern().Matches(match.Groups[1].Value))
                candidates.Add(quoted.Groups[1].Value);
        }

        return [.. candidates.Select(c => Path.GetFullPath(Path.Combine(folder, c))).Where(Directory.Exists).Distinct(StringComparer.Ordinal)];
    }

    private static Dictionary<string, string> ReadProperties(string path)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path))
            return properties;

        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            var separator = trimmed.IndexOfAny(['=', ':']);
            if (trimmed.Length == 0 || trimmed[0] is '#' or '!' || separator <= 0)
                continue;
            properties[trimmed[..separator].Trim()] = trimmed[(separator + 1)..].Trim();
        }

        return properties;
    }

    private static string? ReadFirst(string folder, string[] files)
    {
        foreach (var file in files)
        {
            var path = Path.Combine(folder, file);
            if (File.Exists(path))
                return File.ReadAllText(path);
        }

        return null;
    }

    [GeneratedRegex("""rootProject\.name\s*=\s*["']([^"']+)["']""")]
    private static partial Regex RootNamePattern();

    [GeneratedRegex(@"\binclude\s*\(?([^)\n]*)")]
    private static partial Regex IncludePattern();

    [GeneratedRegex("""["']([^"']+)["']""")]
    private static partial Regex QuotedPattern();

    [GeneratedRegex(@"JavaLanguageVersion\.of\(\s*(\d+)\s*\)")]
    private static partial Regex ToolchainPattern();

    [GeneratedRegex(@"options\.release(?:\.set\(|\s*=)\s*(\d+)")]
    private static partial Regex ReleaseOptionPattern();

    [GeneratedRegex("""sourceCompatibility\s*=\s*(?:JavaVersion\.)?["']?(VERSION_[\d_]+|[\d.]+)""")]
    private static partial Regex SourceCompatibilityPattern();

    [GeneratedRegex("""\b(implementation|api|compileOnly|compileOnlyApi|runtimeOnly|testImplementation|testCompileOnly|testRuntimeOnly|annotationProcessor)\s*\(?\s*["']([^"':\s]+):([^"':\s]+):([^"'@:\s]+)(?::[^"'@\s]+)?(?:@\w+)?["']""")]
    private static partial Regex StringDependencyPattern();

    [GeneratedRegex("""\b(implementation|api|compileOnly|compileOnlyApi|runtimeOnly|testImplementation|testCompileOnly|testRuntimeOnly)\s*\(?\s*group\s*[:=]\s*["']([^"']+)["']\s*,\s*name\s*[:=]\s*["']([^"']+)["']\s*,\s*version\s*[:=]\s*["']([^"']+)["']""")]
    private static partial Regex MapDependencyPattern();

    [GeneratedRegex(@"\b(implementation|api|compileOnly|compileOnlyApi|runtimeOnly|testImplementation|testCompileOnly|testRuntimeOnly)\s*\(?\s*libs\.([\w.]+?)(?:\.get\(\))?\s*\)?\s*$", RegexOptions.Multiline)]
    private static partial Regex CatalogDependencyPattern();

    [GeneratedRegex("""project\(\s*(?:path\s*[:=]\s*)?["']([^"']+)["']""")]
    private static partial Regex ProjectDependencyPattern();

    [GeneratedRegex(@"srcDirs?\s*(?:\(|=|\+=)?\s*\[?([^\]\)\n]*)")]
    private static partial Regex SourceDirectoryPattern();

    [GeneratedRegex(@"\$\{(\w+)\}|\$(\w+)")]
    private static partial Regex InterpolationPattern();
}

/// <summary>The libraries of a Gradle version catalog, <c>gradle/libs.versions.toml</c>, by their accessor name.</summary>
internal sealed partial class VersionCatalog
{
    private readonly Dictionary<string, ArtifactCoordinate> libraries = new(StringComparer.Ordinal);

    public static VersionCatalog Read(string path)
    {
        var catalog = new VersionCatalog();
        if (!File.Exists(path))
            return catalog;

        var versions = new Dictionary<string, string>(StringComparer.Ordinal);
        var section = "";
        var libraryLines = new List<(string Alias, string Value)>();
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            if (line[0] == '[')
            {
                section = line.Trim('[', ']').Trim();
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals <= 0)
                continue;

            var key = line[..equals].Trim().Trim('"');
            var value = line[(equals + 1)..].Trim();
            if (section == "versions")
                versions[key] = Unquote(value);
            else if (section == "libraries")
                libraryLines.Add((key, value));
        }

        foreach (var (alias, value) in libraryLines)
        {
            if (Parse(value, versions) is { } coordinate)
                catalog.libraries[Normalize(alias)] = coordinate;
        }

        return catalog;
    }

    /// <summary>Finds a library by the accessor a script uses, such as <c>libs.jackson.databind</c> for the alias <c>jackson-databind</c>.</summary>
    public ArtifactCoordinate? Find(string accessor) => libraries.GetValueOrDefault(Normalize(accessor));

    private static ArtifactCoordinate? Parse(string value, Dictionary<string, string> versions)
    {
        if (value.StartsWith('"'))
        {
            var parts = Unquote(value).Split(':');
            return parts.Length >= 3 ? new ArtifactCoordinate(parts[0], parts[1], parts[2]) : null;
        }

        var fields = FieldPattern().Matches(value).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.Ordinal);
        string? group = null;
        string? name = null;
        if (fields.TryGetValue("module", out var module) && module.Split(':') is [var g, var n])
            (group, name) = (g, n);
        else if (fields.TryGetValue("group", out var groupValue) && fields.TryGetValue("name", out var nameValue))
            (group, name) = (groupValue, nameValue);

        var version = fields.TryGetValue("version.ref", out var reference) ? versions.GetValueOrDefault(reference)
            : fields.TryGetValue("version", out var direct) ? direct
            : fields.TryGetValue("strictly", out var strict) ? strict
            : fields.TryGetValue("require", out var required) ? required
            : null;
        return group is null || name is null || version is null ? null : new ArtifactCoordinate(group, name, version);
    }

    private static string Normalize(string alias) => alias.Replace('-', '.').Replace('_', '.').ToLowerInvariant();

    private static string Unquote(string value) => value.Trim().Trim('"', '\'');

    [GeneratedRegex("""([\w.]+)\s*=\s*"([^"]*)"\s*""")]
    private static partial Regex FieldPattern();
}
