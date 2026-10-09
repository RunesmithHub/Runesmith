using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Runesmith.Languages.Java.Projects;

namespace Runesmith.Java.Running;

/// <summary>Finds the class path a project's program runs with: its compiled classes, then its libraries as Maven or Gradle resolve them.</summary>
/// <remarks>Asking the build tool is slow, so its answer is cached in <c>.runesmith/cache/classpath</c> and asked again only after a build
/// file changes. When the tool cannot answer, the libraries the project loader found in the local caches stand in.</remarks>
internal sealed class JavaClassPath(Func<ProcessStartInfo, Action<string>, CancellationToken, Task<int>> run)
{
    private const string InitScript = """
        allprojects {
            tasks.register('runesmithClassPath') {
                def output = project.findProperty('runesmithClassPathFile')
                def classPath = project.provider { project.extensions.findByName('sourceSets')?.findByName('main')?.runtimeClasspath?.asPath ?: '' }
                doLast {
                    if (output) new File(output.toString()).text = classPath.get()
                }
            }
        }
        """;

    private static readonly string[] BuildFiles =
        ["pom.xml", "build.gradle", "build.gradle.kts", "settings.gradle", "settings.gradle.kts", "gradle.properties", Path.Combine("gradle", "libs.versions.toml")];

    /// <summary>Gets the class path that runs the build tools.</summary>
    public static JavaClassPath Default { get; } = new(JavaRunSupport.RunAsync);

    /// <summary>Gets the folder plain folders compile into.</summary>
    public static string PlainClasses(string rootPath) => Path.Combine(rootPath, ".runesmith", "out", "classes");

    /// <summary>Gets the folders a project compiles its main classes and resources into.</summary>
    public static IReadOnlyList<string> OutputFolders(string rootPath, JavaProject project) => project.Kind switch
    {
        JavaProjectKind.Maven => [Path.Combine(project.RootPath, "target", "classes")],
        JavaProjectKind.Gradle => [Path.Combine(project.RootPath, "build", "classes", "java", "main"), Path.Combine(project.RootPath, "build", "resources", "main")],
        _ => [PlainClasses(rootPath)],
    };

    /// <summary>Gets a project's class path: its output folders, then its libraries.</summary>
    public async Task<IReadOnlyList<string>> GetAsync(string rootPath, JavaProject project, string? javaHome, CancellationToken cancellationToken)
    {
        var libraries = project.Kind == JavaProjectKind.Folder
            ? project.ClassPathJars
            : ReadCache(rootPath, project) ?? await ResolveAsync(rootPath, project, javaHome, cancellationToken).ConfigureAwait(false) ?? project.ClassPathJars;
        return [.. OutputFolders(rootPath, project).Concat(libraries).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>Gets the file a project's resolved libraries are cached in.</summary>
    public static string CacheFile(string rootPath, JavaProject project)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(project.RootPath)))[..12];
        return Path.Combine(rootPath, ".runesmith", "cache", "classpath", $"{project.Kind.ToString().ToLowerInvariant()}-{hash}.txt");
    }

    /// <summary>Caches a project's libraries, stamped with its build files' last change.</summary>
    public static void WriteCache(string rootPath, JavaProject project, IEnumerable<string> libraries)
    {
        var file = CacheFile(rootPath, project);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllLines(file, [Stamp(rootPath, project), string.Join(Path.PathSeparator, libraries)]);
    }

    private static string[]? ReadCache(string rootPath, JavaProject project)
    {
        try
        {
            var lines = File.ReadAllLines(CacheFile(rootPath, project));
            return lines.Length >= 2 && lines[0] == Stamp(rootPath, project)
                ? lines[1].Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Stamp(string rootPath, JavaProject project)
    {
        var latest = new[] { rootPath, project.RootPath }.Distinct(StringComparer.Ordinal)
            .SelectMany(folder => BuildFiles.Select(file => Path.Combine(folder, file)))
            .Where(File.Exists)
            .Select(file => File.GetLastWriteTimeUtc(file).Ticks)
            .DefaultIfEmpty(0)
            .Max();
        return latest.ToString(CultureInfo.InvariantCulture);
    }

    private async Task<string[]?> ResolveAsync(string rootPath, JavaProject project, string? javaHome, CancellationToken cancellationToken)
    {
        var output = Path.Combine(Path.GetDirectoryName(CacheFile(rootPath, project))!, "resolved.tmp");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.Delete(output);
        JavaBuildProvider.BuildTool tool;
        if (project.Kind == JavaProjectKind.Maven)
        {
            tool = JavaBuildProvider.Tool(rootPath, "Maven", "mvnw", "mvn",
                ["-q", "-f", Path.Combine(project.RootPath, "pom.xml"), "dependency:build-classpath", "-Dmdep.includeScope=runtime", "-Dmdep.outputFile=" + output]);
        }
        else
        {
            var script = Path.Combine(Path.GetDirectoryName(output)!, "runesmith-classpath.gradle");
            await File.WriteAllTextAsync(script, InitScript, cancellationToken).ConfigureAwait(false);
            var relative = Path.GetRelativePath(rootPath, project.RootPath);
            var task = relative == "." ? ":runesmithClassPath" : ":" + relative.Replace(Path.DirectorySeparatorChar, ':').Replace('/', ':') + ":runesmithClassPath";
            tool = JavaBuildProvider.Tool(rootPath, "Gradle", "gradlew", "gradle", ["-q", "-I", script, task, "-PrunesmithClassPathFile=" + output]);
        }

        var start = new ProcessStartInfo(tool.Executable) { WorkingDirectory = rootPath };
        foreach (var argument in tool.Arguments)
            start.ArgumentList.Add(argument);
        if (javaHome is not null)
            start.Environment["JAVA_HOME"] = javaHome;

        try
        {
            if (await run(start, _ => { }, cancellationToken).ConfigureAwait(false) != 0 || !File.Exists(output))
                return null;
        }
        catch (Win32Exception)
        {
            return null;
        }

        var libraries = (await File.ReadAllTextAsync(output, cancellationToken).ConfigureAwait(false))
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        File.Delete(output);
        WriteCache(rootPath, project, libraries);
        return libraries;
    }
}
