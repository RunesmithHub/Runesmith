namespace Runesmith.Dap;

/// <summary>Which way a traced message went.</summary>
public enum DapDirection
{
    Sent,
    Received,
}

/// <summary>A message sent to or received from a debug adapter, as JSON, for logs.</summary>
public sealed record DapTrace(DapDirection Direction, string Json);
