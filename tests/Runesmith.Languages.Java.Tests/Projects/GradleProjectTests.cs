using Runesmith.Languages.Java.Projects;

namespace Runesmith.Languages.Java.Tests.Projects;

public sealed class GradleProjectTests : IDisposable
{
    private readonly FakeRepositories repositories = new();

    [Fact]
    public void ReadsAKotlinBuildWithAToolchainACatalogAndTransitiveLibraries()
    {
        var jackson = repositories.AddGradleArtifact("com.fasterxml.jackson.core", "jackson-databind", "2.17.0",
            FakeRepositories.Dependency("com.fasterxml.jackson.core", "jackson-core", "2.17.0"));
        var core = repositories.AddGradleArtifact("com.fasterxml.jackson.core", "jackson-core", "2.17.0");
        var junit = repositories.AddGradleArtifact("org.junit.jupiter", "junit-jupiter", "5.10.2");
        var guava = repositories.AddMavenArtifact("com.google.guava", "guava", "33.0");
        repositories.Write("settings.gradle.kts", """rootProject.name = "shop" """);
        repositories.Write("gradle.properties", "guavaVersion=33.0\n");
        repositories.Write("gradle/libs.versions.toml", """
            [versions]
            jackson = "2.17.0"
            [libraries]
            jackson-databind = { module = "com.fasterxml.jackson.core:jackson-databind", version.ref = "jackson" }
            junit-jupiter = { group = "org.junit.jupiter", name = "junit-jupiter", version = "5.10.2" }
            """);
        repositories.Write("build.gradle.kts", """
            plugins { java }
            java { toolchain { languageVersion = JavaLanguageVersion.of(21) } }
            dependencies {
                implementation(libs.jackson.databind)
                implementation("com.google.guava:guava:${guavaVersion}")
                testImplementation(libs.junit.jupiter)
                implementation("org.unknown:computed:$someVersion")
            }
            tasks.withType<JavaCompile> { options.compilerArgs.add("--enable-preview") }
            """);
        repositories.Write("src/main/java/shop/Shop.java", "");

        var project = Assert.Single(JavaProjectLoader.Load(repositories.Project, repositories.Options));

        Assert.Equal("shop", project.Name);
        Assert.Equal(JavaProjectKind.Gradle, project.Kind);
        Assert.Equal(21, project.Release);
        Assert.True(project.PreviewEnabled);
        Assert.Equal([guava, jackson, junit, core], project.ClassPathJars);
    }

    [Fact]
    public void ReadsAGroovyMultiProjectBuild()
    {
        repositories.Write("settings.gradle", "rootProject.name = 'store'\ninclude 'api', 'services:billing'\n");
        repositories.Write("build.gradle", "subprojects { apply plugin: 'java' }\njava { sourceCompatibility = JavaVersion.VERSION_17 }\n");
        repositories.Write("api/build.gradle", "dependencies { implementation group: 'org.slf4j', name: 'slf4j-api', version: '2.0.9' }");
        repositories.Write("api/src/main/java/Api.java", "");
        repositories.Write("services/billing/build.gradle", """
            java { sourceCompatibility = '1.8' }
            sourceSets { main { java { srcDirs = ['src'] } } }
            dependencies { implementation project(':api') }
            """);
        repositories.Write("services/billing/src/Billing.java", "");
        var slf4j = repositories.AddMavenArtifact("org.slf4j", "slf4j-api", "2.0.9");

        var projects = JavaProjectLoader.Load(repositories.Project, repositories.Options).ToDictionary(p => p.Name);

        Assert.Equal(["api", "billing"], projects.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(17, projects["api"].Release);
        Assert.Equal([slf4j], projects["api"].ClassPathJars);
        Assert.Equal(8, projects["billing"].Release);
        Assert.Equal(["api"], projects["billing"].ProjectReferences);
        Assert.Contains(Path.Combine(repositories.Project, "services", "billing", "src"), projects["billing"].SourceRoots);
    }

    [Theory]
    [InlineData("java { toolchain { languageVersion.set(JavaLanguageVersion.of(17)) } }", 17)]
    [InlineData("tasks.compileJava { options.release = 11 }", 11)]
    [InlineData("tasks.compileJava { options.release.set(21) }", 21)]
    [InlineData("sourceCompatibility = 1.8", 8)]
    [InlineData("sourceCompatibility = JavaVersion.VERSION_1_8", 8)]
    [InlineData("plugins { java }", null)]
    public void ReadsTheReleaseFormsOfBuildScripts(string script, int? expected) => Assert.Equal(expected, GradleProjectReader.ReadRelease(script));

    [Fact]
    public void TreatsAFolderWithoutBuildFilesAsOneProject()
    {
        repositories.Write("Main.java", "class Main {}");

        var project = Assert.Single(JavaProjectLoader.Load(repositories.Project, repositories.Options));

        Assert.Equal(JavaProjectKind.Folder, project.Kind);
        Assert.Equal([repositories.Project], project.SourceRoots);
        Assert.Equal(25, project.Release);
    }

    public void Dispose() => repositories.Dispose();
}
