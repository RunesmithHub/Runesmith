namespace Runesmith.Workspace.Files;

/// <summary>How this system compares paths: ignoring case on Windows and macOS, exactly on Linux.</summary>
public static class PathComparison
{
    public static StringComparison Comparison { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static StringComparer Comparer { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Gets the full path without a trailing separator.</summary>
    public static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary>Whether <paramref name="path"/> is <paramref name="folder"/> or inside it; both must be normalized.</summary>
    public static bool IsInside(string path, string folder) =>
        path.StartsWith(folder, Comparison)
        && (path.Length == folder.Length || path[folder.Length] == Path.DirectorySeparatorChar || folder.EndsWith(Path.DirectorySeparatorChar));
}
