namespace Runesmith.Dap.Protocol;

/// <summary>The program paused, such as at a breakpoint or after a step.</summary>
/// <param name="Reason">Why: <c>step</c>, <c>breakpoint</c>, <c>exception</c>, <c>pause</c>, <c>entry</c>, <c>goto</c>, <c>function breakpoint</c>,
/// <c>data breakpoint</c> or <c>instruction breakpoint</c>.</param>
public sealed record StoppedEvent(string Reason)
{
    public string? Description { get; init; }

    public int? ThreadId { get; init; }

    public bool PreserveFocusHint { get; init; }

    /// <summary>Gets more detail, such as an exception's message.</summary>
    public string? Text { get; init; }

    public bool AllThreadsStopped { get; init; }

    public IReadOnlyList<int>? HitBreakpointIds { get; init; }
}

/// <summary>The program resumed without the client asking.</summary>
public sealed record ContinuedEvent(int ThreadId)
{
    public bool? AllThreadsContinued { get; init; }
}

/// <summary>The program exited.</summary>
public sealed record ExitedEvent(int ExitCode);

/// <summary>Debugging ended.</summary>
public sealed record TerminatedEvent;

/// <summary>A thread started or exited.</summary>
/// <param name="Reason"><c>started</c> or <c>exited</c>.</param>
public sealed record ThreadEvent(string Reason, int ThreadId);

/// <summary>Text from the program or the adapter.</summary>
public sealed record OutputEvent(string Output)
{
    /// <summary>Gets where it comes from: <c>console</c>, <c>important</c>, <c>stdout</c>, <c>stderr</c> or <c>telemetry</c>; null means
    /// <c>console</c>.</summary>
    public string? Category { get; init; }

    public string? Group { get; init; }

    public int? VariablesReference { get; init; }

    public Source? Source { get; init; }

    public int? Line { get; init; }

    public int? Column { get; init; }
}

/// <summary>A breakpoint changed on the adapter's side, such as when a module loaded and it became verified.</summary>
/// <param name="Reason"><c>changed</c>, <c>new</c> or <c>removed</c>.</param>
public sealed record BreakpointEvent(string Reason, Breakpoint Breakpoint);

/// <summary>The adapter's capabilities changed.</summary>
public sealed record CapabilitiesEvent(Capabilities Capabilities);

/// <summary>An event the client has no type for, with its body as JSON.</summary>
public sealed record UnknownEvent(string Event, System.Text.Json.JsonElement? Body);

/// <summary>The details of an error answer, when the adapter sends them.</summary>
public sealed record ErrorResponse
{
    public Message? Error { get; init; }
}

/// <summary>A message with an id and a format whose <c>{name}</c> parts come from <see cref="Variables"/>.</summary>
public sealed record Message(int Id, string Format)
{
    public IReadOnlyDictionary<string, string>? Variables { get; init; }

    public bool? ShowUser { get; init; }
}
