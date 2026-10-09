using Runesmith.Java.Running;
using Runesmith.Sdk.Running;

namespace Runesmith.Java.Tests.Running;

public sealed class BuildToolTypesTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("runesmith-java-tools-").FullName;

    [Fact]
    public async Task RunsMavenGoalsWithProfiles()
    {
        File.WriteAllText(Path.Combine(root, "pom.xml"), "<project />");
        var type = new MavenGoalsType(null);

        var plan = await type.PrepareAsync(await ConfigurationAsync(type, ("goals", "clean install"), ("profiles", "dev,fast"), ("arguments", "-DskipTests")), Run(), CancellationToken.None);

        Assert.Equal(["-P", "dev,fast", "clean", "install", "-DskipTests"], plan.Arguments.TakeLast(5));
        Assert.Equal(root, plan.WorkingDirectory);
    }

    [Fact]
    public async Task PrefersTheGradleWrapper()
    {
        File.WriteAllText(Path.Combine(root, "build.gradle"), "");
        var wrapper = Path.Combine(root, OperatingSystem.IsWindows() ? "gradlew.cmd" : "gradlew");
        File.WriteAllText(wrapper, "");
        var type = new GradleTasksType(null);

        var plan = await type.PrepareAsync(await ConfigurationAsync(type, ("tasks", "clean build"), ("environment", "CI=true")), Run(), CancellationToken.None);

        Assert.Contains(wrapper, plan.Arguments.Prepend(plan.Program));
        Assert.Equal(["clean", "build"], plan.Arguments.TakeLast(2));
        Assert.Equal("true", plan.Environment["CI"]);
    }

    [Fact]
    public async Task RunsJUnitTestsWithMaven()
    {
        File.WriteAllText(Path.Combine(root, "pom.xml"), "<project />");
        var type = new JUnitTestsType(null);

        var plan = await type.PrepareAsync(await ConfigurationAsync(type, ("filter", "MathTest#adds")), Run(), CancellationToken.None);

        Assert.Equal(["test", "-Dtest=MathTest#adds", "-Dsurefire.failIfNoSpecifiedTests=false"], plan.Arguments.TakeLast(3));
    }

    [Fact]
    public async Task RunsJUnitTestsWithGradle()
    {
        File.WriteAllText(Path.Combine(root, "build.gradle.kts"), "");
        var type = new JUnitTestsType(null);

        var plan = await type.PrepareAsync(await ConfigurationAsync(type, ("filter", "demo.MathTest")), Run(), CancellationToken.None);

        Assert.Equal(["test", "--tests", "demo.MathTest"], plan.Arguments.TakeLast(3));
    }

    [Fact]
    public async Task JUnitTestsNeedABuild()
    {
        var type = new JUnitTestsType(null);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await type.PrepareAsync(await ConfigurationAsync(type), Run(), CancellationToken.None));
    }

    [Fact]
    public void SplitsWordsKeepingQuotedParts() =>
        Assert.Equal(["exec:java", "-Dexec.args=a b"], JavaRunSupport.Words("exec:java \"-Dexec.args=a b\""));

    public void Dispose() => Directory.Delete(root, recursive: true);

    private RunContext Run() => new(root, RunMode.Run);

    private async Task<RunConfiguration> ConfigurationAsync(IRunConfigurationType type, params (string Id, string Value)[] values)
    {
        var options = (await type.GetOptionsAsync(root, CancellationToken.None)).Defaults();
        foreach (var (id, value) in values)
            options.Set(id, value);
        return new RunConfiguration(type.Id, type.Name, options);
    }
}
