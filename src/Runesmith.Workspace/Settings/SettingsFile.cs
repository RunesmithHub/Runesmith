using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Runesmith.Workspace.Files;

namespace Runesmith.Workspace.Settings;

/// <summary>One settings file: a JSON object of keys and values, which may have comments and trailing commas.</summary>
internal sealed class SettingsFile(string path)
{
    private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public string Path { get; } = path;

    /// <summary>Gets the values as read, including keys no loaded part knows, so saving keeps them.</summary>
    public JsonObject Values { get; private set; } = [];

    /// <summary>Gets why the file could not be read, or null when it was read or does not exist.</summary>
    public string? Error { get; private set; }

    public void Load()
    {
        string text;
        try
        {
            text = File.Exists(Path) ? File.ReadAllText(Path) : "";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Error = exception.Message;
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            Values = [];
            Error = null;
            return;
        }

        try
        {
            Values = JsonNode.Parse(text, documentOptions: ReadOptions) as JsonObject
                ?? throw new JsonException("The file must hold a JSON object, such as { \"editor.fontSize\": 14 }.");
            Error = null;
        }
        catch (JsonException exception)
        {
            Error = exception.Message;
        }
    }

    /// <exception cref="InvalidOperationException">The file could not be read, so writing would lose what it holds.</exception>
    public void Save()
    {
        if (Error is not null)
            throw new InvalidOperationException($"{Path} could not be read, so it is not changed: {Error} Fix the file and try again.");

        var sorted = new JsonObject();
        foreach (var (key, value) in Values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            sorted[key] = value?.DeepClone();
        AtomicFile.WriteAllBytes(Path, Encoding.UTF8.GetBytes(sorted.ToJsonString(WriteOptions) + "\n"));
    }
}
