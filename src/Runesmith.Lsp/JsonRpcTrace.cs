namespace Runesmith.Lsp;

/// <summary>Whether a traced message was sent or received.</summary>
public enum JsonRpcDirection
{
    Sent,
    Received,
}

/// <summary>A message on a <see cref="JsonRpcConnection"/>, for logging: its method when it has one, its id when it is a request or a
/// response, and its JSON.</summary>
public sealed record JsonRpcTrace(JsonRpcDirection Direction, string? Method, string? Id, string Json);
