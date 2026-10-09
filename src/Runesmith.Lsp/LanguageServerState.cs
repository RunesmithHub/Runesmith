namespace Runesmith.Lsp;

public enum LanguageServerState
{
    NotStarted,
    Starting,
    Running,
    ShuttingDown,
    Stopped,

    /// <summary>The server could not start or exited on its own.</summary>
    Failed,
}
