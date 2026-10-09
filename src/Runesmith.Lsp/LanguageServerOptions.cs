using System.Text.Json;

namespace Runesmith.Lsp;

/// <summary>How to start a language server and what to tell it when it starts.</summary>
public sealed record LanguageServerOptions
{
    /// <summary>Gets the server's executable, a path or a command found on <c>PATH</c>.</summary>
    public required string Command { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>Gets the folder the server works on, sent as the root and the only workspace folder.</summary>
    public required string RootPath { get; init; }

    /// <summary>Gets the server's working directory; defaults to <see cref="RootPath"/>.</summary>
    public string? WorkingDirectory { get; init; }

    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();

    /// <summary>Gets the server-specific <c>initializationOptions</c>.</summary>
    public JsonElement? InitializationOptions { get; init; }

    /// <summary>Gets the answer to <c>workspace/configuration</c> per section; unknown sections get null.</summary>
    public IReadOnlyDictionary<string, JsonElement> Configuration { get; init; } = new Dictionary<string, JsonElement>();

    /// <summary>Gets how long a server may take to exit after <c>shutdown</c> and <c>exit</c> before it is killed.</summary>
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public string ClientName { get; init; } = "Runesmith";

    public string? ClientVersion { get; init; }
}
