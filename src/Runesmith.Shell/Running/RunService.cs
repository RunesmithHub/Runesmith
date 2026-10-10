using System.Collections.ObjectModel;
using System.Composition;
using System.Diagnostics;
using System.Globalization;
using Runesmith.Sdk.Build;
using Runesmith.Sdk.Running;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.Tasks;
using Runesmith.Sdk.Workspace;
using Runesmith.Shell.Editors;

namespace Runesmith.Shell.Running;

/// <summary>Runs configurations: their before-launch steps in order, then their program, each run in a <see cref="RunSession"/>; and builds
/// the selected configuration for the Build command.</summary>
[Export]
[Shared]
public sealed class RunService
{
    private const string BuildSource = "build";
    private const string BuildChannel = "Build";

    private readonly IRunConfigurationService configurations;
    private readonly IBuildService build;
    private readonly IWorkspace workspace;
    private readonly Func<Task> saveAll;
    private readonly IOutputService output;
    private readonly IDiagnosticService diagnostics;
    private readonly INotificationService notifications;
    private readonly IBackgroundTasks? tasks;
    private readonly Func<Debugging.IRunDebugger?> debugger;
    private CancellationTokenSource? building;

    /// <summary>Creates the service.</summary>
    [ImportingConstructor]
    public RunService(IRunConfigurationService configurations, IBuildService build, IWorkspace workspace, Lazy<EditorService> editors,
        IOutputService output, IDiagnosticService diagnostics, INotificationService notifications, [Import(AllowDefault = true)] IBackgroundTasks? tasks,
        [Import(AllowDefault = true)] Lazy<Debugging.IRunDebugger>? debugger)
        : this(configurations, build, workspace, () => editors.Value.SaveAllAsync(), output, diagnostics, notifications, tasks, () => debugger?.Value)
    {
    }

