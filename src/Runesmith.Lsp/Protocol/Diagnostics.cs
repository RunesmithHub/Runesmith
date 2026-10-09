using System.Text.Json;
using System.Text.Json.Serialization;

namespace Runesmith.Lsp.Protocol;

public enum DiagnosticSeverity
{
    Error = 1,
    Warning = 2,
    Information = 3,
    Hint = 4,
}

public enum DiagnosticTag
{
    Unnecessary = 1,
    Deprecated = 2,
}

/// <summary>A problem in a document. A code sent as a number arrives as its decimal text.</summary>
public sealed record Diagnostic(
    Range Range,
    DiagnosticSeverity? Severity,
    [property: JsonConverter(typeof(StringOrNumberConverter))] string? Code,
    string? Source,
    string Message,
    IReadOnlyList<DiagnosticTag>? Tags)
{
    /// <summary>Gets data the server attached, which code action requests send back unchanged.</summary>
    public JsonElement? Data { get; init; }
}

/// <summary>The diagnostics of a document, which replace any it had.</summary>
public sealed record PublishDiagnosticsParams(string Uri, int? Version, IReadOnlyList<Diagnostic> Diagnostics);
