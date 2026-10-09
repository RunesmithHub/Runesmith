using System.Composition;
using Runesmith.Sdk.Build;
using Runesmith.Sdk.Options;
using Runesmith.Sdk.Running;
using Runesmith.Sdk.Sdks;

namespace Runesmith.Java.Running;

/// <summary>Runs Maven goals in the open folder, with its <c>mvnw</c> when it has one.</summary>
[Export(typeof(IRunConfigurationType))]
[Shared]
[method: ImportingConstructor]
public sealed class MavenGoalsType([Import(AllowDefault = true)] ISdkService? sdks) : IRunConfigurationType
{
    /// <summary>The type's id.</summary>
    public const string TypeId = "java.maven";

    internal const string Goals = "goals";
    internal const string Profiles = "profiles";

    public string Id => TypeId;

    public string Name => "Maven goals";

    public string Icon => "package";

    public string Description => "Run Maven goals, such as clean install, with profiles.";

    public bool CanDebug => false;

    public Task<OptionSet> GetOptionsAsync(string rootPath, CancellationToken cancellationToken) => Task.FromResult(Options);

    private static readonly OptionSet Options = new(
    [
        new Option(Goals, "Goals", OptionKind.Text) { IsRequired = true, Default = "package", Placeholder = "clean install" },
        new Option(Profiles, "Profiles", OptionKind.Text) { Placeholder = "dev,fast", Description = "The profiles to activate, separated by commas." },
        JavaRunSupport.ArgumentsOption("Arguments", "More arguments for Maven, one per line, such as -DskipTests."),
        JavaRunSupport.JdkOption,
        JavaRunSupport.EnvironmentOption,
        JavaRunSupport.WorkingDirectoryOption,
    ]);

    public Task<IReadOnlyList<RunConfiguration>> DetectAsync(string rootPath, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RunConfiguration>>([]);

    public Task<LaunchPlan> PrepareAsync(RunConfiguration configuration, RunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(context);
        var values = configuration.Values;
        List<string> arguments = [];
        if (values.Get(Profiles) is { Length: > 0 } profiles && !string.IsNullOrWhiteSpace(profiles))
            arguments.AddRange(["-P", profiles.Trim()]);
        arguments.AddRange(JavaRunSupport.Words(values.Get(Goals)));
        arguments.AddRange(values.GetList(JavaRunSupport.Arguments));
        var tool = JavaBuildProvider.Tool(context.RootPath, "Maven", "mvnw", "mvn", arguments);
        return JavaRunSupport.ToolPlanAsync(tool, configuration, context, sdks, cancellationToken);
    }

    public Task<BuildResult?> BuildAsync(RunConfiguration configuration, BuildContext context, CancellationToken cancellationToken) =>
        Task.FromResult<BuildResult?>(new BuildResult(true, 0, 0, TimeSpan.Zero));
}

/// <summary>Runs Gradle tasks in the open folder, with its <c>gradlew</c> when it has one.</summary>
[Export(typeof(IRunConfigurationType))]
[Shared]
[method: ImportingConstructor]
public sealed class GradleTasksType([Import(AllowDefault = true)] ISdkService? sdks) : IRunConfigurationType
{
    /// <summary>The type's id.</summary>
    public const string TypeId = "java.gradle";

    internal const string Tasks = "tasks";

    public string Id => TypeId;

    public string Name => "Gradle tasks";

    public string Icon => "layers";

    public string Description => "Run Gradle tasks, such as build or run.";

    public bool CanDebug => false;

    public Task<OptionSet> GetOptionsAsync(string rootPath, CancellationToken cancellationToken) => Task.FromResult(Options);

    private static readonly OptionSet Options = new(
    [
        new Option(Tasks, "Tasks", OptionKind.Text) { IsRequired = true, Default = "build", Placeholder = "clean build" },
        JavaRunSupport.ArgumentsOption("Arguments", "More arguments for Gradle, one per line, such as --info."),
        JavaRunSupport.JdkOption,
        JavaRunSupport.EnvironmentOption,
        JavaRunSupport.WorkingDirectoryOption,
    ]);

