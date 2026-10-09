using System.Text.Json;
using System.Text.Json.Serialization;
using Runesmith.Sdk;

namespace Runesmith.Git.Views.Clone;

/// <summary>What the clone dialog remembers between runs: the folder the last clone went in.</summary>
internal sealed record CloneMemory(string? Parent)
{
    private static readonly CloneMemory Empty = new(Parent: null);

    private static string File => Path.Combine(RunesmithPaths.State, "clone.json");

    /// <summary>Gets the folder clones go in: the last one used while it exists, else a Projects folder in the home folder, else the home folder.</summary>
    public static string DefaultParent()
    {
        if (Load().Parent is { Length: > 0 } last && Directory.Exists(last))
            return last;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var projects = Path.Combine(home, "Projects");
        return Directory.Exists(projects) ? projects : home;
    }

    public static CloneMemory Load()
    {
        try
        {
            return System.IO.File.Exists(File) ? JsonSerializer.Deserialize(System.IO.File.ReadAllText(File), CloneMemoryJson.Default.CloneMemory) ?? Empty : Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Empty;
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(RunesmithPaths.State);
            System.IO.File.WriteAllText(File, JsonSerializer.Serialize(this, CloneMemoryJson.Default.CloneMemory));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CloneMemory))]
internal sealed partial class CloneMemoryJson : JsonSerializerContext;
