namespace Runesmith.Lsp;

/// <summary>Converts between file paths and the <c>file://</c> URIs the protocol uses.</summary>
public static class LspUri
{
    /// <summary>Gets the URI of a file path, with the path made absolute and its special characters escaped.</summary>
    public static string FromPath(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return new Uri(Path.GetFullPath(path)).AbsoluteUri;
    }

    /// <summary>Gets the local path of a <c>file://</c> URI, or null when the URI is not a file.</summary>
    public static string? ToPath(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile ? parsed.LocalPath : null;

    /// <summary>Gets whether two URIs name the same file, ignoring differences in escaping and, on Windows, in case.</summary>
    public static bool AreSame(string first, string second)
    {
        if (string.Equals(first, second, StringComparison.Ordinal))
            return true;
        var firstPath = ToPath(first);
        var secondPath = ToPath(second);
        return firstPath is not null && secondPath is not null &&
               string.Equals(firstPath, secondPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
