using System.Composition;
using System.Diagnostics;
using Runesmith.Composition;
using Runesmith.Dap;
using Runesmith.Sdk.Debugging;
using Runesmith.Sdk.Running;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.Workspace;
using Runesmith.Shell.Running;
using Runesmith.Shell.Services;
using RunesmithHub.Protocol;

namespace Runesmith.Shell.Debugging;

/// <summary>Debugs a run's launch plan, for <see cref="RunService"/> in debug mode.</summary>
public interface IRunDebugger
{
    /// <summary>Gets why nothing can be debugged now, such as when no plugin provides a debug engine, or null when something can.</summary>
    string? WhyCannotDebug { get; }

    /// <summary>Starts the plan's program under the debugger its <see cref="LaunchPlan.Debug"/> names and waits until the session ends.</summary>
    /// <returns>The program's exit code, or null when it did not start or its exit code is unknown; the run's console says why.</returns>
    Task<int?> DebugAsync(RunSession run, LaunchPlan plan, CancellationToken cancellationToken);
}

/// <summary>Starts debug sessions through the debug engines plugins provide, keeps them, and sends them the breakpoints as they change.</summary>
[Export]
[Export(typeof(IRunDebugger))]
[Shared]
public sealed class DebugService : IRunDebugger
{
    /// <summary>The Output channel the debuggers' own logs go to.</summary>
    public const string ChannelName = "Debugger";

    private readonly IEnumerable<Lazy<IDebugAdapterProvider>> providerExports;
    private readonly BreakpointService breakpoints;
    private readonly IWorkspace workspace;
    private readonly Func<object, PluginInfo?> ownerOf;
    private readonly Action<string> logRefusal;
    private readonly Action<string> logAdapter;
    private readonly AdapterStarter start;
    private readonly Lock gate = new();
    private readonly List<(DebugSession Session, RunSession? Run)> sessions = [];
    private IReadOnlyList<IDebugAdapterProvider>? providers;
    private DebugSession? latest;
    private IReadOnlyList<Dap.Protocol.ExceptionBreakpointsFilter> exceptionFilters = [];

    [ImportingConstructor]
    public DebugService([ImportMany] IEnumerable<Lazy<IDebugAdapterProvider>> providers, BreakpointService breakpoints, IWorkspace workspace, IOutputService output)
        : this(providers, breakpoints, workspace, provider => PluginCallers.Of(provider.GetType().Assembly),
            line => output.GetChannel(PluginAccess.ChannelName).AppendLine(line), line => output.GetChannel(ChannelName).AppendLine(line), StartAdapterAsync)
    {
    }

    internal DebugService(IEnumerable<Lazy<IDebugAdapterProvider>> providers, BreakpointService breakpoints, IWorkspace workspace, Func<object, PluginInfo?> ownerOf,
        Action<string> logRefusal, Action<string> logAdapter, AdapterStarter start)
    {
        providerExports = providers;
        this.breakpoints = breakpoints;
        this.workspace = workspace;
        this.ownerOf = ownerOf;
        this.logRefusal = logRefusal;
        this.logAdapter = logAdapter;
        this.start = start;
        breakpoints.Changed += (_, path) =>
        {
            foreach (var session in Active)
                _ = session.UpdateBreakpointsAsync(path);
        };
        breakpoints.ExceptionFiltersChanged += (_, _) =>
        {
            foreach (var session in Active)
                _ = session.UpdateExceptionFiltersAsync();
        };
    }

    /// <summary>Starts or reaches a debug adapter: the adapter's client and what ends the adapter.</summary>
    internal delegate Task<(DebugAdapterClient Client, Func<ValueTask> Release)> AdapterStarter(DebugAdapterDescriptor adapter, string rootPath, Action<string> log,
        CancellationToken cancellationToken);

    /// <summary>Gets the debug engines plugins provide; one that fails to load is left out.</summary>
    public IReadOnlyList<IDebugAdapterProvider> Providers => providers ??= LoadProviders();

    public string? WhyCannotDebug => Providers.Count == 0 ? "No installed plugin provides a debugger." : null;

    /// <summary>Gets the session the debugger's controls act on: the last one started that has not ended, or null.</summary>
    public DebugSession? Current
    {
        get
        {
            lock (gate)
                return sessions.LastOrDefault(s => s.Session.State != DebugState.Ended).Session;
        }
    }

    /// <summary>Gets the session started last, even when it has ended, or null before the first.</summary>
    public DebugSession? Latest => Volatile.Read(ref latest);

    /// <summary>Gets the kinds of exception breakpoints the last debugger that had any offered.</summary>
    public IReadOnlyList<Dap.Protocol.ExceptionBreakpointsFilter> ExceptionFilters => Volatile.Read(ref exceptionFilters);

    /// <summary>Gets the sessions that have not ended, oldest first.</summary>
    public IReadOnlyList<DebugSession> Active
    {
        get
        {
            lock (gate)
                return [.. sessions.Select(s => s.Session).Where(s => s.State != DebugState.Ended)];
        }
    }

    /// <summary>Raised, off the UI thread, when a session starts or ends, or its state, call stack or current frame changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised, off the UI thread, when a session starts.</summary>
    public event EventHandler<DebugSession>? SessionStarted;

    /// <summary>Finds the engine that serves a debugger, such as <c>dotnet</c>, or returns null.</summary>
    public IDebugAdapterProvider? FindProvider(string debugger) =>
        Providers.FirstOrDefault(p => p.Debuggers.Contains(debugger, StringComparer.OrdinalIgnoreCase));

