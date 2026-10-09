using System.Diagnostics;
using System.Xml.Linq;
using Runesmith.Languages.Java.Syntax;
using Runesmith.Sdk.Options;
using Runesmith.Sdk.Templates;
using static Runesmith.Java.Templates.JavaTemplateOptions;

namespace Runesmith.Java.Templates.Tests;

public sealed class JavaTemplateTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("runesmith-java-templates-").FullName;
    private readonly JavaTemplateProvider provider = new(null);

    public void Dispose() => Directory.Delete(root, recursive: true);

    public static TheoryData<JavaTemplateKind, string, string, bool, bool, bool, int> Combinations()
    {
        var data = new TheoryData<JavaTemplateKind, string, string, bool, bool, bool, int>();
        foreach (var kind in (JavaTemplateKind[])[JavaTemplateKind.Application, JavaTemplateKind.Library, JavaTemplateKind.MultiModule])
        {
            foreach (var (build, dsl) in (ValueTuple<string, string>[])[(Maven, Kotlin), (Gradle, Kotlin), (Gradle, Groovy), (None, Kotlin)])
            {
                if (kind == JavaTemplateKind.MultiModule && build == None)
                    continue;
                foreach (var release in (int[])[8, 17, 25])
                {
                    foreach (var moduleInfo in (bool[])[false, true])
                    {
                        foreach (var junit in (bool[])[false, true])
                        {
                            foreach (var sample in (bool[])[false, true])
                                data.Add(kind, build, dsl, moduleInfo, junit, sample, release);
                        }
                    }
                }
            }
        }

        return data;
    }

    public static TheoryData<JavaTemplateKind, string, bool, bool, int> Compilable()
    {
        var data = new TheoryData<JavaTemplateKind, string, bool, bool, int>();
        foreach (var kind in (JavaTemplateKind[])[JavaTemplateKind.Application, JavaTemplateKind.Library, JavaTemplateKind.MultiModule])
        {
            foreach (var build in (string[])[None, Maven, Gradle])
            {
                if (kind == JavaTemplateKind.MultiModule && build == None)
                    continue;
                foreach (var sample in (bool[])[false, true])
                {
                    data.Add(kind, build, false, sample, 8);
                    data.Add(kind, build, true, sample, 25);
                }
            }
        }

        return data;
    }

    [Fact]
    public async Task OffersTheFourTemplates()
    {
        var templates = await provider.GetTemplatesAsync(CancellationToken.None);

        Assert.Equal(["Application", "Library", "Compact source file", "Multi-module project"], templates.Select(t => t.Name));
        Assert.All(templates, t => Assert.Equal("Java", t.Language));
        Assert.Equal("runesmith.java.application", templates[0].Id);
        Assert.Equal([Maven, Gradle, None], Option(templates[0], BuildSystem).Choices.Select(c => c.Value));
        Assert.Equal([Maven, Gradle], Option(templates[3], BuildSystem).Choices.Select(c => c.Value));
        Assert.Equal("25", Option(templates[0], JavaRelease).Default);
        Assert.Equal("jdk", Option(templates[0], Jdk).SdkKind);
        Assert.Equal([Jdk, SampleCode], templates[2].Options.Options.Select(o => o.Id));
    }

    [Fact]
    public async Task ShowsTheGradleDslOnlyForGradle()
    {
        var template = (await provider.GetTemplatesAsync(CancellationToken.None))[0];
        var values = template.Options.Defaults();
        var dsl = Option(template, GradleDsl);

        Assert.False(dsl.VisibleWhen!.IsMet(values));
        values.Set(BuildSystem, Gradle);
        Assert.True(dsl.VisibleWhen.IsMet(values));
        Assert.Empty(template.Options.Validate(values));
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public async Task GeneratesValidJava(JavaTemplateKind kind, string build, string dsl, bool moduleInfo, bool junit, bool sample, int release)
    {
        var (folder, result) = await CreateAsync(kind, "Hello World", build, dsl, moduleInfo, junit, sample, release);

        var options = new JavaParseOptions { Version = new JavaVersion(release) };
        var javaFiles = Directory.GetFiles(folder, "*.java", SearchOption.AllDirectories);
        Assert.NotEmpty(javaFiles);
        foreach (var file in javaFiles)
        {
            var tree = JavaSyntaxTree.Parse(File.ReadAllText(file), options);
            Assert.True(tree.Diagnostics.IsEmpty, $"{file}: {string.Join("; ", tree.Diagnostics.Select(d => d.Message))}");
        }

        Assert.Single(result.FilesToOpen);
        Assert.True(File.Exists(result.FilesToOpen[0]));
        Assert.True(File.Exists(Path.Combine(folder, ".gitignore")));
        Assert.Equal(moduleInfo && release >= 11, javaFiles.Any(f => Path.GetFileName(f) == "module-info.java"));
        Assert.Equal(junit && build != None, javaFiles.Any(f => f.Contains($"{Path.DirectorySeparatorChar}test{Path.DirectorySeparatorChar}", StringComparison.Ordinal)));
        Assert.Contains($"{Path.DirectorySeparatorChar}com{Path.DirectorySeparatorChar}example{Path.DirectorySeparatorChar}helloworld", result.FilesToOpen[0], StringComparison.Ordinal);

        var modules = kind == JavaTemplateKind.MultiModule ? (string[])["", "lib", "app"] : [""];
        foreach (var module in modules)
        {
            var at = Path.Combine(folder, module);
            switch (build)
            {
                case Maven:
                    var pom = XDocument.Load(Path.Combine(at, "pom.xml"));
                    Assert.Equal("project", pom.Root!.Name.LocalName);
                    break;
                case Gradle when module.Length > 0:
                    Assert.True(File.Exists(Path.Combine(at, dsl == Kotlin ? "build.gradle.kts" : "build.gradle")));
                    break;
                case Gradle:
                    Assert.True(File.Exists(Path.Combine(at, dsl == Kotlin ? "settings.gradle.kts" : "settings.gradle")));
                    break;
            }
        }

        if (build == Gradle && kind != JavaTemplateKind.MultiModule)
        {
            var script = File.ReadAllText(Path.Combine(folder, dsl == Kotlin ? "build.gradle.kts" : "build.gradle"));
            Assert.Contains($"options.release = {release}", script, StringComparison.Ordinal);
            Assert.Equal(junit, script.Contains("useJUnitPlatform()", StringComparison.Ordinal));
        }

        if (build == Maven && kind != JavaTemplateKind.MultiModule)
            Assert.Contains($"<maven.compiler.release>{release}</maven.compiler.release>", File.ReadAllText(Path.Combine(folder, "pom.xml")), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Compilable))]
    public async Task CompilesWithJavac(JavaTemplateKind kind, string build, bool moduleInfo, bool sample, int release)
    {
        var javac = FindJavac();
        Assert.SkipWhen(javac is null, "javac is not on the PATH.");
        Assert.SkipWhen(JavacRelease(javac) < release, $"The javac on the PATH cannot compile for Java {release}.");

        var (folder, _) = await CreateAsync(kind, "my-app", build, Kotlin, moduleInfo, junit: false, sample, release);
        var output = Path.Combine(root, "classes");
        if (kind == JavaTemplateKind.MultiModule)
        {
            Compile(javac, release, Sources(Path.Combine(folder, "lib")), Path.Combine(output, "lib"), null, moduleInfo);
            Compile(javac, release, Sources(Path.Combine(folder, "app")), Path.Combine(output, "app"), Path.Combine(output, "lib"), moduleInfo);
        }
        else
        {
            Compile(javac, release, Sources(folder), output, null, moduleInfo);
        }
    }

    [Fact]
    public async Task CompactSourceFileCompiles()
    {
        var (folder, result) = await CreateAsync(JavaTemplateKind.CompactSourceFile, "hello world", Maven, Kotlin, false, false, true, 25);

        Assert.Equal(Path.Combine(folder, "HelloWorld.java"), result.FilesToOpen[0]);
        Assert.True(JavaSyntaxTree.Parse(File.ReadAllText(result.FilesToOpen[0])).Diagnostics.IsEmpty);
        if (FindJavac() is { } javac && JavacRelease(javac) >= 25)
            Compile(javac, 25, [result.FilesToOpen[0]], Path.Combine(root, "classes"), null, false);
    }

    [Fact]
    public async Task RefusesAFolderThatIsNotEmpty()
    {
        var template = (await provider.GetTemplatesAsync(CancellationToken.None))[0];
        Directory.CreateDirectory(Path.Combine(root, "taken"));
        File.WriteAllText(Path.Combine(root, "taken", "file.txt"), "");

        await Assert.ThrowsAsync<IOException>(() => provider.CreateAsync(template, new ProjectCreationRequest("taken", root, template.Options.Defaults()),
            new Progress<string>(), CancellationToken.None));
    }

    [Theory]
    [InlineData("My App", "my-app")]
    [InlineData("  Spaces & Symbols!! ", "spaces-symbols")]
    [InlineData("lib.core", "lib.core")]
    public void DerivesArtifactIds(string name, string expected) => Assert.Equal(expected, JavaNames.ArtifactId(name));

    [Theory]
    [InlineData("my-library", "MyLibrary")]
    [InlineData("2fast", "App2fast")]
    [InlineData("hello world", "HelloWorld")]
    public void DerivesClassNames(string name, string expected) => Assert.Equal(expected, JavaNames.ClassName(name));

    [Theory]
    [InlineData("class", "class_")]
    [InlineData("3d-viewer", "_3dviewer")]
    [InlineData("My-App", "myapp")]
    public void DerivesPackageSegments(string text, string expected) => Assert.Equal(expected, JavaNames.PackageSegment(text));

    [Theory]
    [InlineData("25.0.1", 25)]
    [InlineData("1.8.0_392", 8)]
    [InlineData("21", 21)]
    public void ReadsTheJdkMajorVersion(string version, int expected) => Assert.Equal(expected, JavaTemplateProvider.Major(version));

    private async Task<(string Folder, ProjectCreationResult Result)> CreateAsync(JavaTemplateKind kind, string name, string build, string dsl, bool moduleInfo,
        bool junit, bool sample, int release)
    {
        var template = (await provider.GetTemplatesAsync(CancellationToken.None)).Single(t => Equals(t.Data, kind));
        var values = template.Options.Defaults();
        values.Set(BuildSystem, build);
        values.Set(GradleDsl, dsl);
        values.Set(ModuleInfo, moduleInfo ? "true" : "false");
        values.Set(JUnit, junit ? "true" : "false");
        values.Set(SampleCode, sample ? "true" : "false");
        values.Set(JavaRelease, release.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Empty(template.Options.Validate(values));

        var location = Directory.CreateDirectory(Path.Combine(root, Guid.NewGuid().ToString("N")[..8])).FullName;
        var result = await provider.CreateAsync(template, new ProjectCreationRequest(name, location, values), new Progress<string>(), CancellationToken.None);
        Assert.Equal(Path.Combine(location, name), result.Folder);
        return (result.Folder, result);
    }

    private static Option Option(ProjectTemplate template, string id) => template.Options.Options.Single(o => o.Id == id);

    private static string[] Sources(string folder) =>
        [.. Directory.GetFiles(folder, "*.java", SearchOption.AllDirectories).Where(f => !f.Contains($"{Path.DirectorySeparatorChar}test{Path.DirectorySeparatorChar}", StringComparison.Ordinal))];

    private static void Compile(string javac, int release, IReadOnlyList<string> sources, string output, string? dependency, bool moduleInfo)
    {
        var start = new ProcessStartInfo(javac) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in (string[])["--release", release.ToString(System.Globalization.CultureInfo.InvariantCulture), "-d", output])
            start.ArgumentList.Add(argument);
        if (dependency is not null)
        {
            start.ArgumentList.Add(moduleInfo && release >= 11 ? "-p" : "-cp");
            start.ArgumentList.Add(dependency);
        }

        foreach (var source in sources)
            start.ArgumentList.Add(source);
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync();
        var text = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, text + errors.Result);
    }

    // javac -version prints "javac 21.0.2", or "javac 1.8.0_462" for Java 8.
    private static int JavacRelease(string javac)
    {
        var start = new ProcessStartInfo(javac, "-version") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using var process = Process.Start(start)!;
        var text = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        var version = text.Trim().Split(' ').LastOrDefault() ?? "";
        var parts = version.Split('.', '_', '-', '+');
        return int.TryParse(parts[0], out var first) ? first == 1 && parts.Length > 1 && int.TryParse(parts[1], out var second) ? second : first : 0;
    }

    private static string? FindJavac()
    {
        var name = OperatingSystem.IsWindows() ? "javac.exe" : "javac";
        return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => Path.Combine(folder, name))
            .FirstOrDefault(File.Exists);
    }
}
