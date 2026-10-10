namespace Runesmith.Dap;

/// <summary>A debug adapter answered a request with an error, or could not be started or reached.</summary>
public sealed class DebugAdapterException : Exception
{
    public DebugAdapterException()
    {
    }

    public DebugAdapterException(string message)
        : base(message)
    {
    }

    public DebugAdapterException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception for an error answer.</summary>
    /// <param name="command">The request that failed, such as <c>evaluate</c>.</param>
    /// <param name="message">The error's text, for the user.</param>
    /// <param name="errorId">The adapter's id for the error, when it sent one.</param>
    public DebugAdapterException(string command, string message, int? errorId)
        : base(message)
    {
        Command = command;
        ErrorId = errorId;
    }

    /// <summary>Gets the request that failed, or null when the adapter could not be started or reached.</summary>
    public string? Command { get; }

    /// <summary>Gets the adapter's id for the error, when it sent one.</summary>
    public int? ErrorId { get; }
}
