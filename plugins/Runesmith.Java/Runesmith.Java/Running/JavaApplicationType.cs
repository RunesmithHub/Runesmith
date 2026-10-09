using System.ComponentModel;
using System.Composition;
using System.Diagnostics;
using System.Text;
using Runesmith.Languages.Java.Projects;
using Runesmith.Sdk.Build;
using Runesmith.Sdk.Options;
using Runesmith.Sdk.Running;
using Runesmith.Sdk.Sdks;

namespace Runesmith.Java.Running;

/// <summary>Runs a Java program: a class with a <c>main</c> method, or a compact source file.</summary>
/// <remarks>
/// <para>Maven and Gradle projects run from their compiled classes (<c>target/classes</c>, or <c>build/classes/java/main</c> and
/// <c>build/resources/main</c>), which the folder's build provider compiles, plus the libraries the build tool resolves: Maven's
/// <c>dependency:build-classpath</c>, or a small Gradle init script that prints the main source set's runtime class path. Their answer is
/// cached until a build file changes.</para>
/// <para>A folder without a build compiles its sources with <c>javac</c> into <c>.runesmith/out/classes</c> as the Build step, and runs
/// from there. A compact source file runs from its source with <c>java File.java</c> and needs no build.</para>
/// </remarks>
[Export(typeof(IRunConfigurationType))]
[Shared]
public sealed class JavaApplicationType : IRunConfigurationType
{
    /// <summary>The type's id.</summary>
    public const string TypeId = "java.application";

    internal const string MainClass = "mainClass";
    internal const string Module = "module";
    internal const string VmOptions = "vmOptions";

    private readonly ISdkService? sdks;
    private readonly JavaClassPath classPath;

    /// <summary>Creates the type, which runs programs with the configuration's JDK or the user's default one.</summary>
    [ImportingConstructor]
    public JavaApplicationType([Import(AllowDefault = true)] ISdkService? sdks)
        : this(sdks, JavaClassPath.Default)
    {
    }

    internal JavaApplicationType(ISdkService? sdks, JavaClassPath classPath)
    {
        this.sdks = sdks;
        this.classPath = classPath;
    }

    public string Id => TypeId;

    public string Name => "Java application";

    public string Icon => "play";

    public string Description => "Run a class with a main method, or a compact source file.";

    public bool CanDebug => true;

    /// <remarks>The Module option shows only when the folder's main sources have a <c>module-info.java</c>.</remarks>
    public Task<OptionSet> GetOptionsAsync(string rootPath, CancellationToken cancellationToken) =>
        Task.Run(() => CreateOptions(MainClassFinder.HasModuleDeclaration(rootPath)), cancellationToken);

    public Task<IReadOnlyList<RunConfiguration>> DetectAsync(string rootPath, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<RunConfiguration>>(() =>
        {
            var defaults = CreateOptions(modular: false).Defaults();
            var names = new HashSet<string>(StringComparer.Ordinal);
            var configurations = new List<RunConfiguration>();
            foreach (var entry in MainClassFinder.Find(rootPath, cancellationToken))
            {
                var name = entry.SimpleName;
                if (!names.Add(name) && !names.Add(name = entry.Name))
                    continue;

                var values = defaults.Copy();
                values.Set(MainClass, entry.Name);
                configurations.Add(new RunConfiguration(TypeId, name, values) { IsDetected = true });
            }

            return configurations;
        }, cancellationToken);

    public async Task<LaunchPlan> PrepareAsync(RunConfiguration configuration, RunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(context);
        var values = configuration.Values;
        var main = values.Get(MainClass)?.Trim();
        if (string.IsNullOrEmpty(main))
            throw new InvalidOperationException($"{configuration.Name} has no main class.");

        var javaHome = await JavaRunSupport.FindJavaHomeAsync(values, sdks, cancellationToken).ConfigureAwait(false);
        var java = JavaRunSupport.Tool(javaHome, "java");
        var vmOptions = values.GetList(VmOptions).ToList();
        if (context.Mode == RunMode.Debug)
            vmOptions.Insert(0, JavaRunSupport.JdwpAgent);
        var arguments = values.GetList(JavaRunSupport.Arguments);
        var environment = JavaRunSupport.LaunchEnvironment(values, javaHome);

        List<string> launch;
        string workingDirectory;
        string? path = null;
        if (IsSourceFile(main))
        {
            var file = Path.GetFullPath(Path.Combine(context.RootPath, main));
            workingDirectory = JavaRunSupport.ResolveWorkingDirectory(values, context.RootPath, context.RootPath);
            launch = [.. vmOptions, file, .. arguments];
        }
        else
        {
            var project = FindProject(context.RootPath, main);
            workingDirectory = JavaRunSupport.ResolveWorkingDirectory(values, context.RootPath, project.RootPath);
            path = string.Join(Path.PathSeparator, await classPath.GetAsync(context.RootPath, project, javaHome, cancellationToken).ConfigureAwait(false));
            launch = values.Get(Module) is { Length: > 0 } module
                ? [.. vmOptions, "--module-path", path, "--module", $"{module.Trim()}/{main}", .. arguments]
                : [.. vmOptions, "-cp", path, main, .. arguments];
        }

        var plan = new LaunchPlan(java, launch, workingDirectory) { Environment = environment };
        if (context.Mode != RunMode.Debug)
            return plan;

        var settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["java"] = java,
            [IsSourceFile(main) ? "file" : "mainClass"] = main,
            ["classPath"] = path ?? "",
            ["vmOptions"] = string.Join('\n', vmOptions),
            ["args"] = string.Join('\n', arguments),
            ["cwd"] = workingDirectory,
            ["env"] = string.Join('\n', environment.Select(pair => $"{pair.Key}={pair.Value}")),
        };
        if (values.Get(Module) is { Length: > 0 } debugModule)
            settings["module"] = debugModule.Trim();
        return plan with { Debug = new DebugLaunch("jdwp", settings) };
    }

