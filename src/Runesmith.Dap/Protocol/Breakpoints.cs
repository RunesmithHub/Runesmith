namespace Runesmith.Dap.Protocol;

/// <summary>A source file, by path or, for code without a file, by a reference the adapter gives out.</summary>
public sealed record Source
{
    public string? Name { get; init; }

    public string? Path { get; init; }

    /// <summary>Gets the reference to ask the adapter for the text with, or 0 when the source has a path.</summary>
    public int SourceReference { get; init; }

    /// <summary>Gets a hint for how to show the source: <c>normal</c>, <c>emphasize</c> or <c>deemphasize</c>.</summary>
    public string? PresentationHint { get; init; }

    public string? Origin { get; init; }
}

/// <summary>A breakpoint as the client asks for it.</summary>
/// <param name="Line">The line, counted from 1.</param>
public sealed record SourceBreakpoint(int Line)
{
    public int? Column { get; init; }

    /// <summary>Gets an expression that must be true for the breakpoint to stop.</summary>
    public string? Condition { get; init; }

    /// <summary>Gets how many hits to ignore, such as <c>5</c> or <c>&gt;= 5</c>, as the adapter understands it.</summary>
    public string? HitCondition { get; init; }

    /// <summary>Gets a message to log instead of stopping, with expressions in braces.</summary>
    public string? LogMessage { get; init; }
}

/// <summary>The arguments of <c>setBreakpoints</c>: every breakpoint of one source, replacing the ones set before.</summary>
public sealed record SetBreakpointsArguments(Source Source, IReadOnlyList<SourceBreakpoint> Breakpoints)
{
    public bool SourceModified { get; init; }
}

/// <summary>A breakpoint as the adapter set it.</summary>
/// <param name="Verified">Whether the adapter could set it, such as on a line with code in a loaded module.</param>
public sealed record Breakpoint(bool Verified)
{
    public int? Id { get; init; }

    /// <summary>Gets why the breakpoint is not verified, or other news about it.</summary>
    public string? Message { get; init; }

    public Source? Source { get; init; }

    public int? Line { get; init; }

    public int? Column { get; init; }

    public int? EndLine { get; init; }

    public int? EndColumn { get; init; }
}

/// <summary>The body of the answer to <c>setBreakpoints</c>, one breakpoint per requested one, in order.</summary>
public sealed record SetBreakpointsResponse(IReadOnlyList<Breakpoint> Breakpoints);

/// <summary>The arguments of <c>setExceptionBreakpoints</c>.</summary>
/// <param name="Filters">The ids of the <see cref="ExceptionBreakpointsFilter"/>s to turn on.</param>
public sealed record SetExceptionBreakpointsArguments(IReadOnlyList<string> Filters);
