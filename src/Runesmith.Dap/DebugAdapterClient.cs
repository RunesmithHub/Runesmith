using System.Text.Json;
using System.Text.Json.Nodes;
using Runesmith.Dap.Protocol;

namespace Runesmith.Dap;

/// <summary>Talks to a debug adapter over its streams: the requests a debugger's interface needs, as typed methods, and the adapter's events.</summary>
/// <remarks>Every method can be called from any thread and never blocks it. Events are raised on a background task, one at a time and in the
/// order the adapter sent them; <see cref="Closed"/> comes after the events sent before the adapter stopped. When the adapter stops or its
/// streams break, waiting requests fail with an <see cref="IOException"/>.</remarks>
public sealed class DebugAdapterClient : IAsyncDisposable
{
    private readonly DapConnection _connection;
    private readonly TaskCompletionSource _initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Capabilities _capabilities = Capabilities.None;

    /// <summary>Creates a client over an adapter's output and input; call <see cref="Start"/> to begin reading.</summary>
    /// <param name="fromAdapter">The stream the adapter writes to, such as its standard output.</param>
    /// <param name="toAdapter">The stream the adapter reads, such as its standard input.</param>
    public DebugAdapterClient(Stream fromAdapter, Stream toAdapter)
    {
        _connection = new DapConnection(fromAdapter, toAdapter)
        {
            EventHandler = OnEvent,
            Canceled = OnCanceled,
        };
        _connection.Closed += (_, failure) =>
        {
            _initialized.TrySetException(new IOException("The debug adapter stopped before it was ready.", failure));
            Closed?.Invoke(this, failure);
        };
        _connection.Trace += trace => Trace?.Invoke(trace);
    }

    /// <summary>Gets what the adapter supports, once <see cref="InitializeAsync"/> has answered.</summary>
    public Capabilities Capabilities => Volatile.Read(ref _capabilities);

    /// <summary>Gets whether the adapter's streams have closed.</summary>
    public bool IsClosed => _connection.IsClosed;

    /// <summary>Gets a task that ends when the adapter sends <c>initialized</c>, ready for breakpoints and <c>configurationDone</c>.</summary>
    public Task Initialized => _initialized.Task;

    /// <summary>Gets or sets what answers the adapter's own requests, such as <c>runInTerminal</c>, with the response body; without one they
    /// fail as not supported.</summary>
    public Func<string, JsonElement?, CancellationToken, Task<object?>>? RequestHandler
    {
        get => _connection.RequestHandler;
        set => _connection.RequestHandler = value;
    }

    public event EventHandler<StoppedEvent>? Stopped;

    public event EventHandler<ContinuedEvent>? Continued;

    public event EventHandler<ExitedEvent>? Exited;

    public event EventHandler<TerminatedEvent>? Terminated;

    public event EventHandler<OutputEvent>? Output;

    public event EventHandler<ThreadEvent>? ThreadChanged;

    public event EventHandler<BreakpointEvent>? BreakpointChanged;

    /// <summary>Raised for events without a typed counterpart, such as <c>module</c> or <c>process</c>.</summary>
    public event EventHandler<UnknownEvent>? OtherEvent;

    /// <summary>Raised once when the adapter's streams close, with the exception that closed them, or null when the adapter ended them.</summary>
    public event EventHandler<Exception?>? Closed;

    /// <summary>Raised for every message sent or received, for logs; the JSON is only produced while there are subscribers.</summary>
    public event Action<DapTrace>? Trace;

    /// <summary>Starts reading from the adapter on a background task.</summary>
    public void Start() => _connection.Start();

    /// <summary>Sends <c>initialize</c> and keeps the adapter's capabilities.</summary>
    public async Task<Capabilities> InitializeAsync(InitializeArguments arguments, CancellationToken cancellationToken = default)
    {
        var capabilities = await RequestAsync<Capabilities>("initialize", arguments, cancellationToken).ConfigureAwait(false) ?? Capabilities.None;
        Volatile.Write(ref _capabilities, capabilities);
        return capabilities;
    }

