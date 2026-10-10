namespace Runesmith.Dap.Protocol;

/// <summary>The arguments of the <c>initialize</c> request: who the client is and what it supports.</summary>
/// <param name="AdapterId">The id of the debug adapter, such as <c>coreclr</c>.</param>
public sealed record InitializeArguments(string AdapterId)
{
    public string? ClientId { get; init; }

    public string? ClientName { get; init; }

    public string? Locale { get; init; }

    public bool LinesStartAt1 { get; init; } = true;

    public bool ColumnsStartAt1 { get; init; } = true;

    /// <summary>Gets the format of paths, <c>path</c> or <c>uri</c>.</summary>
    public string PathFormat { get; init; } = "path";

    public bool SupportsVariableType { get; init; } = true;

    public bool SupportsVariablePaging { get; init; }

    public bool SupportsRunInTerminalRequest { get; init; }

    public bool SupportsProgressReporting { get; init; }

    public bool SupportsInvalidatedEvent { get; init; }

    public bool SupportsMemoryReferences { get; init; }

    public bool SupportsStartDebuggingRequest { get; init; }
}

/// <summary>What a debug adapter supports, from its answer to <c>initialize</c> and later <c>capabilities</c> events. Missing ones are false.</summary>
public sealed record Capabilities
{
    /// <summary>Gets capabilities that support nothing optional.</summary>
    public static Capabilities None { get; } = new();

    public bool SupportsConfigurationDoneRequest { get; init; }

    public bool SupportsFunctionBreakpoints { get; init; }

    public bool SupportsConditionalBreakpoints { get; init; }

    public bool SupportsHitConditionalBreakpoints { get; init; }

    public bool SupportsEvaluateForHovers { get; init; }

    /// <summary>Gets the kinds of exception breakpoints, such as "All exceptions" and "Unhandled exceptions".</summary>
    public IReadOnlyList<ExceptionBreakpointsFilter>? ExceptionBreakpointFilters { get; init; }

    public bool SupportsStepBack { get; init; }

    public bool SupportsSetVariable { get; init; }

    public bool SupportsRestartFrame { get; init; }

    public bool SupportsStepInTargetsRequest { get; init; }

    public bool SupportsCompletionsRequest { get; init; }

    public bool SupportsRestartRequest { get; init; }

    public bool SupportsExceptionOptions { get; init; }

    public bool SupportsValueFormattingOptions { get; init; }

    public bool SupportsExceptionInfoRequest { get; init; }

    public bool SupportTerminateDebuggee { get; init; }

    public bool SupportSuspendDebuggee { get; init; }

    public bool SupportsDelayedStackTraceLoading { get; init; }

    public bool SupportsLoadedSourcesRequest { get; init; }

    public bool SupportsLogPoints { get; init; }

    public bool SupportsTerminateThreadsRequest { get; init; }

    public bool SupportsSetExpression { get; init; }

    public bool SupportsTerminateRequest { get; init; }

    public bool SupportsCancelRequest { get; init; }

    public bool SupportsBreakpointLocationsRequest { get; init; }

    public bool SupportsSteppingGranularity { get; init; }

    public bool SupportsExceptionFilterOptions { get; init; }

    public bool SupportsSingleThreadExecutionRequests { get; init; }
}

/// <summary>A kind of exception breakpoint an adapter offers.</summary>
/// <param name="Filter">The id sent in <c>setExceptionBreakpoints</c>.</param>
/// <param name="Label">The name shown to the user.</param>
public sealed record ExceptionBreakpointsFilter(string Filter, string Label)
{
    public string? Description { get; init; }

    /// <summary>Gets whether the filter is on unless the user turns it off.</summary>
    public bool Default { get; init; }

    public bool SupportsCondition { get; init; }
}
