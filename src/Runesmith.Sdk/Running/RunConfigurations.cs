using Runesmith.Sdk.Build;
using Runesmith.Sdk.Options;

namespace Runesmith.Sdk.Running;

/// <summary>How a configuration is launched.</summary>
public enum RunMode
{
    Run,
    Debug,
}

/// <summary>What runs before a configuration launches.</summary>
public enum BeforeLaunchKind
{
    /// <summary>Build the configuration's project.</summary>
    Build,

    /// <summary>Run another configuration, by name, and wait for it to end.</summary>
    RunConfiguration,

    /// <summary>Run a command line in the folder.</summary>
    Command,
}

/// <summary>A step that runs before a configuration launches; a failing step stops the launch.</summary>
/// <param name="Kind">What the step does.</param>
/// <param name="Argument">The configuration's name or the command line, for the kinds that need one.</param>
public sealed record BeforeLaunchStep(BeforeLaunchKind Kind, string? Argument = null);

/// <summary>A named, saved way to run something: a project with arguments, tests, a build goal.</summary>
/// <param name="TypeId">The id of its <see cref="IRunConfigurationType"/>.</param>
/// <param name="Name">The name shown in the run widget.</param>
/// <param name="Values">Its option values.</param>
public sealed record RunConfiguration(string TypeId, string Name, OptionValues Values)
{
    /// <summary>Gets the steps that run before it launches; a new configuration builds first.</summary>
    public IReadOnlyList<BeforeLaunchStep> BeforeLaunch { get; init; } = [new(BeforeLaunchKind.Build)];

    /// <summary>Gets whether it is saved only for this user instead of in the folder's shared <c>.runesmith/run.json</c>.</summary>
    public bool IsLocal { get; init; }

    /// <summary>Gets whether it was detected in the folder rather than made by the user; detected ones are found again on each open.</summary>
    public bool IsDetected { get; init; }
}

/// <summary>What a debugger needs to start or attach to a program.</summary>
/// <param name="Debugger">The debugger, such as <c>dotnet</c> or <c>jdwp</c>.</param>
/// <param name="Settings">The debugger's own settings.</param>
public sealed record DebugLaunch(string Debugger, IReadOnlyDictionary<string, string> Settings);

/// <summary>How to start a configuration's program.</summary>
/// <param name="Program">The executable.</param>
/// <param name="Arguments">Its arguments, unquoted.</param>
/// <param name="WorkingDirectory">The folder it starts in.</param>
public sealed record LaunchPlan(string Program, IReadOnlyList<string> Arguments, string WorkingDirectory)
{
    /// <summary>Gets environment variables to set, besides Runesmith's own environment.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();

    /// <summary>Gets what the debugger needs, when the plan is for debugging.</summary>
    public DebugLaunch? Debug { get; init; }
}

/// <summary>What a configuration type gets when it prepares a launch.</summary>
/// <param name="RootPath">The open folder.</param>
/// <param name="Mode">Whether to run or debug.</param>
public sealed record RunContext(string RootPath, RunMode Mode);

/// <summary>A kind of run configuration, such as ".NET project" or "Maven goal". Export it with
/// <c>[Export(typeof(IRunConfigurationType))]</c>.</summary>
public interface IRunConfigurationType
{
    /// <summary>Gets a unique id, saved with each configuration.</summary>
    string Id { get; }

    /// <summary>Gets the name shown when adding a configuration.</summary>
    string Name { get; }

    /// <summary>Gets the icon's name.</summary>
    string Icon { get; }

    /// <summary>Gets a short description for the Add menu.</summary>
    string Description { get; }

    /// <summary>Gets the settings a configuration of this type has in a folder, such as a choice of the folder's projects.</summary>
    Task<OptionSet> GetOptionsAsync(string rootPath, CancellationToken cancellationToken);

    /// <summary>Gets whether configurations of this type can be debugged.</summary>
    bool CanDebug { get; }

    /// <summary>Finds the configurations a folder has without the user making them, such as one per runnable project.</summary>
    Task<IReadOnlyList<RunConfiguration>> DetectAsync(string rootPath, CancellationToken cancellationToken);

    /// <summary>Turns a configuration into the program to start.</summary>
    Task<LaunchPlan> PrepareAsync(RunConfiguration configuration, RunContext context, CancellationToken cancellationToken);

    /// <summary>Builds what the configuration runs, or returns null to build the whole folder with its build provider.</summary>
    Task<BuildResult?> BuildAsync(RunConfiguration configuration, BuildContext context, CancellationToken cancellationToken);
}