    /// <summary>Sends <c>launch</c>, which most adapters answer only after <see cref="ConfigurationDoneAsync"/>.</summary>
    /// <param name="arguments">The adapter's own launch settings, such as <c>program</c> and <c>args</c>.</param>
    public Task LaunchAsync(JsonObject arguments, CancellationToken cancellationToken = default) => SendAsync("launch", arguments, cancellationToken);

    /// <summary>Sends <c>attach</c>, which most adapters answer only after <see cref="ConfigurationDoneAsync"/>.</summary>
    public Task AttachAsync(JsonObject arguments, CancellationToken cancellationToken = default) => SendAsync("attach", arguments, cancellationToken);

    /// <summary>Tells the adapter that the breakpoints are set, when it supports <c>configurationDone</c>; otherwise does nothing.</summary>
    public Task ConfigurationDoneAsync(CancellationToken cancellationToken = default) =>
        Capabilities.SupportsConfigurationDoneRequest ? SendAsync("configurationDone", null, cancellationToken) : Task.CompletedTask;

    /// <summary>Replaces the breakpoints of one source; the answer has one breakpoint per requested one, in order.</summary>
    public async Task<IReadOnlyList<Breakpoint>> SetBreakpointsAsync(SetBreakpointsArguments arguments, CancellationToken cancellationToken = default) =>
        (await RequestAsync<SetBreakpointsResponse>("setBreakpoints", arguments, cancellationToken).ConfigureAwait(false))?.Breakpoints ?? [];

    /// <summary>Turns on the exception breakpoint filters named, and off the others.</summary>
    public Task SetExceptionBreakpointsAsync(IReadOnlyList<string> filters, CancellationToken cancellationToken = default) =>
        SendAsync("setExceptionBreakpoints", new SetExceptionBreakpointsArguments(filters), cancellationToken);

    public async Task<IReadOnlyList<DapThread>> ThreadsAsync(CancellationToken cancellationToken = default) =>
        (await RequestAsync<ThreadsResponse>("threads", null, cancellationToken).ConfigureAwait(false))?.Threads ?? [];

    public async Task<StackTraceResponse> StackTraceAsync(StackTraceArguments arguments, CancellationToken cancellationToken = default) =>
        await RequestAsync<StackTraceResponse>("stackTrace", arguments, cancellationToken).ConfigureAwait(false) ?? new StackTraceResponse([]);

    public async Task<IReadOnlyList<Scope>> ScopesAsync(int frameId, CancellationToken cancellationToken = default) =>
        (await RequestAsync<ScopesResponse>("scopes", new ScopesArguments(frameId), cancellationToken).ConfigureAwait(false))?.Scopes ?? [];

    public async Task<IReadOnlyList<Variable>> VariablesAsync(VariablesArguments arguments, CancellationToken cancellationToken = default) =>
        (await RequestAsync<VariablesResponse>("variables", arguments, cancellationToken).ConfigureAwait(false))?.Variables ?? [];

    public async Task<EvaluateResponse> EvaluateAsync(EvaluateArguments arguments, CancellationToken cancellationToken = default) =>
        await RequestAsync<EvaluateResponse>("evaluate", arguments, cancellationToken).ConfigureAwait(false) ?? new EvaluateResponse("", 0);

    /// <summary>Resumes a thread; returns whether every thread resumed.</summary>
    public async Task<bool> ContinueAsync(int threadId, CancellationToken cancellationToken = default) =>
        (await RequestAsync<ContinueResponse>("continue", new ThreadArguments(threadId), cancellationToken).ConfigureAwait(false))?.AllThreadsContinued ?? true;

    /// <summary>Steps over the current line.</summary>
    public Task NextAsync(int threadId, CancellationToken cancellationToken = default) => SendAsync("next", new ThreadArguments(threadId), cancellationToken);

    public Task StepInAsync(int threadId, CancellationToken cancellationToken = default) => SendAsync("stepIn", new ThreadArguments(threadId), cancellationToken);

    public Task StepOutAsync(int threadId, CancellationToken cancellationToken = default) => SendAsync("stepOut", new ThreadArguments(threadId), cancellationToken);

