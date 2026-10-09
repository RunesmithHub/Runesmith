using System.Composition;
using System.Text;
using System.Text.Json;
using Runesmith.Sdk;

namespace Runesmith.Workspace.Files;

/// <summary>A folder opened recently, and when.</summary>
/// <param name="Path">The folder's full path.</param>
/// <param name="Opened">When it was last opened, or null when that is not known.</param>
public sealed record RecentFolder(string Path, DateTimeOffset? Opened);

/// <summary>The folders opened most recently, newest first, kept between runs.</summary>
[Export(typeof(RecentFolders))]
[Shared]
public sealed class RecentFolders
{
    /// <summary>How many folders are remembered.</summary>
    public const int Capacity = 10;

    private readonly string _path;
    private readonly TimeProvider _time;
    private List<RecentFolder> _entries;

    public RecentFolders()
        : this(System.IO.Path.Combine(RunesmithPaths.State, "recent.json"), TimeProvider.System)
    {
    }

    internal RecentFolders(string path, TimeProvider time)
    {
        _path = path;
        _time = time;
        _entries = Read(path);
    }

    /// <summary>Gets the folders, newest first.</summary>
    public IReadOnlyList<string> Items => [.. _entries.Select(e => e.Path)];

    /// <summary>Gets the folders with when they were opened, newest first.</summary>
    public IReadOnlyList<RecentFolder> Entries => _entries;

    /// <summary>Raised when the list changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Puts a folder at the top of the list.</summary>
    public void Add(string folder)
    {
        folder = PathComparison.Normalize(folder);
        Update([new RecentFolder(folder, _time.GetUtcNow()), .. _entries.Where(entry => !PathComparison.Comparer.Equals(entry.Path, folder))]);
    }

    public void Remove(string folder)
    {
        folder = PathComparison.Normalize(folder);
        Update([.. _entries.Where(entry => !PathComparison.Comparer.Equals(entry.Path, folder))]);
    }

    private void Update(List<RecentFolder> entries)
    {
        _entries = entries.Count > Capacity ? entries[..Capacity] : entries;
        try
        {
            AtomicFile.WriteAllBytes(_path, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_entries, RecentJson.Default.ListRecentFolder)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The list still works for this run.
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Older versions kept only the paths.
    private static List<RecentFolder> Read(string path)
    {
        try
        {
            if (!File.Exists(path))
                return [];

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            return document.RootElement.EnumerateArray().FirstOrDefault().ValueKind == JsonValueKind.String
                ? [.. document.RootElement.Deserialize(RecentJson.Default.ListString)!.Select(p => new RecentFolder(p, null))]
                : document.RootElement.Deserialize(RecentJson.Default.ListRecentFolder) ?? [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }
}

[System.Text.Json.Serialization.JsonSourceGenerationOptions(PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase)]
[System.Text.Json.Serialization.JsonSerializable(typeof(List<string>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(List<RecentFolder>))]
internal sealed partial class RecentJson : System.Text.Json.Serialization.JsonSerializerContext;
