using System.Text.Json;
using System.Text.Json.Serialization;

namespace Runesmith.Lsp.Protocol;

public enum CompletionTriggerKind
{
    Invoked = 1,
    TriggerCharacter = 2,
    TriggerForIncompleteCompletions = 3,
}

public enum CompletionItemKind
{
    Text = 1,
    Method = 2,
    Function = 3,
    Constructor = 4,
    Field = 5,
    Variable = 6,
    Class = 7,
    Interface = 8,
    Module = 9,
    Property = 10,
    Unit = 11,
    Value = 12,
    Enum = 13,
    Keyword = 14,
    Snippet = 15,
    Color = 16,
    File = 17,
    Reference = 18,
    Folder = 19,
    EnumMember = 20,
    Constant = 21,
    Struct = 22,
    Event = 23,
    Operator = 24,
    TypeParameter = 25,
}

public enum InsertTextFormat
{
    PlainText = 1,
    Snippet = 2,
}

/// <summary>How completion was triggered.</summary>
public sealed record CompletionContext(CompletionTriggerKind TriggerKind, string? TriggerCharacter = null);

public sealed record CompletionParams(TextDocumentIdentifier TextDocument, Position Position, CompletionContext? Context)
    : TextDocumentPositionParams(TextDocument, Position);

/// <summary>The edit of a completion item: a plain <see cref="Protocol.TextEdit"/> has <see cref="Range"/>, an
/// <see cref="Protocol.InsertReplaceEdit"/> has <see cref="Insert"/> and <see cref="Replace"/>.</summary>
public sealed record CompletionEdit(string NewText, Range? Range, Range? Insert, Range? Replace);

/// <summary>One completion suggestion.</summary>
public sealed record CompletionItem
{
    public required string Label { get; init; }

    public CompletionItemKind? Kind { get; init; }

    public string? Detail { get; init; }

    /// <summary>Gets the documentation; plain strings arrive as plain text.</summary>
    [JsonConverter(typeof(MarkupContentConverter))]
    public MarkupContent? Documentation { get; init; }

    public string? SortText { get; init; }

    public string? FilterText { get; init; }

    public string? InsertText { get; init; }

    public InsertTextFormat? InsertTextFormat { get; init; }

    [JsonPropertyName("textEdit")]
    [JsonConverter(typeof(CompletionEditConverter))]
    public CompletionEdit? Edit { get; init; }

    public IReadOnlyList<TextEdit>? AdditionalTextEdits { get; init; }

    public IReadOnlyList<string>? CommitCharacters { get; init; }

    public bool? Preselect { get; init; }

    /// <summary>Gets data the server attached for resolving the item; send it back unchanged.</summary>
    public JsonElement? Data { get; init; }

    /// <summary>Gets the edit when it is a plain text edit.</summary>
    [JsonIgnore]
    public TextEdit? TextEdit => Edit is { Range: { } range } ? new TextEdit(range, Edit.NewText) : null;

    /// <summary>Gets the edit when it has separate insert and replace ranges.</summary>
    [JsonIgnore]
    public InsertReplaceEdit? InsertReplaceEdit => Edit is { Insert: { } insert, Replace: { } replace } ? new InsertReplaceEdit(Edit.NewText, insert, replace) : null;
}

/// <summary>Completion items; when incomplete, typing further asks the server again.</summary>
public sealed record CompletionList(bool IsIncomplete, IReadOnlyList<CompletionItem> Items)
{
    public static CompletionList Empty { get; } = new(false, []);
}
