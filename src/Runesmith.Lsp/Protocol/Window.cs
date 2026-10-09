using System.Text.Json.Serialization;

namespace Runesmith.Lsp.Protocol;

public enum MessageType
{
    Error = 1,
    Warning = 2,
    Info = 3,
    Log = 4,
    Debug = 5,
}

public sealed record ShowMessageParams(MessageType Type, string Message);

public sealed record LogMessageParams(MessageType Type, string Message);

/// <summary>A step of long-running work: <see cref="Kind"/> is <c>begin</c>, <c>report</c> or <c>end</c>.</summary>
public sealed record WorkDoneProgress(string Kind, string? Title, string? Message, int? Percentage, bool? Cancellable);

/// <summary>Progress of the work with <see cref="Token"/>; numeric tokens arrive as their decimal text.</summary>
public sealed record ProgressParams([property: JsonConverter(typeof(StringOrNumberConverter))] string Token, WorkDoneProgress Value);
