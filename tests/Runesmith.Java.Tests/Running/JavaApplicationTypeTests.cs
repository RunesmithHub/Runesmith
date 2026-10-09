using System.Diagnostics;
using Runesmith.Java.Running;
using Runesmith.Languages.Java.Projects;
using Runesmith.Sdk.Build;
using Runesmith.Sdk.Running;
using Runesmith.Sdk.Shell;

namespace Runesmith.Java.Tests.Running;

public sealed class JavaApplicationTypeTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("runesmith-java-run-").FullName;

    [Fact]
    public async Task DetectsClassesWithAMainMethod()
    {
        Write("src/com/example/App.java", "package com.example; public class App { public static void main(String[] args) { } }");
        Write("src/com/example/Tool.java", "package com.example; class Tool { static void main(String... args) { } }");
        Write("src/Instance.java", "class Instance { void main() { } }");
        Write("src/OldStyle.java", "class OldStyle { public static void main(String args[]) { } }");
        Write("src/com/example/Helper.java", "package com.example; class Helper { int main(String[] args) { return 0; } void main(int x) { } }");

        var names = (await Detect()).ToDictionary(c => c.Name, c => c.Values.Get(JavaApplicationType.MainClass));

        Assert.Equal(new Dictionary<string, string?>
        {
            ["App"] = "com.example.App",
            ["Tool"] = "com.example.Tool",
            ["Instance"] = "Instance",
            ["OldStyle"] = "OldStyle",
        }, names);
    }

    [Fact]
    public async Task DetectsCompactSourceFiles()
    {
        Write("scripts/Hello.java", "void main() { IO.println(\"Hello\"); }");

        var configuration = Assert.Single(await Detect());

        Assert.Equal("Hello.java", configuration.Name);
        Assert.Equal("scripts/Hello.java", configuration.Values.Get(JavaApplicationType.MainClass));
        Assert.True(configuration.IsDetected);
    }

    [Fact]
    public async Task SkipsTestSourcesBuildOutputAndHiddenFolders()
    {
        Write("pom.xml", Pom);
        Write("src/main/java/com/example/App.java", "package com.example; public class App { public static void main(String[] args) { } }");
        Write("src/test/java/com/example/AppTest.java", "package com.example; class AppTest { public static void main(String[] args) { } }");
        Write("target/generated-sources/Gen.java", "class Gen { public static void main(String[] args) { } }");

        var configuration = Assert.Single(await Detect());

        Assert.Equal("App", configuration.Name);
    }

    [Fact]
    public async Task NamesClassesWithTheSameNameByTheirFullName()
    {
        Write("src/a/Main.java", "package a; class Main { public static void main(String[] args) { } }");
        Write("src/b/Main.java", "package b; class Main { public static void main(String[] args) { } }");

        var names = (await Detect()).Select(c => c.Name).Order(StringComparer.Ordinal);

        Assert.Equal(["Main", "b.Main"], names);
    }

    [Fact]
    public async Task RunsPlainFoldersFromTheirCompiledClasses()
    {
        Write("src/Main.java", "class Main { public static void main(String[] args) { } }");
        var configuration = Configuration("Main", ("arguments", "one\ntwo"), ("vmOptions", "-Xmx64m"), ("environment", "GREETING=hi"), ("jdk", "/jdks/25"));

        var plan = await Type().PrepareAsync(configuration, new RunContext(root, RunMode.Run), CancellationToken.None);

        Assert.Equal(Path.Combine("/jdks/25", "bin", OperatingSystem.IsWindows() ? "java.exe" : "java"), plan.Program);
        Assert.Equal(["-Xmx64m", "-cp", JavaClassPath.PlainClasses(root), "Main", "one", "two"], plan.Arguments);
        Assert.Equal(root, plan.WorkingDirectory);
        Assert.Equal("hi", plan.Environment["GREETING"]);
        Assert.Equal("/jdks/25", plan.Environment["JAVA_HOME"]);
        Assert.Null(plan.Debug);
    }

    [Fact]
    public async Task RunsCompactSourceFilesFromTheirSource()
    {
        Write("Hello.java", "void main() { }");

        var plan = await Type().PrepareAsync(Configuration("Hello.java", ("arguments", "x")), new RunContext(root, RunMode.Run), CancellationToken.None);

        Assert.Equal([Path.Combine(root, "Hello.java"), "x"], plan.Arguments);
        var build = await Type().BuildAsync(Configuration("Hello.java"), Context(), CancellationToken.None);
        Assert.True(build?.Succeeded);
    }

    [Fact]
    public async Task RunsMavenProjectsWithTheCachedClassPath()
    {
        Write("pom.xml", Pom);
        Write("src/main/java/com/example/App.java", "package com.example; public class App { public static void main(String[] args) { } }");
        var project = JavaProjectLoader.Load(root)[0];
        JavaClassPath.WriteCache(root, project, ["/repo/a.jar", "/repo/b.jar"]);

        var plan = await Type().PrepareAsync(Configuration("com.example.App"), new RunContext(root, RunMode.Run), CancellationToken.None);

        var classPath = string.Join(Path.PathSeparator, Path.Combine(root, "target", "classes"), "/repo/a.jar", "/repo/b.jar");
        Assert.Equal(["-cp", classPath, "com.example.App"], plan.Arguments);
        Assert.Null(await Type().BuildAsync(Configuration("com.example.App"), Context(), CancellationToken.None));
    }

    [Fact]
    public async Task AsksMavenForTheClassPathWhenThePomChanged()
    {
        Write("pom.xml", Pom);
        Write("src/main/java/App.java", "public class App { public static void main(String[] args) { } }");
        var project = JavaProjectLoader.Load(root)[0];
        JavaClassPath.WriteCache(root, project, ["/old.jar"]);
        File.SetLastWriteTimeUtc(Path.Combine(root, "pom.xml"), DateTime.UtcNow.AddMinutes(1));
        var asked = new List<string>();
        var classPath = new JavaClassPath((start, _, _) =>
        {
            asked.AddRange(start.ArgumentList);
            var output = start.ArgumentList.Single(a => a.StartsWith("-Dmdep.outputFile=", StringComparison.Ordinal))["-Dmdep.outputFile=".Length..];
            File.WriteAllText(output, "/new.jar");
            return Task.FromResult(0);
        });

        var plan = await new JavaApplicationType(null, classPath).PrepareAsync(Configuration("App"), new RunContext(root, RunMode.Run), CancellationToken.None);

        Assert.Contains("dependency:build-classpath", asked);
        Assert.Equal(string.Join(Path.PathSeparator, Path.Combine(root, "target", "classes"), "/new.jar"), plan.Arguments[1]);
        Assert.Contains("/new.jar", File.ReadAllText(JavaClassPath.CacheFile(root, project)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunsGradleProjectsFromTheirClassesAndResources()
    {
        Write("settings.gradle", "rootProject.name = 'demo'");
        Write("build.gradle", "plugins { id 'java' }");
        Write("src/main/java/demo/App.java", "package demo; public class App { public static void main(String[] args) { } }");
        var project = JavaProjectLoader.Load(root)[0];
        JavaClassPath.WriteCache(root, project, ["/cache/lib.jar"]);

        var plan = await Type().PrepareAsync(Configuration("demo.App"), new RunContext(root, RunMode.Run), CancellationToken.None);

        var expected = string.Join(Path.PathSeparator, Path.Combine(root, "build", "classes", "java", "main"), Path.Combine(root, "build", "resources", "main"), "/cache/lib.jar");
        Assert.Equal(["-cp", expected, "demo.App"], plan.Arguments);
    }

    [Fact]
    public async Task RunsModulesFromTheModulePath()
    {
        Write("src/Main.java", "class Main { public static void main(String[] args) { } }");

        var plan = await Type().PrepareAsync(Configuration("app.Main", ("module", "app")), new RunContext(root, RunMode.Run), CancellationToken.None);

        Assert.Equal(["--module-path", JavaClassPath.PlainClasses(root), "--module", "app/app.Main"], plan.Arguments);
    }

    [Fact]
    public async Task OffersTheModuleOnlyForModularProjects()
    {
        Write("src/demo/Main.java", "package demo; class Main { public static void main(String[] args) { } }");

        var plain = await Type().GetOptionsAsync(root, CancellationToken.None);
        Write("src/module-info.java", "module demo { }");
        var modular = await Type().GetOptionsAsync(root, CancellationToken.None);

        Assert.DoesNotContain(plain.Options, o => o.Id == JavaApplicationType.Module);
        Assert.Contains(modular.Options, o => o.Id == JavaApplicationType.Module);
    }

    [Fact]
    public async Task DebugPlansStartTheJvmWithTheJdwpAgent()
    {
        Write("src/Main.java", "class Main { public static void main(String[] args) { } }");
        var configuration = Configuration("Main", ("arguments", "a"), ("vmOptions", "-ea"), ("environment", "X=1"), ("jdk", "/jdk"));

        var plan = await Type().PrepareAsync(configuration, new RunContext(root, RunMode.Debug), CancellationToken.None);

        Assert.Equal(JavaRunSupport.JdwpAgent, plan.Arguments[0]);
        var debug = Assert.IsType<DebugLaunch>(plan.Debug);
        Assert.Equal("jdwp", debug.Debugger);
        Assert.Equal("Main", debug.Settings["mainClass"]);
        Assert.Equal(JavaClassPath.PlainClasses(root), debug.Settings["classPath"]);
        Assert.Equal($"{JavaRunSupport.JdwpAgent}\n-ea", debug.Settings["vmOptions"]);
        Assert.Equal("a", debug.Settings["args"]);
        Assert.Equal(root, debug.Settings["cwd"]);
        Assert.Contains("X=1", debug.Settings["env"].Split('\n'));
        Assert.Equal(plan.Program, debug.Settings["java"]);
    }

    [Fact]
    public async Task CompilesAndRunsAPlainFolderWithTheJdk()
    {
        if (FindOnPath("javac") is null)
            Assert.Skip("javac is not on the PATH.");

        Write("src/hello/Main.java", """
            package hello;
            public class Main {
                public static void main(String[] args) {
                    System.out.println("Hello from " + args[0]);
                }
            }
            """);
        var type = Type();
        var configuration = Assert.Single(await type.DetectAsync(root, CancellationToken.None));
        configuration.Values.Set(JavaRunSupport.Arguments, "Runesmith");

        var build = await type.BuildAsync(configuration, Context(), CancellationToken.None);
        Assert.True(build?.Succeeded);
        var plan = await type.PrepareAsync(configuration, new RunContext(root, RunMode.Run), TestContext.Current.CancellationToken);

        var start = new ProcessStartInfo(plan.Program) { WorkingDirectory = plan.WorkingDirectory, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var argument in plan.Arguments)
            start.ArgumentList.Add(argument);
        foreach (var (key, value) in plan.Environment)
            start.Environment[key] = value;
        using var process = Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, process.ExitCode);
        Assert.Equal("Hello from Runesmith", output.Trim());
    }

    [Fact]
    public async Task ReportsCompilerErrors()
    {
        if (FindOnPath("javac") is null)
            Assert.Skip("javac is not on the PATH.");

        Write("src/Broken.java", "class Broken { public static void main(String[] args) { int x = \"no\"; } }");
        var reported = new List<Diagnostic>();

        var build = await Type().BuildAsync(Configuration("Broken"), Context(reported.Add), CancellationToken.None);

        Assert.False(build?.Succeeded);
        var error = Assert.Single(reported);
        Assert.Equal(Path.Combine(root, "src", "Broken.java"), error.FilePath);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private const string Pom = """
        <project>
          <modelVersion>4.0.0</modelVersion>
          <groupId>com.example</groupId>
          <artifactId>demo</artifactId>
          <version>1.0</version>
        </project>
        """;

    private static JavaApplicationType Type() =>
        new(null, new JavaClassPath((_, _, _) => throw new InvalidOperationException("Tests never run Maven or Gradle.")));

    private Task<IReadOnlyList<RunConfiguration>> Detect() => Type().DetectAsync(root, CancellationToken.None);

    private static RunConfiguration Configuration(string mainClass, params (string Id, string Value)[] values)
    {
        var options = JavaApplicationType.CreateOptions(modular: false).Defaults();
        options.Set(JavaApplicationType.MainClass, mainClass);
        foreach (var (id, value) in values)
            options.Set(id, value);
        return new RunConfiguration(JavaApplicationType.TypeId, mainClass, options);
    }

    private BuildContext Context(Action<Diagnostic>? report = null) => new(root, new Channel(), report ?? (_ => { }));

    private void Write(string relative, string text)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static string? FindOnPath(string tool) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => Path.Combine(folder, OperatingSystem.IsWindows() ? tool + ".exe" : tool))
            .FirstOrDefault(File.Exists);

    private sealed class Channel : IOutputChannel
    {
        public string Name => "test";

        public void Append(string text)
        {
        }

        public void AppendLine(string line)
        {
        }

        public void Clear()
        {
        }

        public void Show()
        {
        }
    }
}