    public Task<IReadOnlyList<RunConfiguration>> DetectAsync(string rootPath, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RunConfiguration>>([]);

    public Task<LaunchPlan> PrepareAsync(RunConfiguration configuration, RunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(context);
        List<string> arguments = [.. JavaRunSupport.Words(configuration.Values.Get(Tasks)), .. configuration.Values.GetList(JavaRunSupport.Arguments)];
        var tool = JavaBuildProvider.Tool(context.RootPath, "Gradle", "gradlew", "gradle", arguments);
        return JavaRunSupport.ToolPlanAsync(tool, configuration, context, sdks, cancellationToken);
    }

    public Task<BuildResult?> BuildAsync(RunConfiguration configuration, BuildContext context, CancellationToken cancellationToken) =>
        Task.FromResult<BuildResult?>(new BuildResult(true, 0, 0, TimeSpan.Zero));
}

/// <summary>Runs a project's JUnit tests through its build: <c>mvn test -Dtest=...</c> or <c>gradle test --tests ...</c>.</summary>
[Export(typeof(IRunConfigurationType))]
[Shared]
[method: ImportingConstructor]
public sealed class JUnitTestsType([Import(AllowDefault = true)] ISdkService? sdks) : IRunConfigurationType
{
    /// <summary>The type's id.</summary>
    public const string TypeId = "java.junit";

    internal const string Filter = "filter";

    public string Id => TypeId;

    public string Name => "JUnit tests";

    public string Icon => "check-circle";

    public string Description => "Run the JUnit tests of a Maven or Gradle project, or the ones a filter picks.";

    public bool CanDebug => false;

    public Task<OptionSet> GetOptionsAsync(string rootPath, CancellationToken cancellationToken) => Task.FromResult(Options);

    private static readonly OptionSet Options = new(
    [
        new Option(Filter, "Tests", OptionKind.Text)
        {
            Placeholder = "All tests",
            Description = "A test class, a method such as MathTest#adds, or a pattern such as *IntegrationTest; empty runs every test.",
        },
        JavaRunSupport.ArgumentsOption("Arguments", "More arguments for the build tool, one per line."),
        JavaRunSupport.JdkOption,
        JavaRunSupport.EnvironmentOption,
        JavaRunSupport.WorkingDirectoryOption,
    ]);

    public Task<IReadOnlyList<RunConfiguration>> DetectAsync(string rootPath, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RunConfiguration>>([]);

    public Task<LaunchPlan> PrepareAsync(RunConfiguration configuration, RunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(context);
        var filter = configuration.Values.Get(Filter)?.Trim();
        var extra = configuration.Values.GetList(JavaRunSupport.Arguments);
        var build = JavaBuildProvider.FindTool(context.RootPath)
            ?? throw new InvalidOperationException("JUnit tests run through Maven or Gradle, and the folder has neither a pom.xml nor a Gradle build.");
        JavaBuildProvider.BuildTool tool = build.Name == "Maven"
            ? JavaBuildProvider.Tool(context.RootPath, "Maven", "mvnw", "mvn",
                string.IsNullOrEmpty(filter) ? ["test", .. extra] : ["test", "-Dtest=" + filter, "-Dsurefire.failIfNoSpecifiedTests=false", .. extra])
            : JavaBuildProvider.Tool(context.RootPath, "Gradle", "gradlew", "gradle",
                string.IsNullOrEmpty(filter) ? ["test", .. extra] : ["test", "--tests", filter.Replace('#', '.'), .. extra]);
        return JavaRunSupport.ToolPlanAsync(tool, configuration, context, sdks, cancellationToken);
    }

    public Task<BuildResult?> BuildAsync(RunConfiguration configuration, BuildContext context, CancellationToken cancellationToken) =>
        Task.FromResult<BuildResult?>(new BuildResult(true, 0, 0, TimeSpan.Zero));
}
