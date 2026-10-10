using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Runesmith.Sdk;
using Runesmith.Shell.ToolWindows;

namespace Runesmith.Shell.Session;

/// <summary>The files open in a folder when it was last closed, so reopening the folder restores them.</summary>
/// <param name="Files">The open files, in tab order, with their caret offsets.</param>
/// <param name="Active">The file whose tab was active.</param>
internal sealed record WorkspaceSession(IReadOnlyList<SessionFile> Files, string? Active);

/// <param name="Path">The file's full path.</param>
/// <param name="Caret">The caret's offset when the folder closed.</param>
internal sealed record SessionFile(string Path, int Caret)
{
    /// <summary>Gets the id of the custom editor the file was open in, or null for the text editor.</summary>
    public string? Editor { get; init; }
}

/// <summary>The size and place of the main window, and the folder that was open, for the next start.</summary>
internal sealed record WindowSession(double X, double Y, double Width, double Height, bool IsMaximized, string? LastFolder);

/// <summary>Reads and writes sessions in Runesmith's state folder; a missing or damaged session reads as none.</summary>
internal static class SessionStore
{
    private static string Folder => Path.Combine(RunesmithPaths.State, "sessions");

    private static string WindowPath => Path.Combine(RunesmithPaths.State, "window.json");

    private static string ToolWindowsPath => Path.Combine(RunesmithPaths.State, "tool-windows.json");

    public static WorkspaceSession? LoadWorkspace(string rootPath) => Read(WorkspacePath(rootPath), SessionJson.Default.WorkspaceSession);

    public static void SaveWorkspace(string rootPath, WorkspaceSession session) => Write(WorkspacePath(rootPath), session, SessionJson.Default.WorkspaceSession);

    public static WindowSession? LoadWindow() => Read(WindowPath, SessionJson.Default.WindowSession);

    public static void SaveWindow(WindowSession session) => Write(WindowPath, session, SessionJson.Default.WindowSession);

    public static ToolWindowSession? LoadToolWindows() => Read(ToolWindowsPath, SessionJson.Default.ToolWindowSession);

    public static void SaveToolWindows(ToolWindowSession session) => Write(ToolWindowsPath, session, SessionJson.Default.ToolWindowSession);

    private static string WorkspacePath(string rootPath)
    {
        var key = OperatingSystem.IsLinux() ? rootPath : rootPath.ToUpperInvariant();
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
        return Path.Combine(Folder, hash + ".json");
    }

    private static T? Read<T>(string path, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
        where T : class
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), type) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static void Write<T>(string path, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, type));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(WorkspaceSession))]
[JsonSerializable(typeof(WindowSession))]
[JsonSerializable(typeof(ToolWindowSession))]
internal sealed partial class SessionJson : JsonSerializerContext;
