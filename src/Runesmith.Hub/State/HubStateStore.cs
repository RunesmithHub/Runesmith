using System.Text.Json;

namespace Runesmith.Hub.State;

/// <summary>Reads and writes the installed state. A write replaces the file in one step, so a crash leaves the old or the new state.</summary>
public sealed class HubStateStore(string path)
{
    public string Path { get; } = path;

    /// <summary>Gets why the file could not be read the last time, or null.</summary>
    public string? LoadError { get; private set; }

    /// <summary>Reads the state; a missing file is an empty state, and so is a damaged one, with <see cref="LoadError"/> set.</summary>
    public HubState Load()
    {
        LoadError = null;
        try
        {
            if (!File.Exists(Path))
                return new HubState();

            var state = JsonSerializer.Deserialize(File.ReadAllBytes(Path), HubStateJson.Default.HubState) ?? new HubState();
            return state with { Plugins = state.Plugins ?? [], Quarantined = state.Quarantined ?? [] };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            LoadError = exception.Message;
            return new HubState();
        }
    }

    /// <exception cref="IOException">The file cannot be written; the previous state stays.</exception>
    public void Save(HubState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temporary = Path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, state, HubStateJson.Default.HubState);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporary, Path, overwrite: true);
    }
}