    internal RunService(IRunConfigurationService configurations, IBuildService build, IWorkspace workspace, Func<Task> saveAll,
        IOutputService output, IDiagnosticService diagnostics, INotificationService notifications, IBackgroundTasks? tasks, Func<Debugging.IRunDebugger?>? debugger = null)
    {
        this.configurations = configurations;
        this.build = build;
        this.workspace = workspace;
        this.saveAll = saveAll;
        this.output = output;
        this.diagnostics = diagnostics;
        this.notifications = notifications;
        this.tasks = tasks;
        this.debugger = debugger ?? (() => null);
        build.StateChanged += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Gets the runs, one per tab; a new run of a configuration replaces its finished one.</summary>
    public ObservableCollection<RunSession> Sessions { get; } = [];

    /// <summary>Gets whether a configuration builds now, for the Build command, or the folder does.</summary>
    public bool IsBuilding => building is not null || build.IsBuilding;

    /// <summary>Gets whether any run's program or steps are still going.</summary>
    public bool IsRunning => Sessions.Any(s => s.State != RunState.Finished);

    /// <summary>Raised when a run starts, and when one ends or a build starts or ends.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Raised when a run starts, so the Run tool window can show its tab.</summary>
    public event EventHandler<RunSession>? SessionStarted;

    /// <summary>Gets the run of a configuration that has not finished, or null.</summary>
    public RunSession? FindActive(string configurationName) =>
        Sessions.LastOrDefault(s => s.Configuration.Name == configurationName && s.State != RunState.Finished);

    /// <summary>Runs a configuration; when it runs already, asks whether to stop it and run it again.</summary>
    /// <returns>The run, which goes on until its <see cref="RunSession.Completion"/> ends, or null when the user kept the running one or no folder is
    /// open.</returns>
    public Task<RunSession?> RunAsync(RunConfiguration configuration, RunMode mode = RunMode.Run) => RunAsync(configuration, mode, []);

    /// <summary>Removes a finished run's tab.</summary>
    public void Remove(RunSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.Stop();
        Sessions.Remove(session);
        if (session.State == RunState.Finished)
            session.Dispose();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Builds a configuration with its type's own build, or the whole folder when the type has none, showing the log in the Output
    /// panel; without a configuration it builds the folder.</summary>
    /// <returns>The outcome, or null when nothing could build or a build runs already.</returns>
    public async Task<BuildResult?> BuildAsync(RunConfiguration? configuration)
    {
        if (configuration is null || configurations.FindType(configuration.TypeId) is not { } type || workspace.RootPath is not { } root)
            return await build.BuildAsync().ConfigureAwait(true);
        if (IsBuilding)
            return null;

        await saveAll().ConfigureAwait(true);
        var channel = output.GetChannel(BuildChannel);
        channel.Clear();
        channel.Show();
        using var cancel = new CancellationTokenSource();
        building = cancel;
        StateChanged?.Invoke(this, EventArgs.Empty);
        BuildResult? result;
        try
        {
            using var task = tasks?.Start($"Building {configuration.Name}", cancel.Cancel);
            result = await BuildWithTypeAsync(type, configuration, root, channel, cancel.Token).ConfigureAwait(true);
        }
        finally
        {
            building = null;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        if (result is null)
            return await build.BuildAsync().ConfigureAwait(true);

        if (!cancel.IsCancellationRequested)
        {
            var kind = result.Succeeded ? (result.Warnings > 0 ? NotificationKind.Warning : NotificationKind.Success) : NotificationKind.Error;
            var detail = result.Succeeded && result.Warnings == 0
                ? $"{configuration.Name} built in {Seconds(result.Duration)} s."
                : $"{Count(result.Errors, "error")} and {Count(result.Warnings, "warning")}.";
            notifications.Notify(kind, result.Succeeded ? "Build succeeded" : "Build failed", detail);
        }

        return result;
    }

    /// <summary>Stops the running build of a configuration.</summary>
    public void CancelBuild()
    {
        building?.Cancel();
        build.Cancel();
    }

    private async Task<RunSession?> RunAsync(RunConfiguration configuration, RunMode mode, IReadOnlyList<string> callers)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (workspace.RootPath is not { } root)
            return null;

        var type = configurations.FindType(configuration.TypeId);
        if (FindActive(configuration.Name) is { } active)
        {
            if (!await notifications.ConfirmAsync($"{configuration.Name} is running", $"Stop {configuration.Name} and run it again?", "Stop and Rerun").ConfigureAwait(true))
                return null;

            active.Stop();
            await active.Completion.ConfigureAwait(true);
        }

        configurations.MarkRun(configuration);
        var session = new RunSession(configuration, type, mode);
        var index = Sessions.ToList().FindIndex(s => s.Configuration.Name == configuration.Name && s.State == RunState.Finished);
        if (index >= 0)
        {
            var old = Sessions[index];
            Sessions[index] = session;
            old.Dispose();
        }
        else
            Sessions.Add(session);
        session.StateChanged += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
        SessionStarted?.Invoke(this, session);
        StateChanged?.Invoke(this, EventArgs.Empty);

        _ = ExecuteAsync(session, root, [.. callers, configuration.Name]);
        return session;
    }

    private async Task ExecuteAsync(RunSession session, string root, IReadOnlyList<string> chain)
    {
        using var task = tasks?.Start($"Running {session.Configuration.Name}", session.Stop);
        var configuration = session.Configuration;
        var console = session.Console;
        var token = session.StopToken;
        if (session.Type is not { } type)
        {
            console.AppendLine($"No plugin provides the configuration type {configuration.TypeId}; turn on the plugin that adds it.", ConsoleSource.Error);
            session.Finish(null);
            return;
        }

        try
        {
            await saveAll().ConfigureAwait(true);
            foreach (var step in configuration.BeforeLaunch)
            {
                if (!await RunStepAsync(session, type, step, root, chain).ConfigureAwait(true) || token.IsCancellationRequested)
                {
                    if (!token.IsCancellationRequested)
                        console.AppendLine($"{Describe(step)} failed, so {configuration.Name} did not start.", ConsoleSource.Error);
                    session.Finish(null);
                    return;
                }
            }

            var plan = await Task.Run(() => type.PrepareAsync(configuration, new RunContext(root, session.Mode), token), token).ConfigureAwait(true);
            session.WorkingDirectory = plan.WorkingDirectory;
            console.AppendLine(CommandLine(Path.GetFileName(plan.Program), plan.Arguments), ConsoleSource.System);
            if (session.Mode == RunMode.Debug && debugger() is { } engine)
            {
                var debugged = await engine.DebugAsync(session, plan, token).ConfigureAwait(true);
                if (session.State == RunState.Running)
                {
                    console.AppendLine(session.WasStopped
                        ? $"Stopped after {Seconds(session.Elapsed)} s."
                        : debugged is { } code
                            ? string.Create(CultureInfo.CurrentCulture, $"Process finished with exit code {code} in {Seconds(session.Elapsed)} s.")
                            : $"Debugging ended after {Seconds(session.Elapsed)} s.", ConsoleSource.System);
                }

                session.Finish(debugged);
                return;
            }

            var process = RunProcess.Start(plan, console.Append);
            session.Started(process);
            var exitCode = await process.Exited.ConfigureAwait(true);
            console.AppendLine(process.WasStopped
                ? $"Stopped after {Seconds(session.Elapsed)} s."
                : string.Create(CultureInfo.CurrentCulture, $"Process finished with exit code {exitCode} in {Seconds(session.Elapsed)} s."), ConsoleSource.System);
            session.Finish(exitCode);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            console.AppendLine("Stopped.", ConsoleSource.System);
            session.Finish(null);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            console.AppendLine($"{configuration.Name} could not start: {exception.Message}", ConsoleSource.Error);
            session.Finish(null);
        }
    }

    private async Task<bool> RunStepAsync(RunSession session, IRunConfigurationType type, BeforeLaunchStep step, string root, IReadOnlyList<string> chain)
    {
        var console = session.Console;
        var token = session.StopToken;
        switch (step.Kind)
        {
            case BeforeLaunchKind.Build:
            {
                console.AppendLine($"Building {session.Configuration.Name}...", ConsoleSource.System);
                var channel = new ConsoleChannel(BuildChannel, console);
                var result = await BuildWithTypeAsync(type, session.Configuration, root, channel, token).ConfigureAwait(true);
                if (result is null)
                {
                    if (!build.CanBuild)
                        return true;

                    console.AppendLine("Building the folder; the Build output shows the log.", ConsoleSource.System);
                    result = await build.BuildAsync().ConfigureAwait(true);
                }

                return result?.Succeeded == true;
            }

            case BeforeLaunchKind.RunConfiguration:
            {
                var name = step.Argument ?? "";
                if (chain.Contains(name, StringComparer.Ordinal))
                {
                    console.AppendLine($"{name} cannot run before itself.", ConsoleSource.Error);
                    return false;
                }

                if (configurations.Find(name) is not { } other)
                {
                    console.AppendLine($"There is no configuration named {name}.", ConsoleSource.Error);
                    return false;
                }

                console.AppendLine($"Running {name}...", ConsoleSource.System);
                var run = await RunAsync(other, RunMode.Run, chain).ConfigureAwait(true);
                return run is not null && await run.Completion.ConfigureAwait(true) == 0;
            }

            default:
            {
                if (string.IsNullOrWhiteSpace(step.Argument))
                    return true;

                console.AppendLine("> " + step.Argument, ConsoleSource.System);
                using var process = RunProcess.Start(ShellCommand(step.Argument, root), console.Append);
                using var registration = token.Register(process.Stop);
                return await process.Exited.ConfigureAwait(true) == 0;
            }
        }
    }

    private async Task<BuildResult?> BuildWithTypeAsync(IRunConfigurationType type, RunConfiguration configuration, string root, IOutputChannel channel,
        CancellationToken token)
    {
        diagnostics.Clear(BuildSource);
        var found = new Dictionary<string, List<Diagnostic>>(StringComparer.Ordinal);
        void Report(Diagnostic diagnostic)
        {
            lock (found)
            {
                if (!found.TryGetValue(diagnostic.FilePath, out var list))
                    found[diagnostic.FilePath] = list = [];
                list.Add(diagnostic);
                diagnostics.Set(BuildSource, diagnostic.FilePath, [.. list]);
            }
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await Task.Run(() => type.BuildAsync(configuration, new BuildContext(root, channel, Report), token), token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            channel.AppendLine("The build was canceled.");
            return new BuildResult(false, 0, 0, stopwatch.Elapsed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            channel.AppendLine($"The build could not run: {exception.Message}");
            return new BuildResult(false, 1, 0, stopwatch.Elapsed);
        }
    }

    /// <summary>Gets how a before-launch command line runs: through the system's shell, in the folder.</summary>
    internal static LaunchPlan ShellCommand(string commandLine, string root) =>
        OperatingSystem.IsWindows()
            ? new LaunchPlan("cmd.exe", ["/d", "/c", commandLine], root)
            : new LaunchPlan("/bin/sh", ["-c", commandLine], root);

    /// <summary>Writes a program and its arguments as a command line to show, quoting arguments with spaces.</summary>
    internal static string CommandLine(string program, IReadOnlyList<string> arguments) =>
        string.Join(' ', [Quote(program), .. arguments.Select(Quote)]);

    private static string Quote(string argument) =>
        argument.Length == 0 ? "\"\"" : argument.Any(c => char.IsWhiteSpace(c) || c == '"') ? "\"" + argument.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : argument;

    private static string Describe(BeforeLaunchStep step) => step.Kind switch
    {
        BeforeLaunchKind.Build => "The build",
        BeforeLaunchKind.RunConfiguration => $"Running {step.Argument}",
        _ => $"The command {step.Argument}",
    };

    private static string Seconds(TimeSpan time) => time.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture);

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    // Lets a configuration's build write into its run's console.
    private sealed class ConsoleChannel(string name, ConsoleBuffer console) : IOutputChannel
    {
        public string Name => name;

        public void Append(string text) => console.Append(text, ConsoleSource.System);

        public void AppendLine(string line) => console.AppendLine(line, ConsoleSource.System);

        public void Clear()
        {
        }

        public void Show()
        {
        }
    }
}
