using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Runesmith.Lsp.Protocol;

/// <summary>Reads a string or a number as a string, and writes it back as a string.</summary>
internal sealed class StringOrNumberConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.String => reader.GetString(),
        JsonTokenType.Number => reader.TryGetInt64(out var whole) ? whole.ToString(CultureInfo.InvariantCulture) : reader.GetDouble().ToString(CultureInfo.InvariantCulture),
        JsonTokenType.Null => null,
        _ => throw new JsonException($"Expected a string or a number, not {reader.TokenType}."),
    };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value);
    }
}

/// <summary>Reads a provider sent as <c>true</c>, <c>false</c> or an options object as a flag.</summary>
internal sealed class FlagOrOptionsConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True:
                return true;
            case JsonTokenType.False or JsonTokenType.Null:
                return false;
            default:
                reader.Skip();
                return true;
        }
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) => writer.WriteBooleanValue(value);
}

/// <summary>Reads documentation sent as a plain string or as markup content.</summary>
internal sealed class MarkupContentConverter : JsonConverter<MarkupContent?>
{
    public override MarkupContent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
        if (reader.TokenType == JsonTokenType.String)
            return new MarkupContent(MarkupKind.PlainText, reader.GetString() ?? "");
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        return new MarkupContent(root.TryGetProperty("kind", out var kind) ? kind.GetString() ?? MarkupKind.PlainText : MarkupKind.PlainText,
            root.TryGetProperty("value", out var value) ? value.GetString() ?? "" : "");
    }

    public override void Write(Utf8JsonWriter writer, MarkupContent? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("kind", value.Kind);
        writer.WriteString("value", value.Value);
        writer.WriteEndObject();
    }
}

/// <summary>Reads hover contents sent as markup content, a marked string or an array of marked strings, as one piece of markup.</summary>
internal sealed class HoverContentsConverter : JsonConverter<MarkupContent>
{
    public override MarkupContent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        switch (root.ValueKind)
        {
            case JsonValueKind.String:
                return new MarkupContent(MarkupKind.Markdown, root.GetString() ?? "");
            case JsonValueKind.Array:
                var builder = new StringBuilder();
                foreach (var item in root.EnumerateArray())
                {
                    if (builder.Length > 0)
                        builder.Append("\n\n");
                    builder.Append(MarkedString(item));
                }

                return new MarkupContent(MarkupKind.Markdown, builder.ToString());
            case JsonValueKind.Object when root.TryGetProperty("kind", out var kind):
                return new MarkupContent(kind.GetString() ?? MarkupKind.PlainText, root.TryGetProperty("value", out var value) ? value.GetString() ?? "" : "");
            case JsonValueKind.Object:
                return new MarkupContent(MarkupKind.Markdown, MarkedString(root));
            default:
                return new MarkupContent(MarkupKind.PlainText, "");
        }
    }

    public override void Write(Utf8JsonWriter writer, MarkupContent value, JsonSerializerOptions options) =>
        new MarkupContentConverter().Write(writer, value, options);

    private static string MarkedString(JsonElement element) =>
        element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? ""
            : $"```{(element.TryGetProperty("language", out var language) ? language.GetString() : "")}\n{(element.TryGetProperty("value", out var value) ? value.GetString() : "")}\n```";
}

/// <summary>Reads a parameter label sent as a string or as <c>[start, end]</c> offsets.</summary>
internal sealed class ParameterLabelConverter : JsonConverter<ParameterLabel>
{
    public override ParameterLabel Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return new ParameterLabel(reader.GetString(), -1, -1);
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        return root.ValueKind == JsonValueKind.Array && root.GetArrayLength() == 2
            ? new ParameterLabel(null, root[0].GetInt32(), root[1].GetInt32())
            : new ParameterLabel("", -1, -1);
    }

    public override void Write(Utf8JsonWriter writer, ParameterLabel value, JsonSerializerOptions options)
    {
        if (value.Text is not null)
        {
            writer.WriteStringValue(value.Text);
            return;
        }

        writer.WriteStartArray();
        writer.WriteNumberValue(value.Start);
        writer.WriteNumberValue(value.End);
        writer.WriteEndArray();
    }
}

/// <summary>Reads the text document sync capability, sent as a kind or an options object.</summary>
internal sealed class TextDocumentSyncConverter : JsonConverter<TextDocumentSyncOptions?>
{
    public override TextDocumentSyncOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
        if (reader.TokenType == JsonTokenType.Number)
        {
            var kind = (TextDocumentSyncKind)reader.GetInt32();
            return new TextDocumentSyncOptions(kind != TextDocumentSyncKind.None, kind, false, false);
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var openClose = root.TryGetProperty("openClose", out var open) && open.ValueKind == JsonValueKind.True;
        var change = root.TryGetProperty("change", out var changeKind) && changeKind.ValueKind == JsonValueKind.Number
            ? (TextDocumentSyncKind)changeKind.GetInt32()
            : TextDocumentSyncKind.None;
        var save = false;
        var includeText = false;
        if (root.TryGetProperty("save", out var saveElement))
        {
            save = saveElement.ValueKind is JsonValueKind.True or JsonValueKind.Object;
            includeText = saveElement.ValueKind == JsonValueKind.Object && saveElement.TryGetProperty("includeText", out var include) && include.ValueKind == JsonValueKind.True;
        }

        return new TextDocumentSyncOptions(openClose, change, save, includeText);
    }

    public override void Write(Utf8JsonWriter writer, TextDocumentSyncOptions? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteBoolean("openClose", value.OpenClose);
        writer.WriteNumber("change", (int)value.Change);
        writer.WriteStartObject("save");
        writer.WriteBoolean("includeText", value.SaveIncludesText);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }
}

/// <summary>Reads a completion item's edit, sent as a text edit or an insert and replace edit.</summary>
internal sealed class CompletionEditConverter : JsonConverter<CompletionEdit?>
{
    public override CompletionEdit? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var text = root.TryGetProperty("newText", out var newText) ? newText.GetString() ?? "" : "";
        return new CompletionEdit(text, RangeOf(root, "range", options), RangeOf(root, "insert", options), RangeOf(root, "replace", options));
    }

    public override void Write(Utf8JsonWriter writer, CompletionEdit? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("newText", value.NewText);
        WriteRange(writer, "range", value.Range, options);
        WriteRange(writer, "insert", value.Insert, options);
        WriteRange(writer, "replace", value.Replace, options);
        writer.WriteEndObject();
    }

    private static Range? RangeOf(JsonElement root, string name, JsonSerializerOptions options) =>
        root.TryGetProperty(name, out var element) ? element.Deserialize(LspJsonContext.Default.Range) : null;

    private static void WriteRange(Utf8JsonWriter writer, string name, Range? range, JsonSerializerOptions options)
    {
        if (range is null)
            return;
        writer.WritePropertyName(name);
        JsonSerializer.Serialize(writer, range, LspJsonContext.Default.Range);
    }
}
