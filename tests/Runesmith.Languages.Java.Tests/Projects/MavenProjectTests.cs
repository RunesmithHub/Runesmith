using Runesmith.Languages.Java.Projects;

namespace Runesmith.Languages.Java.Tests.Projects;

public sealed class MavenProjectTests : IDisposable
{
    private readonly FakeRepositories repositories = new();

    [Fact]
    public void ReadsTheReleaseSourcesAndPreviewFlag()
    {
        repositories.Write("pom.xml", """
            <project>
              <groupId>com.example</groupId><artifactId>app</artifactId><version>1.0</version>
              <properties><java.version>21</java.version><maven.compiler.release>${java.version}</maven.compiler.release></properties>
              <build><plugins><plugin><artifactId>maven-compiler-plugin</artifactId>
                <configuration><compilerArgs><arg>--enable-preview</arg></compilerArgs></configuration>
              </plugin></plugins></build>
            </project>
            """);
        repositories.Write("src/main/java/com/example/App.java", "");
        repositories.Write("src/test/java/com/example/AppTest.java", "");

        var project = Assert.Single(JavaProjectLoader.Load(repositories.Project, repositories.Options));

        Assert.Equal("app", project.Name);
        Assert.Equal(JavaProjectKind.Maven, project.Kind);
        Assert.Equal(21, project.Release);
        Assert.True(project.PreviewEnabled);
        Assert.Equal([Path.Combine(repositories.Project, "src", "main", "java"), Path.Combine(repositories.Project, "src", "test", "java")], project.SourceRoots);
    }

    [Theory]
    [InlineData("<maven.compiler.source>1.8</maven.compiler.source>", 8)]
    [InlineData("<maven.compiler.target>11</maven.compiler.target>", 11)]
    [InlineData("", 25)]
    public void ReadsOlderReleasePropertiesAndFallsBackToTheDefault(string properties, int expected)
    {
        repositories.Write("pom.xml", $"<project><groupId>g</groupId><artifactId>a</artifactId><version>1</version><properties>{properties}</properties></project>");

        Assert.Equal(expected, Assert.Single(JavaProjectLoader.Load(repositories.Project, repositories.Options)).Release);
    }

    [Fact]
    public void ResolvesDependenciesTransitivelyWithScopesExclusionsAndNearestWins()
    {
        var guava = repositories.AddMavenArtifact("com.google.guava", "guava", "33.0",
            FakeRepositories.Dependency("com.google.guava", "failureaccess", "1.0") + FakeRepositories.Dependency("org.checkerframework", "checker", "3.0")
            + FakeRepositories.Dependency("junit", "junit", "4.13", "test") + FakeRepositories.Dependency("org.optional", "opt", "1.0", extra: "<optional>true</optional>"));
        var failureAccess = repositories.AddMavenArtifact("com.google.guava", "failureaccess", "1.0");
        repositories.AddMavenArtifact("org.checkerframework", "checker", "3.0");
        repositories.AddMavenArtifact("junit", "junit", "4.13");
        repositories.AddMavenArtifact("org.optional", "opt", "1.0");
        var slf4jNear = repositories.AddMavenArtifact("org.slf4j", "slf4j-api", "2.0");
        repositories.AddMavenArtifact("org.slf4j", "slf4j-api", "1.7");
        var logback = repositories.AddMavenArtifact("ch.qos.logback", "logback", "1.5", FakeRepositories.Dependency("org.slf4j", "slf4j-api", "1.7"));
        var junitJupiter = repositories.AddMavenArtifact("org.junit", "jupiter", "5.10");
        repositories.Write("pom.xml", $"""
            <project><groupId>g</groupId><artifactId>a</artifactId><version>1</version>
              <dependencies>
                {FakeRepositories.Dependency("com.google.guava", "guava", "33.0", extra: "<exclusions><exclusion><groupId>org.checkerframework</groupId><artifactId>checker</artifactId></exclusion></exclusions>")}
                {FakeRepositories.Dependency("org.slf4j", "slf4j-api", "2.0")}
                {FakeRepositories.Dependency("ch.qos.logback", "logback", "1.5")}
                {FakeRepositories.Dependency("org.junit", "jupiter", "5.10", "test")}
                {FakeRepositories.Dependency("org.missing", "missing", "1.0")}
              </dependencies>
            </project>
            """);

        var jars = Assert.Single(JavaProjectLoader.Load(repositories.Project, repositories.Options)).ClassPathJars;

        Assert.Equal([guava, slf4jNear, logback, junitJupiter, failureAccess], jars);
    }

