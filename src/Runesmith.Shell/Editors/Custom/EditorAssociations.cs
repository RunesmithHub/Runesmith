using System.Text.Json;
using System.Text.Json.Serialization;
using Runesmith.Sdk;

namespace Runesmith.Shell.Editors.Custom;

/// <summary>The editor the user chose as the default for each kind of file, by pattern such as <c>*.png</c>, kept in
/// <c>editor-associations.json</c> in the settings folder.</summary>
internal sealed class EditorAssociations
{
    private readonly string path;
    private readonly Dictionary<string, string> defaults;

    public EditorAssociations(string path)
    {
        this.path = path;
        defaults = new Dictionary<string, string>(Read(path), StringComparer.OrdinalIgnoreCase);
    }

    public static string DefaultPath => Path.Combine(RunesmithPaths.Config, "editor-associations.json");

    /// <summary>Gets the editor id chosen for a pattern, or null.</summary>
    public string? Get(string pattern) => defaults.GetValueOrDefault(pattern);

    /// <summary>Chooses the default editor for a pattern, or forgets the choice with null, and saves.</summary>
    public void Set(string pattern, string? editorId)
    {
        if (editorId is null)
            defaults.Remove(pattern);
        else
            defaults[pattern] = editorId;
        Write();
    }

    private static Dictionary<string, string> Read(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), AssociationsJson.Default.DictionaryStringString) ?? [] : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private void Write()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var sorted = defaults.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(p => p.Key, p => p.Value);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(sorted, AssociationsJson.Default.DictionaryStringString));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class AssociationsJson : JsonSerializerContext;
