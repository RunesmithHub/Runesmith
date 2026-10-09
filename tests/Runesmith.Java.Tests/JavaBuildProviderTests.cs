namespace Runesmith.Java.Tests;

public sealed class JavaBuildProviderTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("runesmith-java-build-").FullName;

    [Fact]
    public void BuildsMavenProjectsWithMvn()
    {
        File.WriteAllText(Path.Combine(root, "pom.xml"), "<project />");

        var tool = JavaBuildProvider.FindTool(root);

        Assert.NotNull(tool);
        Assert.Equal("Maven", tool.Name);
        Assert.Equal(["-q", "compile"], tool.Arguments.TakeLast(2));
    }

    [Fact]
    public void PrefersTheGradleWrapper()
    {
        File.WriteAllText(Path.Combine(root, "build.gradle.kts"), "plugins { java }");
        File.WriteAllText(Path.Combine(root, OperatingSystem.IsWindows() ? "gradlew.cmd" : "gradlew"), "");

        var tool = JavaBuildProvider.FindTool(root);

        Assert.NotNull(tool);
        Assert.Equal("Gradle", tool.Name);
        Assert.Contains("gradlew", tool.Display, StringComparison.Ordinal);
        Assert.Equal("compileJava", tool.Arguments[^1]);
    }

    [Fact]
    public void CannotBuildFoldersWithoutABuild() => Assert.False(new JavaBuildProvider().CanBuild(root));

    [Fact]
    public void TheLanguageCoversJavaFiles()
    {
        var language = new JavaLanguage().Definition;

        Assert.Equal("java", language.Id);
        Assert.Equal([".java"], language.Extensions);
        Assert.Equal("source.java", language.ScopeName);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
