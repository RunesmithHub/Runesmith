using System.Text.Json;
using System.Text.Json.Serialization;

namespace Runesmith.Lsp.Protocol;

/// <summary>What a server offers for a feature it describes with a flag or an options object: null when it does not offer it.</summary>
/// <param name="ResolveProvider">Whether the server fills in details of an item in a separate request.</param>
/// <param name="PrepareProvider">Whether the server checks a rename position first.</param>
public sealed record ProviderOptions(bool ResolveProvider, bool PrepareProvider)
{
    public static ProviderOptions Default { get; } = new(false, false);
}

/// <summary>Reads a provider sent as <c>true</c>, <c>false</c> or an options object.</summary>
internal sealed class ProviderOptionsConverter : JsonConverter<ProviderOptions?>
{
    public override ProviderOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True:
                return ProviderOptions.Default;
            case JsonTokenType.StartObject:
                using (var document = JsonDocument.ParseValue(ref reader))
                {
                    var root = document.RootElement;
                    return new ProviderOptions(Flag(root, "resolveProvider"), Flag(root, "prepareProvider"));
                }

            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, ProviderOptions? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteBooleanValue(false);
            return;
        }

        writer.WriteStartObject();
        writer.WriteBoolean("resolveProvider", value.ResolveProvider);
        writer.WriteBoolean("prepareProvider", value.PrepareProvider);
        writer.WriteEndObject();
    }

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}

/// <summary>A command the server runs, with <c>workspace/executeCommand</c>.</summary>
public sealed record Command(string Title, [property: JsonPropertyName("command")] string Name, IReadOnlyList<JsonElement>? Arguments);

/// <summary>Edits to one version of a document.</summary>
public sealed record TextDocumentEdit(OptionalVersionedTextDocumentIdentifier TextDocument, IReadOnlyList<TextEdit> Edits);

/// <summary>Identifies a document and, when known, its version.</summary>
public sealed record OptionalVersionedTextDocumentIdentifier(string Uri, int? Version);

/// <summary>Changes to many documents. Edits arrive in <see cref="Changes"/> or <see cref="DocumentChanges"/>; file operations are not
/// supported, so the client declares none.</summary>
public sealed record WorkspaceEdit
{
    public IReadOnlyDictionary<string, IReadOnlyList<TextEdit>>? Changes { get; init; }

    public IReadOnlyList<TextDocumentEdit>? DocumentChanges { get; init; }

    /// <summary>Gets each document's edits, from whichever form the server sent.</summary>
    [JsonIgnore]
    public IEnumerable<(string Uri, int? Version, IReadOnlyList<TextEdit> Edits)> Documents =>
        DocumentChanges is { } changes
            ? changes.Select(change => (change.TextDocument.Uri, change.TextDocument.Version, change.Edits))
            : (Changes ?? new Dictionary<string, IReadOnlyList<TextEdit>>()).Select(pair => (pair.Key, (int?)null, pair.Value));
}

/// <summary>Why code actions are asked for.</summary>
public enum CodeActionTriggerKind
{
    Invoked = 1,
    Automatic = 2,
}

/// <summary>The kinds of code actions servers use; kinds are hierarchical, such as <c>refactor.extract</c>.</summary>
public static class CodeActionKinds
{
    public const string QuickFix = "quickfix";
    public const string Refactor = "refactor";
    public const string Source = "source";
}

public sealed record CodeActionContext(IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<string>? Only, CodeActionTriggerKind? TriggerKind);

public sealed record CodeActionParams(TextDocumentIdentifier TextDocument, Range Range, CodeActionContext Context);

/// <summary>Why a code action cannot be applied now.</summary>
public sealed record CodeActionDisabled(string Reason);

/// <summary>A quick fix or refactoring: an edit, a command, or both. A bare command a server returns arrives with only
/// <see cref="Title"/> and <see cref="Command"/>.</summary>
public sealed record CodeAction
{
    public required string Title { get; init; }

    public string? Kind { get; init; }

    public IReadOnlyList<Diagnostic>? Diagnostics { get; init; }

    public bool? IsPreferred { get; init; }

    public CodeActionDisabled? Disabled { get; init; }

    public WorkspaceEdit? Edit { get; init; }

    public Command? Command { get; init; }

    /// <summary>Gets data the server attached for resolving the action; send it back unchanged.</summary>
    public JsonElement? Data { get; init; }
}

public sealed record RenameParams(TextDocumentIdentifier TextDocument, Position Position, string NewName);

/// <summary>Where a rename starts. A server that answers with only a range leaves <see cref="Placeholder"/> null; one that answers with
/// <c>defaultBehavior</c> leaves <see cref="Range"/> null too.</summary>
public sealed record PrepareRenameResult(Range? Range, string? Placeholder);

public sealed record FormattingOptions(int TabSize, bool InsertSpaces);

public sealed record DocumentFormattingParams(TextDocumentIdentifier TextDocument, FormattingOptions Options);

public sealed record DocumentRangeFormattingParams(TextDocumentIdentifier TextDocument, Range Range, FormattingOptions Options);

public sealed record InlayHintParams(TextDocumentIdentifier TextDocument, Range Range);

public enum InlayHintKind
{
    Type = 1,
    Parameter = 2,
}

/// <summary>One part of an inlay hint's label.</summary>
public sealed record InlayHintLabelPart(string Value, [property: JsonConverter(typeof(MarkupContentConverter))] MarkupContent? Tooltip);

/// <summary>Reads an inlay hint label sent as a string or as parts, as its parts.</summary>
internal sealed class InlayHintLabelConverter : JsonConverter<IReadOnlyList<InlayHintLabelPart>>
{
    public override IReadOnlyList<InlayHintLabelPart> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return [new InlayHintLabelPart(reader.GetString() ?? "", null)];

        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.ValueKind == JsonValueKind.Array
            ? [.. document.RootElement.EnumerateArray().Select(part => part.Deserialize(LspJsonContext.Default.InlayHintLabelPart)).OfType<InlayHintLabelPart>()]
            : [];
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<InlayHintLabelPart> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var part in value)
            JsonSerializer.Serialize(writer, part, LspJsonContext.Default.InlayHintLabelPart);
        writer.WriteEndArray();
    }
}

/// <summary>Text a server wants shown inside a line, such as a parameter name.</summary>
public sealed record InlayHint
{
    public required Position Position { get; init; }

    [JsonConverter(typeof(InlayHintLabelConverter))]
    public required IReadOnlyList<InlayHintLabelPart> Label { get; init; }

    public InlayHintKind? Kind { get; init; }

    [JsonConverter(typeof(MarkupContentConverter))]
    public MarkupContent? Tooltip { get; init; }

    public bool? PaddingLeft { get; init; }

    public bool? PaddingRight { get; init; }

    public JsonElement? Data { get; init; }

    /// <summary>Gets the label's text.</summary>
    [JsonIgnore]
    public string Text => string.Concat(Label.Select(part => part.Value));
}

public sealed record CodeLensParams(TextDocumentIdentifier TextDocument);

/// <summary>A command shown above a range; servers that resolve lenses send them without a command first.</summary>
public sealed record CodeLens(Range Range, Command? Command, JsonElement? Data);

public sealed record ExecuteCommandParams(string Command, IReadOnlyList<JsonElement>? Arguments);

/// <summary>A server's request that the client apply an edit, such as from a command it ran.</summary>
public sealed record ApplyWorkspaceEditParams(string? Label, WorkspaceEdit Edit);

public sealed record ApplyWorkspaceEditResult(bool Applied, string? FailureReason = null);
