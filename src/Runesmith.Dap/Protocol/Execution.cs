namespace Runesmith.Dap.Protocol;

/// <summary>A thread of the program.</summary>
public sealed record DapThread(int Id, string Name);

/// <summary>The body of the answer to <c>threads</c>.</summary>
public sealed record ThreadsResponse(IReadOnlyList<DapThread> Threads);

/// <summary>The arguments of <c>stackTrace</c>.</summary>
public sealed record StackTraceArguments(int ThreadId)
{
    public int? StartFrame { get; init; }

    /// <summary>Gets how many frames to return, or null for all.</summary>
    public int? Levels { get; init; }
}

/// <summary>A frame of a thread's call stack.</summary>
/// <param name="Id">The id to ask for the frame's scopes or to evaluate in it with; valid while the program is paused.</param>
/// <param name="Line">The line, counted from 1, or 0 when there is no source.</param>
public sealed record StackFrame(int Id, string Name, int Line, int Column)
{
    public Source? Source { get; init; }

    public int? EndLine { get; init; }

    public int? EndColumn { get; init; }

    /// <summary>Gets a hint for how to show the frame: <c>normal</c>, <c>label</c> or <c>subtle</c>.</summary>
    public string? PresentationHint { get; init; }
}

/// <summary>The body of the answer to <c>stackTrace</c>.</summary>
public sealed record StackTraceResponse(IReadOnlyList<StackFrame> StackFrames)
{
    public int? TotalFrames { get; init; }
}

/// <summary>The arguments of <c>scopes</c>.</summary>
public sealed record ScopesArguments(int FrameId);

/// <summary>A group of a frame's variables, such as locals or arguments.</summary>
/// <param name="VariablesReference">The reference to ask for the scope's variables with.</param>
public sealed record Scope(string Name, int VariablesReference)
{
    public string? PresentationHint { get; init; }

    public int? NamedVariables { get; init; }

    public int? IndexedVariables { get; init; }

    /// <summary>Gets whether getting the variables is slow, so they are best asked for only when the user opens the scope.</summary>
    public bool Expensive { get; init; }
}

/// <summary>The body of the answer to <c>scopes</c>.</summary>
public sealed record ScopesResponse(IReadOnlyList<Scope> Scopes);

/// <summary>The arguments of <c>variables</c>.</summary>
public sealed record VariablesArguments(int VariablesReference)
{
    /// <summary>Gets <c>indexed</c> or <c>named</c> to get only those children, or null for both.</summary>
    public string? Filter { get; init; }

    public int? Start { get; init; }

    public int? Count { get; init; }
}

/// <summary>A variable, field or element, with its value as text.</summary>
/// <param name="VariablesReference">The reference to ask for its children with, or 0 when it has none.</param>
public sealed record Variable(string Name, string Value, int VariablesReference)
{
    public string? Type { get; init; }

    /// <summary>Gets an expression that evaluates to the variable, for watches.</summary>
    public string? EvaluateName { get; init; }

    public int? NamedVariables { get; init; }

    public int? IndexedVariables { get; init; }
}

/// <summary>The body of the answer to <c>variables</c>.</summary>
public sealed record VariablesResponse(IReadOnlyList<Variable> Variables);

/// <summary>The arguments of <c>evaluate</c>.</summary>
public sealed record EvaluateArguments(string Expression)
{
    /// <summary>Gets the frame to evaluate in, or null for the global scope.</summary>
    public int? FrameId { get; init; }

    /// <summary>Gets what the evaluation is for: <c>watch</c>, <c>repl</c>, <c>hover</c> or <c>clipboard</c>.</summary>
    public string? Context { get; init; }
}

/// <summary>The body of the answer to <c>evaluate</c>.</summary>
public sealed record EvaluateResponse(string Result, int VariablesReference)
{
    public string? Type { get; init; }

    public int? NamedVariables { get; init; }

    public int? IndexedVariables { get; init; }
}

/// <summary>The arguments of <c>continue</c>, <c>next</c>, <c>stepIn</c>, <c>stepOut</c> and <c>pause</c>.</summary>
public sealed record ThreadArguments(int ThreadId)
{
    /// <summary>Gets whether only this thread resumes, when the adapter supports it.</summary>
    public bool? SingleThread { get; init; }
}

/// <summary>The body of the answer to <c>continue</c>.</summary>
public sealed record ContinueResponse
{
    /// <summary>Gets whether every thread resumed; adapters that leave it out resume every thread.</summary>
    public bool? AllThreadsContinued { get; init; }
}

/// <summary>The arguments of <c>disconnect</c>.</summary>
public sealed record DisconnectArguments
{
    public bool? Restart { get; init; }

    /// <summary>Gets whether to end the program too; adapters that launched it end it unless told otherwise.</summary>
    public bool? TerminateDebuggee { get; init; }

    public bool? SuspendDebuggee { get; init; }
}

/// <summary>The arguments of <c>terminate</c>.</summary>
public sealed record TerminateArguments
{
    public bool? Restart { get; init; }
}

/// <summary>The arguments of <c>cancel</c>.</summary>
public sealed record CancelArguments
{
    public int? RequestId { get; init; }

    public string? ProgressId { get; init; }
}
