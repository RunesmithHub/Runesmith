using System.Collections.Concurrent;
using Runesmith.Languages.Java.Jdk;
using Runesmith.Languages.Java.Libraries;
using Runesmith.Languages.Java.Projects;
using Runesmith.Languages.Java.Semantics;
using Runesmith.Languages.Java.Syntax;

namespace Runesmith.Languages.Java.Analysis;

/// <summary>The Java projects of the open folder, the JDK API of each release they target, and the project of files outside them all.</summary>
internal sealed class JavaWorkspace(string? cacheDirectory, Action<string> log, JavaProjectLoaderOptions? loaderOptions = null, Func<JdkSearchOptions>? jdkSearch = null)
{
    private const int FilesPerBatch = 200;

    // Names of libraries that process annotations, which may generate the types and members code refers to.
    private static readonly string[] ProcessorHints = ["lombok", "processor", "-compiler", "-apt", "jpamodelgen", "auto-value", "immutables"];

    private readonly ConcurrentDictionary<int, Lazy<JdkApi?>> jdks = new();
    private readonly Lock gate = new();
    private IReadOnlyList<JavaProjectModel> projects = [];
    private JavaProjectModel? fallback;

    /// <summary>Gets the projects of the open folder, once loaded.</summary>
    public IReadOnlyList<JavaProjectModel> Projects => Volatile.Read(ref projects);

    /// <summary>Gets the project a file belongs to: the one with the most specific source root that holds it, or the project of loose files.</summary>
    public JavaProjectModel ProjectFor(string path)
    {
        JavaProjectModel? best = null;
        var bestLength = -1;
        foreach (var project in Projects)
        {
            var length = project.Specificity(path);
            if (length > bestLength)
                (best, bestLength) = (project, length);
        }

        return best ?? Fallback;
    }

    /// <summary>Gets the project of files outside every project: the newest release the JDK has, without libraries.</summary>
    public JavaProjectModel Fallback
    {
        get
        {
            lock (gate)
            {
                if (fallback is not null)
                    return fallback;
            }

            var created = CreateFallback();
            lock (gate)
                return fallback ??= created;
        }
    }

    /// <summary>Gets the JDK API of a release, opened once; null when no installed JDK can give it.</summary>
    public JdkApi? Jdk(int release) => jdks.GetOrAdd(release, r => new Lazy<JdkApi?>(() => OpenJdk(r))).Value;

    /// <summary>Reads the folder's projects, opens their libraries, then parses their sources, pausing between files for other requests.</summary>
    public async Task LoadAsync(string rootPath, Func<CancellationToken, ValueTask> yield, CancellationToken cancellationToken)
    {
        var loaded = JavaProjectLoader.Load(rootPath, loaderOptions);
        var models = new List<JavaProjectModel>();
        foreach (var project in loaded)
        {
            await yield(cancellationToken).ConfigureAwait(false);
            models.Add(CreateModel(project));
        }

        Link(loaded, models);

        Volatile.Write(ref projects, models);
        log($"Loaded {models.Count} Java project(s) from {rootPath}.");
        foreach (var model in models)
            await IndexAsync(model, yield, cancellationToken).ConfigureAwait(false);
    }

    // A project compiles against the sources of the projects it depends on, and theirs in turn.
    private static void Link(IReadOnlyList<JavaProject> projects, List<JavaProjectModel> models)
    {
        var byName = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < projects.Count; i++)
            byName.TryAdd(projects[i].Name, i);