    /// <summary>Gets the run a session belongs to, for restarting it, or null.</summary>
    public RunSession? RunOf(DebugSession session)
    {
        lock (gate)
            return sessions.FirstOrDefault(s => s.Session == session).Run;
    }

    public async Task<int?> DebugAsync(RunSession run, LaunchPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(plan);
        var console = run.Console;
        var session = await StartAsync(run.Configuration.Name, plan, console, run, cancellationToken).ConfigureAwait(false);
        if (session is null)
            return null;

        run.StartedDebugging(() => _ = session.StopAsync());
        session.ProgramOutput += (_, e) => console.Append(e.Output, e.Category == "stderr" ? ConsoleSource.Error : ConsoleSource.Output);
        await using var stop = cancellationToken.Register(() => _ = session.StopAsync()).ConfigureAwait(false);
        return await session.Completion.ConfigureAwait(false);
    }

    /// <summary>Starts a session for a plan, writing what goes wrong to the console.</summary>
    /// <returns>The session, running or paused, or null when it could not start.</returns>
    internal async Task<DebugSession?> StartAsync(string name, LaunchPlan plan, ConsoleBuffer console, RunSession? run, CancellationToken cancellationToken)
    {
        if (plan.Debug is not { } launch)
        {
            console.AppendLine($"{name} does not say how to debug it, so it cannot be debugged.", ConsoleSource.Error);
            return null;
        }

        if (FindProvider(launch.Debugger) is not { } provider)
        {
            console.AppendLine($"No installed plugin provides the {launch.Debugger} debugger. Find one in the plugin hub.", ConsoleSource.Error);
            return null;
        }

        var root = workspace.RootPath ?? plan.WorkingDirectory;
        var context = new DebugAdapterContext(plan, launch, root);
        DebugAdapterDescriptor adapter;
        DebugAdapterRequest request;
        try
        {
            adapter = await Task.Run(() => provider.CreateAdapterAsync(context, cancellationToken), cancellationToken).ConfigureAwait(false);
            request = await Task.Run(() => provider.CreateRequestAsync(context, cancellationToken), cancellationToken).ConfigureAwait(false);
            PluginAccess.Demand(ownerOf(provider), adapter is DebugAdapterServer ? Capabilities.Network : Capabilities.Process, "debugger", logRefusal);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            console.AppendLine($"{provider.Name} could not start: {exception.Message}", ConsoleSource.Error);
            return null;
        }

        (DebugAdapterClient Client, Func<ValueTask> Release) started;
        try
        {
            started = await start(adapter, root, line => logAdapter($"[{provider.Name}] {line}"), cancellationToken).ConfigureAwait(false);
        }
        catch (DebugAdapterException exception)
        {
            console.AppendLine(exception.Message, ConsoleSource.Error);
            return null;
        }

        console.AppendLine($"Debugging {name} with {provider.Name}.", ConsoleSource.System);
        var session = new DebugSession(name, provider.Name, started.Client, breakpoints, started.Release);
        session.StateChanged += (_, _) =>
        {
            if (session.Capabilities.ExceptionBreakpointFilters is { Count: > 0 } filters)
                Volatile.Write(ref exceptionFilters, filters);
            Changed?.Invoke(this, EventArgs.Empty);
        };
        Volatile.Write(ref latest, session);
        lock (gate)
        {
            sessions.RemoveAll(s => s.Session.State == DebugState.Ended);
            sessions.Add((session, run));
        }

        SessionStarted?.Invoke(this, session);
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            await session.StartAsync(request, request.AdapterId ?? launch.Debugger, cancellationToken).ConfigureAwait(false);
            return session;
        }
        catch (Exception exception) when (exception is DebugAdapterException or IOException or OperationCanceledException)
        {
            if (exception is not OperationCanceledException)
                console.AppendLine($"{provider.Name} could not start {name}: {exception.Message}", ConsoleSource.Error);
            await session.StopAsync().ConfigureAwait(false);
            return null;
        }
    }

    private static async Task<(DebugAdapterClient, Func<ValueTask>)> StartAdapterAsync(DebugAdapterDescriptor adapter, string rootPath, Action<string> log,
        CancellationToken cancellationToken)
    {
        DebugAdapterProcess process;
        switch (adapter)
        {
            case DebugAdapterExecutable executable:
                var info = new ProcessStartInfo(executable.Command) { WorkingDirectory = executable.WorkingDirectory ?? rootPath };
                foreach (var argument in executable.Arguments)
                    info.ArgumentList.Add(argument);
                foreach (var (name, value) in executable.Environment)
                    info.Environment[name] = value;
                process = DebugAdapterProcess.Start(info, log);
                break;
            case DebugAdapterServer server:
                process = await DebugAdapterProcess.ConnectAsync(server.Host, server.Port, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new DebugAdapterException($"{adapter.GetType().Name} is not a kind of debug adapter Runesmith can start.");
        }

        return (process.Client, process.DisposeAsync);
    }

    private List<IDebugAdapterProvider> LoadProviders()
    {
        var loaded = new List<IDebugAdapterProvider>();
        foreach (var export in providerExports)
        {
            try
            {
                loaded.Add(export.Value);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                logAdapter($"A debug engine could not be loaded: {exception.Message}");
            }
        }

        return loaded;
    }
}