    public async Task<BuildResult?> BuildAsync(RunConfiguration configuration, BuildContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(context);
        if (IsSourceFile(configuration.Values.Get(MainClass)?.Trim() ?? ""))
            return new BuildResult(true, 0, 0, TimeSpan.Zero);

        var projects = JavaProjectLoader.Load(context.RootPath);
        if (projects.Any(p => p.Kind != JavaProjectKind.Folder))
            return null;

        var javaHome = await JavaRunSupport.FindJavaHomeAsync(configuration.Values, sdks, cancellationToken).ConfigureAwait(false);
        return await CompileAsync(context, projects[0], javaHome, cancellationToken).ConfigureAwait(false);
    }

    internal static OptionSet CreateOptions(bool modular)
    {
        List<Option> options =
        [
            new Option(MainClass, "Main class", OptionKind.Text)
            {
                IsRequired = true,
                Placeholder = "com.example.Main",
                Description = "The class with the main method, or the path of a compact source file such as Hello.java.",
            },
            JavaRunSupport.JdkOption,
            new Option(VmOptions, "VM options", OptionKind.List) { Placeholder = "-Xmx512m", Description = "Options for the JVM, one per line." },
            JavaRunSupport.ArgumentsOption("Program arguments", "Arguments passed to main, one per line."),
        ];
        if (modular)
        {
            options.Add(new Option(Module, "Module", OptionKind.Text)
            {
                Group = OptionGroup.Advanced,
                Placeholder = "com.example.app",
                Description = "The module of the main class, which then runs from the module path.",
            });
        }

        options.AddRange([JavaRunSupport.EnvironmentOption, JavaRunSupport.WorkingDirectoryOption]);
        return new OptionSet(options);
    }

    private static bool IsSourceFile(string main) => main.EndsWith(".java", StringComparison.OrdinalIgnoreCase);

    // The project whose sources hold the class, so a module's own output and libraries are used; the first project otherwise.
    private static JavaProject FindProject(string rootPath, string mainClass)
    {
        var projects = JavaProjectLoader.Load(rootPath);
        var relative = mainClass.Replace('.', Path.DirectorySeparatorChar) + ".java";
        return projects.FirstOrDefault(p => p.SourceRoots.Any(root => File.Exists(Path.Combine(root, relative)))) ?? projects[0];
    }

    private static async Task<BuildResult> CompileAsync(BuildContext context, JavaProject project, string? javaHome, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var sources = project.SourceRoots
            .SelectMany(root => Directory.Exists(root) ? Directory.EnumerateFiles(root, "*.java", SearchOption.AllDirectories) : [])
            .Where(file => !IsExcluded(context.RootPath, file) && !MainClassFinder.FindInFile(context.RootPath, file).Any(entry => entry.IsCompactSourceFile))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var output = JavaClassPath.PlainClasses(context.RootPath);
        if (Directory.Exists(output))
            Directory.Delete(output, recursive: true);
        Directory.CreateDirectory(output);
        if (sources.Count == 0)
        {
            context.Output.AppendLine("There are no Java sources to compile.");
            return new BuildResult(true, 0, 0, stopwatch.Elapsed);
        }

        var argumentFile = Path.Combine(Path.GetDirectoryName(output)!, "sources.txt");
        await File.WriteAllLinesAsync(argumentFile, sources.Select(Quote), cancellationToken).ConfigureAwait(false);
        var javac = JavaRunSupport.Tool(javaHome, "javac");
        var start = new ProcessStartInfo(javac) { WorkingDirectory = context.RootPath, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        List<string> arguments = ["-d", output, "-encoding", "UTF-8", "-g"];
        if (project.ClassPathJars.Count > 0)
            arguments.AddRange(["-cp", string.Join(Path.PathSeparator, project.ClassPathJars)]);
        arguments.Add("@" + argumentFile);
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        context.Output.AppendLine($"> javac -d {Path.GetRelativePath(context.RootPath, output)} ({sources.Count} files)");
        var errors = 0;
        var warnings = 0;
        var gate = new Lock();
        void Line(string line)
        {
            context.Output.AppendLine(line);
            if (JavaBuildOutputParser.Parse(line, context.RootPath) is not { } diagnostic)
                return;
            lock (gate)
            {
                if (diagnostic.Severity == DiagnosticSeverity.Error)
                    errors++;
                else
                    warnings++;
            }

            context.ReportDiagnostic(diagnostic);
        }

        int exitCode;
        try
        {
            exitCode = await JavaRunSupport.RunAsync(start, Line, cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception exception)
        {
            context.Output.AppendLine($"Could not start javac ({exception.Message}). Install a JDK, or choose one in the configuration.");
            return new BuildResult(false, 1, 0, stopwatch.Elapsed);
        }

        var succeeded = exitCode == 0;
        context.Output.AppendLine(succeeded
            ? $"Compiled {sources.Count} files in {stopwatch.Elapsed.TotalSeconds:0.0} s."
            : $"Compiling failed with {errors} error(s) and {warnings} warning(s).");
        return new BuildResult(succeeded, succeeded ? errors : Math.Max(errors, 1), warnings, stopwatch.Elapsed);
    }

    private static bool IsExcluded(string rootPath, string file)
    {
        var parts = Path.GetRelativePath(rootPath, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[..^1];
        var isTest = parts.Zip(parts.Skip(1)).Any(pair => pair.First == "src" && pair.Second == "test");
        return isTest || parts.Any(part => part.StartsWith('.') || part is "target" or "build" or "out" or "bin" or "obj" or "node_modules");
    }

    private static string Quote(string path) => "\"" + path.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