        for (var i = 0; i < projects.Count; i++)
        {
            var found = new List<JavaProjectModel>();
            var allFound = true;
            var visited = new HashSet<int> { i };
            var pending = new Stack<string>(projects[i].ProjectReferences);
            while (pending.TryPop(out var name))
            {
                if (!byName.TryGetValue(name, out var index))
                {
                    allFound = false;
                    continue;
                }

                if (!visited.Add(index))
                    continue;
                found.Add(models[index]);
                foreach (var next in projects[index].ProjectReferences)
                    pending.Push(next);
            }

            models[i].SetReferences(found, allFound);
        }
    }

    /// <summary>Parses a file from disk into its project again, such as after its document closed without saving.</summary>
    public static void Reload(JavaProjectModel project, string path)
    {
        if (!File.Exists(path))
        {
            project.RemoveFile(path);
            return;
        }

        try
        {
            project.SetFile(SourceFile.Create(path, JavaSyntaxTree.Parse(File.ReadAllText(path), project.ParseOptions)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            project.RemoveFile(path);
        }
    }

    private static async Task IndexAsync(JavaProjectModel model, Func<CancellationToken, ValueTask> yield, CancellationToken cancellationToken)
    {
        var batch = new List<SourceFile>();
        foreach (var path in SourceFiles(model.SourceRoots))
        {
            await yield(cancellationToken).ConfigureAwait(false);
            try
            {
                batch.Add(SourceFile.Create(path, JavaSyntaxTree.Parse(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false), model.ParseOptions)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (batch.Count >= FilesPerBatch)
            {
                model.SetFiles(batch);
                batch = [];
            }
        }

        model.SetFiles(batch);
    }

    private static IEnumerable<string> SourceFiles(IReadOnlyList<string> roots)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
        var seen = new HashSet<string>(SourceLibrary.PathComparer);
        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var path in Directory.EnumerateFiles(root, "*.java", options))
            {
                if (seen.Add(path))
                    yield return path;
            }
        }
    }

    private JavaProjectModel CreateModel(JavaProject project)
    {
        var version = JavaVersion.FromRelease(project.Release, project.PreviewEnabled);
        var jdk = Jdk(version.Release);
        // A folder without a build file names no release, so it gets the newest one the installed JDK has.
        if (jdk is null && project.Kind == JavaProjectKind.Folder && NewestRelease() is { } newest && newest < version.Release)
        {
            version = new JavaVersion(newest);
            jdk = Jdk(newest);
        }

        var libraries = new List<IClassLibrary>();
        foreach (var jar in project.ClassPathJars)
        {
            try
            {
                libraries.Add(JarLibrary.Open(jar, version.Release, cacheDirectory));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                log($"Skipped {jar}: {exception.Message}");
            }
        }

        var roots = project.SourceRoots.ToList();
        var generated = Path.Combine(project.RootPath, "build", "generated", "sources", "annotationProcessor", "java", "main");
        if (Directory.Exists(generated))
            roots.Add(generated);

        return new JavaProjectModel(project.Name, roots, version, jdk, libraries, MayGenerateCode(project));
    }

    private static bool MayGenerateCode(JavaProject project)
    {
        if (project.ClassPathJars.Any(jar => ProcessorHints.Any(hint => Path.GetFileName(jar).Contains(hint, StringComparison.OrdinalIgnoreCase))))
            return true;

        foreach (var buildFile in new[] { "pom.xml", "build.gradle", "build.gradle.kts" })
        {
            var path = Path.Combine(project.RootPath, buildFile);
            try
            {
                if (File.Exists(path) && File.ReadAllText(path) is var text
                    && (text.Contains("annotationProcessor", StringComparison.Ordinal) || text.Contains("lombok", StringComparison.Ordinal)))
                {
                    return true;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return true;
            }
        }

        return false;
    }

    private int? NewestRelease()
    {
        var found = JdkLocator.FindAll(jdkSearch?.Invoke());
        return found.Count > 0 ? Math.Clamp(found[0].FeatureVersion, JavaVersion.MinimumRelease, JavaVersion.LatestRelease) : null;
    }

    private JavaProjectModel CreateFallback()
    {
        var release = NewestRelease() ?? JavaVersion.LatestRelease;
        return new JavaProjectModel("loose files", [], new JavaVersion(release), Jdk(release), []);
    }

    private JdkApi? OpenJdk(int release)
    {
        try
        {
            if (JdkLocator.FindFor(release, jdkSearch?.Invoke()) is not { } installation)
            {
                log($"No JDK can give the API of Java {release}; set JAVA_HOME to a JDK {release} or newer.");
                return null;
            }

            var api = JdkApi.Open(installation, release, cacheDirectory);
            log($"Using the API of Java {release} from JDK {installation.Version} at {installation.HomePath}.");
            return api;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentOutOfRangeException)
        {
            log($"Could not read the JDK API of Java {release}: {exception.Message}");
            return null;
        }
    }
}
