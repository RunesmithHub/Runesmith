using System.ComponentModel;
using System.Composition;
using System.Diagnostics;
using Runesmith.Sdk.Build;

namespace Runesmith.Java;

/// <summary>Builds a folder's Maven or Gradle project, with its wrapper when it has one, and reports the compiler's errors and warnings.</summary>
[Export(typeof(IBuildProvider))]
public sealed class JavaBuildProvider : IBuildProvider
{
    public string Name => "Java";

    public bool CanBuild(string rootPath) => FindTool(rootPath) is not null;

    public async Task<BuildResult> BuildAsync(BuildContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var stopwatch = Stopwatch.StartNew();
        if (FindTool(context.RootPath) is not { } tool)
        {
            context.Output.AppendLine($"There is no Maven or Gradle build in {context.RootPath}.");
            return new BuildResult(false, 0, 0, stopwatch.Elapsed);
        }

        var start = new ProcessStartInfo(tool.Executable)
        {
            WorkingDirectory = context.RootPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in tool.Arguments)
            start.ArgumentList.Add(argument);

        context.Output.AppendLine($"> {tool.Display}");
        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (Win32Exception exception)
        {
            context.Output.AppendLine($"Could not start {tool.Display} ({exception.Message}). Install {tool.Name} or add its wrapper to the project, and make sure it is on the PATH.");
            return new BuildResult(false, 0, 0, stopwatch.Elapsed);
        }

        var collector = new DiagnosticCollector(context);
        using var registration = cancellationToken.Register(() => Kill(process));
        await Task.WhenAll(ReadAsync(process.StandardOutput, collector), ReadAsync(process.StandardError, collector)).ConfigureAwait(false);
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var succeeded = process.ExitCode == 0;
        context.Output.AppendLine(succeeded
            ? $"Build succeeded with {collector.Warnings} warning(s) in {stopwatch.Elapsed.TotalSeconds:0.0} s."
            : $"Build failed with {collector.Errors} error(s) and {collector.Warnings} warning(s) in {stopwatch.Elapsed.TotalSeconds:0.0} s.");
        return new BuildResult(succeeded, collector.Errors, collector.Warnings, stopwatch.Elapsed);
    }

    /// <summary>Finds how to build a folder: Maven when it has a <c>pom.xml</c>, Gradle when it has a build or settings script; each through
    /// its wrapper when the folder has one.</summary>
    public static BuildTool? FindTool(string rootPath)
    {
        if (!Directory.Exists(rootPath))
            return null;

        if (File.Exists(Path.Combine(rootPath, "pom.xml")))
            return Tool(rootPath, "Maven", "mvnw", "mvn", ["-q", "compile"]);

        string[] gradleFiles = ["build.gradle", "build.gradle.kts", "settings.gradle", "settings.gradle.kts"];
        return gradleFiles.Any(file => File.Exists(Path.Combine(rootPath, file)))
            ? Tool(rootPath, "Gradle", "gradlew", "gradle", ["compileJava"])
            : null;
    }

    // A wrapper script runs through the shell, so it needs no executable bit; on Windows its .cmd form runs directly.
    internal static BuildTool Tool(string rootPath, string name, string wrapper, string command, IReadOnlyList<string> arguments)
    {
        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(rootPath, wrapper + ".cmd");
            return File.Exists(script)
                ? new BuildTool(name, script, arguments, $"{wrapper} {string.Join(' ', arguments)}")
                : new BuildTool(name, command + ".cmd", arguments, $"{command} {string.Join(' ', arguments)}");
        }

        var unixScript = Path.Combine(rootPath, wrapper);
        return File.Exists(unixScript)
            ? new BuildTool(name, "sh", [unixScript, .. arguments], $"./{wrapper} {string.Join(' ', arguments)}")
            : new BuildTool(name, command, arguments, $"{command} {string.Join(' ', arguments)}");
    }

    private static async Task ReadAsync(StreamReader reader, DiagnosticCollector collector)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            collector.Add(line);
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The build had exited already.
        }
    }

    /// <summary>The program that builds a folder and how to call it.</summary>
    /// <param name="Name">The build tool's name, such as Maven.</param>
    /// <param name="Display">The command as the Output panel shows it.</param>
    public sealed record BuildTool(string Name, string Executable, IReadOnlyList<string> Arguments, string Display);

    // Both output streams feed it from their own threads, and build tools can repeat a diagnostic, so it locks and de-duplicates.
    private sealed class DiagnosticCollector(BuildContext context)
    {
        private readonly HashSet<Diagnostic> seen = [];
        private readonly Lock gate = new();

        public int Errors { get; private set; }

        public int Warnings { get; private set; }

        public void Add(string line)
        {
            context.Output.AppendLine(line);
            if (JavaBuildOutputParser.Parse(line, context.RootPath) is not { } diagnostic)
                return;

            lock (gate)
            {
                if (!seen.Add(diagnostic))
                    return;

                if (diagnostic.Severity == DiagnosticSeverity.Error)
                    Errors++;
                else
                    Warnings++;
            }

            context.ReportDiagnostic(diagnostic);
        }
    }
}