    [Fact]
    public void InheritsFromParentsAndManagesVersionsWithImportedBoms()
    {
        repositories.AddMavenArtifact("com.example", "bom", "2.0", extra: $"""
            <packaging>pom</packaging>
            <dependencyManagement><dependencies>{FakeRepositories.Dependency("com.example", "lib", "2.5")}</dependencies></dependencyManagement>
            """);
        var lib = repositories.AddMavenArtifact("com.example", "lib", "2.5");
        var commons = repositories.AddMavenArtifact("org.apache.commons", "commons-lang3", "3.14");
        repositories.Write("pom.xml", $"""
            <project><groupId>com.example</groupId><artifactId>parent</artifactId><version>1.0</version><packaging>pom</packaging>
              <properties><maven.compiler.release>17</maven.compiler.release><commons.version>3.14</commons.version></properties>
              <dependencyManagement><dependencies>
                {FakeRepositories.Dependency("org.apache.commons", "commons-lang3", "${commons.version}")}
                {FakeRepositories.Dependency("com.example", "bom", "2.0", "import", "<type>pom</type>")}
              </dependencies></dependencyManagement>
              <modules><module>core</module><module>app</module></modules>
            </project>
            """);
        repositories.Write("core/pom.xml", $"""
            <project><parent><groupId>com.example</groupId><artifactId>parent</artifactId><version>1.0</version></parent>
              <artifactId>core</artifactId>
              <dependencies>{FakeRepositories.Dependency("org.apache.commons", "commons-lang3", null)}{FakeRepositories.Dependency("com.example", "lib", null)}</dependencies>
            </project>
            """);
        repositories.Write("core/src/main/java/Core.java", "");
        repositories.Write("app/pom.xml", $"""
            <project><parent><groupId>com.example</groupId><artifactId>parent</artifactId><version>1.0</version></parent>
              <artifactId>app</artifactId>
              <build><sourceDirectory>source</sourceDirectory></build>
              <dependencies>{FakeRepositories.Dependency("com.example", "core", "${project.version}")}</dependencies>
            </project>
            """);
        repositories.Write("app/source/App.java", "");

        var projects = JavaProjectLoader.Load(repositories.Project, repositories.Options).ToDictionary(p => p.Name);

        Assert.Equal(["app", "core"], projects.Keys.Order(StringComparer.Ordinal));
        Assert.Equal([commons, lib], projects["core"].ClassPathJars);
        Assert.Equal(17, projects["core"].Release);
        Assert.Equal(["core"], projects["app"].ProjectReferences);
        Assert.Empty(projects["app"].ClassPathJars);
        Assert.Equal([Path.Combine(repositories.Project, "app", "source")], projects["app"].SourceRoots);
    }

    [Fact]
    public void ReadsAParentFromTheRepository()
    {
        repositories.AddMavenArtifact("org.springframework.boot", "starter-parent", "3.3.0", extra: """
            <packaging>pom</packaging>
            <properties><java.version>17</java.version><maven.compiler.release>${java.version}</maven.compiler.release></properties>
            """);
        repositories.Write("pom.xml", """
            <project><parent><groupId>org.springframework.boot</groupId><artifactId>starter-parent</artifactId><version>3.3.0</version><relativePath/></parent>
              <artifactId>service</artifactId><properties><java.version>21</java.version></properties></project>
            """);

        Assert.Equal(21, Assert.Single(JavaProjectLoader.Load(repositories.Project, repositories.Options)).Release);
    }

    [Fact]
    public void IgnoresAPomThatIsNotXMLAndTreatsTheFolderAsTheProject()
    {
        repositories.Write("pom.xml", "<project");

        var project = Assert.Single(JavaProjectLoader.Load(repositories.Project, repositories.Options));
        Assert.Equal(JavaProjectKind.Folder, project.Kind);
    }

    public void Dispose() => repositories.Dispose();
}