    public Task PauseAsync(int threadId, CancellationToken cancellationToken = default) => SendAsync("pause", new ThreadArguments(threadId), cancellationToken);

    /// <summary>Asks the adapter to end the program gently, when it supports <c>terminate</c>; returns whether it was asked.</summary>
    public async Task<bool> TerminateAsync(CancellationToken cancellationToken = default)
    {
        if (!Capabilities.SupportsTerminateRequest)
            return false;

        await SendAsync("terminate", new TerminateArguments(), cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Ends the debugging session; a launched program ends with it unless <see cref="DisconnectArguments.TerminateDebuggee"/> says
    /// otherwise.</summary>
    public Task DisconnectAsync(DisconnectArguments? arguments = null, CancellationToken cancellationToken = default) =>
        SendAsync("disconnect", arguments ?? new DisconnectArguments(), cancellationToken);

    /// <summary>Sends any request and returns the body of its answer, for requests without a typed method.</summary>
    /// <exception cref="DebugAdapterException">The adapter answered with an error.</exception>
    /// <exception cref="IOException">The adapter stopped before it answered.</exception>
    public Task<JsonElement?> SendRequestAsync(string command, object? arguments, CancellationToken cancellationToken = default) =>
        _connection.SendRequestAsync(command, arguments, cancellationToken);

    public ValueTask DisposeAsync() => _connection.DisposeAsync();

    private async Task<T?> RequestAsync<T>(string command, object? arguments, CancellationToken cancellationToken) =>
        DapConnection.Read<T>(await _connection.SendRequestAsync(command, arguments, cancellationToken).ConfigureAwait(false));

    private Task<JsonElement?> SendAsync(string command, object? arguments, CancellationToken cancellationToken) =>
        _connection.SendRequestAsync(command, arguments, cancellationToken);

    private void OnCanceled(int requestSeq)
    {
        if (Capabilities.SupportsCancelRequest && !_connection.IsClosed)
            _ = CancelQuietlyAsync(requestSeq);
    }

    private async Task CancelQuietlyAsync(int requestSeq)
    {
        try
        {
            await _connection.SendRequestAsync("cancel", new CancelArguments { RequestId = requestSeq }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is DebugAdapterException or IOException)
        {
        }
    }

    private void OnEvent(string name, JsonElement? body)
    {
        switch (name)
        {
            case "initialized":
                _initialized.TrySetResult();
                break;
            case "stopped":
                Raise(Stopped, body);
                break;
            case "continued":
                Raise(Continued, body);
                break;
            case "exited":
                Raise(Exited, body);
                break;
            case "terminated":
                Terminated?.Invoke(this, DapConnection.Read<TerminatedEvent>(body) ?? new TerminatedEvent());
                break;
            case "output":
                Raise(Output, body);
                break;
            case "thread":
                Raise(ThreadChanged, body);
                break;
            case "breakpoint":
                Raise(BreakpointChanged, body);
                break;
            case "capabilities":
                if (DapConnection.Read<CapabilitiesEvent>(body)?.Capabilities is { } changed)
                    Volatile.Write(ref _capabilities, Merge(Capabilities, changed));
                break;
            default:
                OtherEvent?.Invoke(this, new UnknownEvent(name, body));
                break;
        }
    }

    private void Raise<T>(EventHandler<T>? handler, JsonElement? body)
        where T : class
    {
        if (handler is not null && DapConnection.Read<T>(body) is { } value)
            handler(this, value);
    }

    // A capabilities event carries only what changed, so flags it leaves out keep their value.
    private static Capabilities Merge(Capabilities current, Capabilities changed)
    {
        var merged = JsonSerializer.SerializeToNode(current, DapJsonContext.Default.Capabilities)!.AsObject();
        foreach (var (key, value) in JsonSerializer.SerializeToNode(changed, DapJsonContext.Default.Capabilities)!.AsObject())
        {
            if (value is JsonValue flag && flag.TryGetValue<bool>(out var on) && !on)
                continue;
            merged[key] = value?.DeepClone();
        }

        return merged.Deserialize(DapJsonContext.Default.Capabilities) ?? current;
    }
}
