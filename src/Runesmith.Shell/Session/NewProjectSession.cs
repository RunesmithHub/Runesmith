using System.Text.Json;
using System.Text.Json.Serialization;
using Runesmith.Sdk;

namespace Runesmith.Shell.Session;

/// <summary>What the New Project dialog remembers between uses.</summary>
/// <param name="Location">The folder the last project was created in.</param>
/// <param name="Recent">The ids of the templates used last, most recent first.</param>
/// <param name="Values">The option values the user changed, by template id and option id.</param>
/// <param name="Steps">Whether each step plugins add was on, by the step's id; a step that is not here was never shown.</param>
internal sealed record NewProjectSession(
    string? Location,
    IReadOnlyList<string> Recent,
    IReadOnlyDictionary<string, Dictionary<string, string>> Values,
    IReadOnlyDictionary<string, bool>? Steps = null)
{
    /// <summary>The most templates Recent keeps.</summary>
    public const int MaximumRecent = 5;

    /// <summary>Gets an empty session, for the first use.</summary>
    public static NewProjectSession Empty { get; } = new(null, [], new Dictionary<string, Dictionary<string, string>>());

    private static string FilePath => Path.Combine(RunesmithPaths.State, "new-project.json");

    /// <summary>Reads the session; a missing or damaged file reads as <see cref="Empty"/>.</summary>
    public static NewProjectSession Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize(File.ReadAllText(FilePath), NewProjectSessionJson.Default.NewProjectSession) ?? Empty : Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Empty;
        }
    }

    /// <summary>Writes the session, keeping the old file when the new one cannot be written.</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, NewProjectSessionJson.Default.NewProjectSession));
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Gets the session after a project was created from a template.</summary>
    public NewProjectSession WithCreated(string templateId, string location, IReadOnlyDictionary<string, string> changedValues, IReadOnlyDictionary<string, bool> steps)
    {
        var values = new Dictionary<string, Dictionary<string, string>>(Values, StringComparer.Ordinal);
        if (changedValues.Count > 0)
            values[templateId] = new Dictionary<string, string>(changedValues, StringComparer.Ordinal);
        else
            values.Remove(templateId);

        return new NewProjectSession(location, [templateId, .. Recent.Where(id => id != templateId).Take(MaximumRecent - 1)], values, steps);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(NewProjectSession))]
internal sealed partial class NewProjectSessionJson : JsonSerializerContext;
