using System.IO.Compression;

namespace Runesmith.Languages.Java.Tests.Projects;

/// <summary>A temporary folder with a project, a local Maven repository and a Gradle cache, so tests never read the real home folder.</summary>
internal sealed class FakeRepositories : IDisposable
{
    public FakeRepositories()
    {
        Root = Directory.CreateTempSubdirectory("runesmith-projects-").FullName;
        Project = Directory.CreateDirectory(Path.Combine(Root, "project")).FullName;
        Maven = Directory.CreateDirectory(Path.Combine(Root, "m2")).FullName;
        Gradle = Directory.CreateDirectory(Path.Combine(Root, "gradle-cache")).FullName;
    }

    public string Root { get; }

    public string Project { get; }

    public string Maven { get; }

    public string Gradle { get; }

    public Runesmith.Languages.Java.Projects.JavaProjectLoaderOptions Options => new() { MavenRepository = Maven, GradleCache = Gradle, DefaultRelease = 25 };

    /// <summary>Writes a file under the project folder.</summary>
    public string Write(string relativePath, string text)
    {
        var path = Path.Combine(Project, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>Adds a library to the local Maven repository: its POM, with the given dependencies, and an empty JAR.</summary>
    public string AddMavenArtifact(string group, string artifact, string version, string dependencies = "", string extra = "")
    {
        var folder = Path.Combine([Maven, .. group.Split('.'), artifact, version]);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, $"{artifact}-{version}.pom"), Pom(group, artifact, version, dependencies, extra));
        var jar = Path.Combine(folder, $"{artifact}-{version}.jar");
        using (ZipFile.Open(jar, ZipArchiveMode.Create))
        {
        }

        return jar;
    }

    /// <summary>Adds a library to the Gradle cache, in a hash folder per file as Gradle stores them.</summary>
    public string AddGradleArtifact(string group, string artifact, string version, string dependencies = "")
    {
        var folder = Path.Combine(Gradle, group, artifact, version);
        var pomFolder = Directory.CreateDirectory(Path.Combine(folder, "1a2b3c")).FullName;
        var jarFolder = Directory.CreateDirectory(Path.Combine(folder, "4d5e6f")).FullName;
        File.WriteAllText(Path.Combine(pomFolder, $"{artifact}-{version}.pom"), Pom(group, artifact, version, dependencies, ""));
        var jar = Path.Combine(jarFolder, $"{artifact}-{version}.jar");
        using (ZipFile.Open(jar, ZipArchiveMode.Create))
        {
        }

        return jar;
    }

    public static string Dependency(string group, string artifact, string? version, string? scope = null, string extra = "") =>
        $"<dependency><groupId>{group}</groupId><artifactId>{artifact}</artifactId>{(version is null ? "" : $"<version>{version}</version>")}{(scope is null ? "" : $"<scope>{scope}</scope>")}{extra}</dependency>";

    public void Dispose() => Directory.Delete(Root, recursive: true);

    private static string Pom(string group, string artifact, string version, string dependencies, string extra) => $"""
        <project xmlns="http://maven.apache.org/POM/4.0.0">
          <modelVersion>4.0.0</modelVersion>
          <groupId>{group}</groupId>
          <artifactId>{artifact}</artifactId>
          <version>{version}</version>
          {extra}
          <dependencies>{dependencies}</dependencies>
        </project>
        """;
}
