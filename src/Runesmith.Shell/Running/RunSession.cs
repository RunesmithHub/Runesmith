using System.Diagnostics;
using Runesmith.Sdk.Running;

namespace Runesmith.Shell.Running;

/// <summary>Where a run is.</summary>
public enum RunState
{
    /// <summary>Its before-launch steps run, or its program is being prepared.</summary>
    Preparing,

    /// <summary>Its program runs.</summary>
    Running,

    /// <summary>It ended: the program exited, was stopped, or a step before it failed.</summary>
    Finished,
}

/// <summary>One run of a configuration, shown as a tab of the Run tool window: its console, its state and its program.</summary>
/// <remarks>Its members can be called from any thread; <see cref="StateChanged"/> is raised on the thread that changed the state.</remarks>
public sealed class RunSession : IDisposable
{
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly CancellationTokenSource cancel = new();
    private readonly TaskCompletionSource<int?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private RunProcess? process;

    internal RunSession(RunConfiguration configuration, IRunConfigurationType? type, RunMode mode, string? workingDirectory = null)
    {
        Configuration = configuration;
        Type = type;
        Mode = mode;
        WorkingDirectory = workingDirectory;
    }

    public RunConfiguration Configuration { get; }

    /// <summary>Gets the configuration's type, or null when no plugin provides it.</summary>
    public IRunConfigurationType? Type { get; }

    public RunMode Mode { get; }

    /// <summary>Gets the run's output and Runesmith's messages about it.</summary>
    public ConsoleBuffer Console { get; } = new();

    public RunState State { get; private set; }

    /// <summary>Gets the program's exit code once it exited, or null when it did not start or still runs.</summary>
    public int? ExitCode { get; private set; }

    /// <summary>Gets whether the run ended successfully: its program exited with 0 and was not stopped.</summary>
    public bool Succeeded => State == RunState.Finished && ExitCode == 0 && !WasStopped;

    /// <summary>Gets whether the user stopped the run.</summary>
    public bool WasStopped { get; private set; }

    /// <summary>Gets how long the run has taken, or took.</summary>
    public TimeSpan Elapsed => clock.Elapsed;

    /// <summary>Gets the folder the program runs in, once known.</summary>
    public string? WorkingDirectory { get; internal set; }

    /// <summary>Gets a task that ends with the exit code when the run finishes, or with null when its program did not start.</summary>
    public Task<int?> Completion => completion.Task;

    /// <summary>Gets a token canceled when the run is stopped.</summary>
    internal CancellationToken StopToken => cancel.Token;

    /// <summary>Raised when <see cref="State"/> changes.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Stops the before-launch steps or the program and every process it started.</summary>
    public void Stop()
    {
        if (State == RunState.Finished)
            return;

        WasStopped = true;
        try
        {
            cancel.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        Volatile.Read(ref process)?.Stop();
    }

    /// <summary>Sends a line to the program's standard input and shows it in the console.</summary>
    public async Task SendInputAsync(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (Volatile.Read(ref process) is not { } running || State != RunState.Running)
            return;

        Console.AppendLine(line, ConsoleSource.Input);
        await running.WriteLineAsync(line).ConfigureAwait(false);
    }

    public void Dispose() => cancel.Dispose();

    internal void Started(RunProcess started)
    {
        Volatile.Write(ref process, started);
        State = RunState.Running;
        StateChanged?.Invoke(this, EventArgs.Empty);
        if (WasStopped)
            started.Stop();
    }

    internal void Finish(int? exitCode)
    {
        if (State == RunState.Finished)
            return;

        clock.Stop();
        ExitCode = exitCode;
        State = RunState.Finished;
        Volatile.Read(ref process)?.Dispose();
        StateChanged?.Invoke(this, EventArgs.Empty);
        completion.TrySetResult(exitCode);
    }
}
