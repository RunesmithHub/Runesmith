using System.Text.Json;
using System.Text.Json.Serialization;

namespace Runesmith.Lsp.Protocol;

/// <summary>An occurrence of the symbol at a position; <see cref="Kind"/> is 1 for text, 2 for a read and 3 for a write.</summary>
public sealed record DocumentHighlight(Range Range, int? Kind);

/// <summary>A document and nothing else, for requests about a whole document.</summary>
public sealed record DocumentParams(TextDocumentIdentifier TextDocument);

/// <summary>A symbol of a document with the symbols inside it. Kinds are the protocol's numbers, from 1 for a file.</summary>
public sealed record DocumentSymbol(string Name, int Kind, Range Range, Range SelectionRange)
{
    public string? Detail { get; init; }

    public IReadOnlyList<int>? Tags { get; init; }

    public bool? Deprecated { get; init; }

    public IReadOnlyList<DocumentSymbol>? Children { get; init; }
}

/// <summary>A symbol and where it is, as servers without hierarchical document symbols answer, and as workspace symbols arrive. A workspace
/// symbol's location may have no range.</summary>
public sealed record SymbolInformation(string Name, int Kind, SymbolLocation Location)
{
    public string? ContainerName { get; init; }

    public IReadOnlyList<int>? Tags { get; init; }

    public bool? Deprecated { get; init; }
}

/// <summary>A location whose range a workspace symbol may leave out.</summary>
public sealed record SymbolLocation(string Uri, Range? Range);

public sealed record WorkspaceSymbolParams(string Query);

/// <summary>Lines that fold; the client hides the lines after <see cref="StartLine"/> up to <see cref="EndLine"/>.</summary>
public sealed record FoldingRange(int StartLine, int EndLine)
{
    public int? StartCharacter { get; init; }

    public int? EndCharacter { get; init; }

    /// <summary>Gets <c>comment</c>, <c>imports</c>, <c>region</c> or another kind.</summary>
    public string? Kind { get; init; }

    public string? CollapsedText { get; init; }
}

/// <summary>The names of the token types and modifiers that a server's encoded semantic tokens index into.</summary>
public sealed record SemanticTokensLegend(IReadOnlyList<string> TokenTypes, IReadOnlyList<string> TokenModifiers);

/// <summary>What a server offers for semantic tokens: the legend, and whether it answers for ranges and whole documents.</summary>
public sealed record SemanticTokensOptions(SemanticTokensLegend Legend, bool Range, bool Full);

/// <summary>Semantic tokens, each five numbers: the line delta, the start delta, the length, the type index and the modifier bits.</summary>
public sealed record SemanticTokens(IReadOnlyList<int> Data)
{
    public string? ResultId { get; init; }
}

public sealed record SemanticTokensRangeParams(TextDocumentIdentifier TextDocument, Range Range);

/// <summary>A range and the larger range around it.</summary>
public sealed record SelectionRange(Range Range, SelectionRange? Parent);

public sealed record SelectionRangeParams(TextDocumentIdentifier TextDocument, IReadOnlyList<Position> Positions);

/// <summary>A function in a call hierarchy, or a type in a type hierarchy; the server gets it back unchanged with its data.</summary>
public sealed record HierarchyItem(string Name, int Kind, string Uri, Range Range, Range SelectionRange)
{
    public string? Detail { get; init; }

    public IReadOnlyList<int>? Tags { get; init; }

    public JsonElement? Data { get; init; }
}

/// <summary>A caller and the ranges of its calls.</summary>
public sealed record CallHierarchyIncomingCall(HierarchyItem From, IReadOnlyList<Range> FromRanges);

/// <summary>A function called and the ranges of the calls, in the caller.</summary>
public sealed record CallHierarchyOutgoingCall(HierarchyItem To, IReadOnlyList<Range> FromRanges);

/// <summary>The item a call or type hierarchy request is about.</summary>
public sealed record HierarchyItemParams(HierarchyItem Item);

/// <summary>Reads the semantic tokens provider, sent as options with a legend; <c>range</c> and <c>full</c> may be flags or objects.</summary>
internal sealed class SemanticTokensOptionsConverter : JsonConverter<SemanticTokensOptions?>
{
    public override SemanticTokensOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return null;
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (!root.TryGetProperty("legend", out var legend) || legend.Deserialize(LspJsonContext.Default.SemanticTokensLegend) is not { } parsed)
            return null;

        return new SemanticTokensOptions(parsed, Offered(root, "range"), Offered(root, "full"));
    }

    public override void Write(Utf8JsonWriter writer, SemanticTokensOptions? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WritePropertyName("legend");
        JsonSerializer.Serialize(writer, value.Legend, LspJsonContext.Default.SemanticTokensLegend);
        writer.WriteBoolean("range", value.Range);
        writer.WriteBoolean("full", value.Full);
        writer.WriteEndObject();
    }

    private static bool Offered(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.Object;
}
