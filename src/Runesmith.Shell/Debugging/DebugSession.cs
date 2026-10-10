using System.Globalization;
using System.Threading.Channels;
using Runesmith.Dap;
using Runesmith.Dap.Protocol;
using Runesmith.Sdk.Debugging;
using Runesmith.Shell.Running;

namespace Runesmith.Shell.Debugging;

/// <summary>Where a debug session is.</summary>
public enum DebugState
{
    /// <summary>The debugger starts, takes the breakpoints and starts the program.</summary>
    Starting,

    /// <summary>The program runs.</summary>
    Running,

    /// <summary>The program is paused, at a breakpoint, after a step or on request.</summary>
    Paused,

    /// <summary>The session ended.</summary>
    Ended,
}

/// <summary>One program under a debugger: its state, threads and call stack while paused, and the requests to step, continue, inspect and
/// stop it, over a debug adapter.</summary>
/// <remarks>Its members can be called from any thread, and none blocks. The adapter's events are handled one at a time in order, off the UI
/// thread, and <see cref="StateChanged"/> is raised from there; a step's answer reaches the call stack without waiting for anything else.</remarks>
public sealed class DebugSession
{
    private const int FrameLimit = 200;
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);

    private readonly DebugAdapterClient client;
    private readonly BreakpointService breakpoints;
    private readonly Func<ValueTask> release;
    private readonly Channel<Func<Task>> work = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true });
    private readonly TaskCompletionSource<int?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock gate = new();
    private readonly HashSet<string> sentFiles = new(PathKey.Comparer);
    private readonly Dictionary<int, LineBreakpoint> adapterIds = [];
    private IReadOnlyList<DapThread> threads = [];
    private IReadOnlyList<StackFrame> frames = [];
    private StackFrame? currentFrame;
    private StoppedEvent? stop;
    private int? threadId;
    private int epoch;
    private int ended;
    private bool configured;

    internal DebugSession(string name, string debuggerName, DebugAdapterClient client, BreakpointService breakpoints, Func<ValueTask> release)
    {
        Name = name;
        DebuggerName = debuggerName;
        this.client = client;
        this.breakpoints = breakpoints;
        this.release = release;
        client.Stopped += (_, e) => Enqueue(() => OnStoppedAsync(e));
        client.Continued += (_, e) => Enqueue(() => OnContinued(e));
        client.Exited += (_, e) => ExitCode = e.ExitCode;
        client.Terminated += (_, _) => Enqueue(OnTerminatedAsync);
        client.Output += (_, e) => OnOutput(e);
        client.BreakpointChanged += (_, e) => OnBreakpointChanged(e);
        client.ThreadChanged += (_, e) => Enqueue(() => OnThreadChangedAsync(e));
        client.Closed += (_, failure) => Enqueue(() => OnClosedAsync(failure));
        _ = Task.Run(ProcessAsync);
    }

    /// <summary>Gets the name of what is debugged, such as the run configuration's.</summary>
    public string Name { get; }

    /// <summary>Gets the debugger's name, such as the debug engine's.</summary>
    public string DebuggerName { get; }

    public DebugState State { get; private set; }

    /// <summary>Gets what the debugger supports.</summary>
    public Capabilities Capabilities => client.Capabilities;

    /// <summary>Gets the program's threads, as of the last pause.</summary>
    public IReadOnlyList<DapThread> Threads => threads;

    /// <summary>Gets the thread that stepping and continuing act on: the one that paused, or the one picked in the call stack.</summary>
    public int? ThreadId => threadId;

    /// <summary>Gets why the program last paused, while it is paused.</summary>
    public StoppedEvent? Stop => stop;

    /// <summary>Gets the call stack of <see cref="ThreadId"/> while paused, innermost first.</summary>
    public IReadOnlyList<StackFrame> Frames => frames;

    /// <summary>Gets the frame the editor shows and variables and watches are read in: the innermost one with source, unless the user picked
    /// another.</summary>
    public StackFrame? CurrentFrame => currentFrame;

    /// <summary>Gets the program's exit code once it exited, or null.</summary>
    public int? ExitCode { get; private set; }

    /// <summary>Gets the debug console's text: the program's output, the debugger's messages and evaluations.</summary>
    public ConsoleBuffer Console { get; } = new();

    /// <summary>Gets a task that ends with the program's exit code, or null when it is unknown, once the session has ended.</summary>
    public Task<int?> Completion => completion.Task;

    /// <summary>Raised, off the UI thread, when the state, the threads, the call stack or the current frame change.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Raised, off the UI thread, for the program's own output, its standard output and error.</summary>
    public event EventHandler<OutputEvent>? ProgramOutput;

    /// <summary>Starts the adapter's side: <c>initialize</c>, the launch or attach request, the breakpoints and <c>configurationDone</c>.</summary>
    /// <exception cref="DebugAdapterException">The debugger refused to start the program; the message says why.</exception>
    /// <exception cref="IOException">The debugger stopped while it started.</exception>
    public async Task StartAsync(DebugAdapterRequest request, string adapterId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        client.Start();
        await client.InitializeAsync(new InitializeArguments(adapterId)
        {
            ClientId = "runesmith",
            ClientName = "Runesmith",
            Locale = CultureInfo.CurrentUICulture.Name,
            SupportsVariableType = true,
        }, cancellationToken).ConfigureAwait(false);

        var started = request.Kind == DebugRequestKind.Attach
            ? client.AttachAsync(request.Arguments, cancellationToken)
            : client.LaunchAsync(request.Arguments, cancellationToken);
        if (await Task.WhenAny(client.Initialized, started).ConfigureAwait(false) == started)
            await started.ConfigureAwait(false);
        await client.Initialized.WaitAsync(cancellationToken).ConfigureAwait(false);

        foreach (var path in breakpoints.All.Select(b => b.Path).Distinct(PathKey.Comparer).ToList())
            await SendBreakpointsAsync(path, cancellationToken).ConfigureAwait(false);
        await SendExceptionFiltersAsync(cancellationToken).ConfigureAwait(false);
        await client.ConfigurationDoneAsync(cancellationToken).ConfigureAwait(false);
        configured = true;
        await started.ConfigureAwait(false);

        lock (gate)
        {
            if (State != DebugState.Starting)
                return;
            State = DebugState.Running;
        }

        RaiseStateChanged();
    }

    /// <summary>Resumes the program.</summary>
    public Task ContinueAsync() => ResumeAsync("continue", id => client.ContinueAsync(id));

    /// <summary>Runs the current line and pauses at the next one, stepping over calls.</summary>
    public Task StepOverAsync() => ResumeAsync("next", id => client.NextAsync(id));

    /// <summary>Steps into the call on the current line, or over the line when it has none.</summary>
    public Task StepIntoAsync() => ResumeAsync("stepIn", id => client.StepInAsync(id));

    /// <summary>Runs until the current function returns.</summary>
    public Task StepOutAsync() => ResumeAsync("stepOut", id => client.StepOutAsync(id));

    /// <summary>Pauses the running program.</summary>
    public async Task PauseAsync()
    {
        if (State != DebugState.Running)
            return;

        await TryAsync("pause", () => client.PauseAsync(threadId ?? FirstThread(threads) ?? 0)).ConfigureAwait(false);
    }

    /// <summary>Ends the program and the debugger: asks gently first, and stops them after a few seconds.</summary>
    public async Task StopAsync()
    {
        if (State == DebugState.Ended)
            return;

        using var timeout = new CancellationTokenSource(StopTimeout);
        try
        {
            if (!client.IsClosed && !await client.TerminateAsync(timeout.Token).ConfigureAwait(false))
                await client.DisconnectAsync(new DisconnectArguments { TerminateDebuggee = true }, timeout.Token).ConfigureAwait(false);
            await completion.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is DebugAdapterException or IOException or OperationCanceledException or TimeoutException)
        {
        }

        await EndAsync().ConfigureAwait(false);
    }

    /// <summary>Shows another thread's call stack, while paused.</summary>
    public async Task SelectThreadAsync(int id)
    {
        if (State != DebugState.Paused)
            return;

        var current = Volatile.Read(ref epoch);
        var stack = await TryAsync("stackTrace", () => client.StackTraceAsync(new StackTraceArguments(id) { Levels = FrameLimit })).ConfigureAwait(false);
        if (stack is null || current != Volatile.Read(ref epoch))
            return;

        lock (gate)
        {
            threadId = id;
            frames = stack.StackFrames;
            currentFrame = PickFrame(stack.StackFrames);
        }

        RaiseStateChanged();
    }

    /// <summary>Makes a frame of the call stack the current one, for the editor, variables and watches.</summary>
    public void SelectFrame(StackFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        lock (gate)
        {
            if (!frames.Contains(frame) || currentFrame == frame)
                return;
            currentFrame = frame;
        }

        RaiseStateChanged();
    }

    /// <summary>Gets a frame's scopes, such as locals and arguments.</summary>
    /// <exception cref="DebugAdapterException">The debugger could not read them.</exception>
    public Task<IReadOnlyList<Scope>> GetScopesAsync(StackFrame frame, CancellationToken cancellationToken = default) =>
        client.ScopesAsync(frame.Id, cancellationToken);

    /// <summary>Gets the children of a scope, variable or result.</summary>
    /// <exception cref="DebugAdapterException">The debugger could not read them.</exception>
    public Task<IReadOnlyList<Variable>> GetVariablesAsync(int reference, CancellationToken cancellationToken = default) =>
        client.VariablesAsync(new VariablesArguments(reference), cancellationToken);

    /// <summary>Evaluates an expression in the current frame.</summary>
    /// <param name="context"><c>watch</c>, <c>repl</c> or <c>hover</c>.</param>
    /// <exception cref="DebugAdapterException">The expression could not be evaluated; the message says why.</exception>
    public Task<EvaluateResponse> EvaluateAsync(string expression, string context, CancellationToken cancellationToken = default) =>
        client.EvaluateAsync(new EvaluateArguments(expression) { FrameId = currentFrame?.Id, Context = context }, cancellationToken);

    /// <summary>Sends a file's breakpoints again, after the user changed them; with null, sends every file's.</summary>
    public Task UpdateBreakpointsAsync(string? path) => Enqueue(async () =>
    {
        if (!configured || State == DebugState.Ended)
            return;

        IEnumerable<string> paths = path is not null
            ? [path]
            : [.. sentFiles.Concat(breakpoints.All.Select(b => b.Path)).Distinct(PathKey.Comparer)];
        foreach (var file in paths)
            await TryAsync("setBreakpoints", () => SendBreakpointsAsync(file, CancellationToken.None)).ConfigureAwait(false);
    });

    /// <summary>Sends the exception breakpoint choices again, after the user changed them.</summary>
    public Task UpdateExceptionFiltersAsync() => Enqueue(async () =>
    {
        if (configured && State != DebugState.Ended)
            await TryAsync("setExceptionBreakpoints", () => SendExceptionFiltersAsync(CancellationToken.None)).ConfigureAwait(false);
    });

    /// <summary>Gets the exception breakpoint filters to turn on: the user's choices, or the debugger's defaults.</summary>
    public IReadOnlyList<string> ExceptionFiltersToSend()
    {
        var offered = Capabilities.ExceptionBreakpointFilters ?? [];
        var chosen = breakpoints.ExceptionFilters;
        return [.. offered.Where(f => chosen is null ? f.Default : chosen.Contains(f.Filter)).Select(f => f.Filter)];
    }

    internal void WriteLine(string text, ConsoleSource source) => Console.AppendLine(text, source);

    private async Task SendBreakpointsAsync(string path, CancellationToken cancellationToken)
    {
        var capabilities = Capabilities;
        var all = breakpoints.In(path);
        var sent = all.Where(b => b.IsEnabled && (!b.IsLogPoint || capabilities.SupportsLogPoints)).ToList();
        var found = await client.SetBreakpointsAsync(new SetBreakpointsArguments(new Source { Path = path, Name = Path.GetFileName(path) },
        [
            .. sent.Select(b => new SourceBreakpoint(b.Line + 1)
            {
                Condition = string.IsNullOrWhiteSpace(b.Condition) ? null : b.Condition,
                HitCondition = string.IsNullOrWhiteSpace(b.HitCondition) ? null : b.HitCondition,
                LogMessage = b.IsLogPoint ? b.LogMessage : null,
            }),
        ]), cancellationToken).ConfigureAwait(false);

        var statuses = new Dictionary<LineBreakpoint, BreakpointStatus>();
        lock (gate)
        {
            sentFiles.Add(path);
            foreach (var stale in adapterIds.Where(p => PathKey.Equals(p.Value.Path, path)).Select(p => p.Key).ToList())
                adapterIds.Remove(stale);
            for (var i = 0; i < sent.Count; i++)
            {
                var answer = i < found.Count ? found[i] : null;
                statuses[sent[i]] = new BreakpointStatus(answer?.Verified ?? false, answer?.Message);
                if (answer?.Id is { } id)
                    adapterIds[id] = sent[i];
            }
        }

        foreach (var logPoint in all.Where(b => b.IsEnabled && b.IsLogPoint && !capabilities.SupportsLogPoints))
            statuses[logPoint] = new BreakpointStatus(false, $"{DebuggerName} does not support log points.");
        breakpoints.SetStatuses(statuses);
    }

    private async Task SendExceptionFiltersAsync(CancellationToken cancellationToken)
    {
        if (Capabilities.ExceptionBreakpointFilters is { Count: > 0 })
            await client.SetExceptionBreakpointsAsync(ExceptionFiltersToSend(), cancellationToken).ConfigureAwait(false);
    }

    private async Task ResumeAsync(string command, Func<int, Task> send)
    {
        int id;
        lock (gate)
        {
            if (State != DebugState.Paused || (threadId ?? FirstThread(threads)) is not { } paused)
                return;

            id = paused;
            epoch++;
            State = DebugState.Running;
            stop = null;
            frames = [];
            currentFrame = null;
        }

        RaiseStateChanged();
        await TryAsync(command, () => send(id)).ConfigureAwait(false);
    }

    private async Task OnStoppedAsync(StoppedEvent e)
    {
        int current;
        lock (gate)
        {
            if (State == DebugState.Ended)
                return;
            current = ++epoch;
        }

        var all = await TryAsync("threads", () => client.ThreadsAsync()).ConfigureAwait(false) ?? threads;
        var id = e.ThreadId ?? threadId ?? FirstThread(all);
        var stack = id is { } stopped ? await TryAsync("stackTrace", () => client.StackTraceAsync(new StackTraceArguments(stopped) { Levels = FrameLimit })).ConfigureAwait(false) : null;
        lock (gate)
        {
            if (current != epoch || State == DebugState.Ended)
                return;

            threads = all;
            threadId = id;
            stop = e;
            frames = stack?.StackFrames ?? [];
            currentFrame = PickFrame(frames);
            State = DebugState.Paused;
        }

        if (e.Reason == "exception" && (e.Text ?? e.Description) is { Length: > 0 } text)
            Console.AppendLine(text, ConsoleSource.Error);
        RaiseStateChanged();
    }

    private Task OnContinued(ContinuedEvent e)
    {
        lock (gate)
        {
            if (State != DebugState.Paused || (e.AllThreadsContinued == false && e.ThreadId != threadId))
                return Task.CompletedTask;

            epoch++;
            State = DebugState.Running;
            stop = null;
            frames = [];
            currentFrame = null;
        }

        RaiseStateChanged();
        return Task.CompletedTask;
    }

    private async Task OnThreadChangedAsync(ThreadEvent e)
    {
        if (e.Reason != "exited" || State != DebugState.Paused)
            return;

        if (await TryAsync("threads", () => client.ThreadsAsync()).ConfigureAwait(false) is { } all)
        {
            threads = all;
            RaiseStateChanged();
        }
    }

    private async Task OnTerminatedAsync()
    {
        if (!client.IsClosed)
        {
            using var timeout = new CancellationTokenSource(StopTimeout);
            try
            {
                await client.DisconnectAsync(new DisconnectArguments(), timeout.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is DebugAdapterException or IOException or OperationCanceledException)
            {
            }
        }

        await EndAsync().ConfigureAwait(false);
    }

    private async Task OnClosedAsync(Exception? failure)
    {
        if (State == DebugState.Ended)
            return;

        if (State != DebugState.Starting)
            Console.AppendLine(failure is null ? $"{DebuggerName} stopped." : $"{DebuggerName} stopped unexpectedly: {failure.Message}", ConsoleSource.System);
        await EndAsync().ConfigureAwait(false);
    }

    private void OnOutput(OutputEvent e)
    {
        if (e.Category == "telemetry")
            return;

        var source = e.Category switch
        {
            "stdout" => ConsoleSource.Output,
            "stderr" => ConsoleSource.Error,
            _ => ConsoleSource.System,
        };
        Console.Append(e.Output, source);
        if (source != ConsoleSource.System)
            ProgramOutput?.Invoke(this, e);
    }

    private void OnBreakpointChanged(BreakpointEvent e)
    {
        LineBreakpoint? breakpoint;
        lock (gate)
            breakpoint = e.Breakpoint.Id is { } id ? adapterIds.GetValueOrDefault(id) : null;
        if (breakpoint is not null && e.Reason != "removed")
            breakpoints.SetStatuses(new Dictionary<LineBreakpoint, BreakpointStatus> { [breakpoint] = new(e.Breakpoint.Verified, e.Breakpoint.Message) });
    }

    private async Task EndAsync()
    {
        if (Interlocked.Exchange(ref ended, 1) != 0)
            return;

        lock (gate)
        {
            epoch++;
            State = DebugState.Ended;
            stop = null;
            frames = [];
            currentFrame = null;
        }

        breakpoints.ClearStatuses();
        work.Writer.TryComplete();
        try
        {
            await release().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }

        RaiseStateChanged();
        completion.TrySetResult(ExitCode);
    }

    // The innermost frame with a file, since frames without source, such as the runtime's, cannot be shown in an editor.
    private static StackFrame? PickFrame(IReadOnlyList<StackFrame> stack) =>
        stack.FirstOrDefault(f => f.Source?.Path is { Length: > 0 }) ?? (stack.Count > 0 ? stack[0] : null);

    private static int? FirstThread(IReadOnlyList<DapThread> list) => list.Count > 0 ? list[0].Id : null;

    private async Task<T?> TryAsync<T>(string command, Func<Task<T>> request)
    {
        try
        {
            return await request().ConfigureAwait(false);
        }
        catch (DebugAdapterException exception)
        {
            Console.AppendLine($"{command}: {exception.Message}", ConsoleSource.Error);
        }
        catch (IOException)
        {
        }

        return default;
    }

    private async Task TryAsync(string command, Func<Task> request) =>
        await TryAsync<bool>(command, async () =>
        {
            await request().ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);

    private Task Enqueue(Func<Task> step)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!work.Writer.TryWrite(async () =>
            {
                try
                {
                    await step().ConfigureAwait(false);
                }
                finally
                {
                    done.TrySetResult();
                }
            }))
            done.TrySetResult();
        return done.Task;
    }

    private async Task ProcessAsync()
    {
        await foreach (var step in work.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                await step().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Console.AppendLine($"The debugger's event could not be handled: {exception.Message}", ConsoleSource.Error);
            }
        }
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
