namespace Runesmith.Shell.Web;

/// <summary>Finds the files of a plugin's web folder for the paths of its local origin, so no path reaches outside the folder.</summary>
internal static class BundledFiles
{
    /// <summary>The path of the script that gives pages the message channel; it is Runesmith's, whatever the web folder holds.</summary>
    public const string BridgePath = "/runesmith/webview.js";

    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".htm"] = "text/html; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".map"] = "application/json; charset=utf-8",
        [".txt"] = "text/plain; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".ico"] = "image/x-icon",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".ttf"] = "font/ttf",
        [".otf"] = "font/otf",
        [".wasm"] = "application/wasm",
        [".mp3"] = "audio/mpeg",
        [".mp4"] = "video/mp4",
        [".webm"] = "video/webm",
    };

    /// <summary>Gets the file extensions the local origin serves.</summary>
    public static IReadOnlyCollection<string> Extensions => Types.Keys;

    /// <summary>Gets the content type of a file the local origin serves, or null for a kind of file it does not serve.</summary>
    public static string? ContentTypeOf(string path) => Types.GetValueOrDefault(Path.GetExtension(path));

    /// <summary>Whether a decoded URL path stays inside the web folder: no <c>.</c> or <c>..</c> segments, backslashes, colons or control
    /// characters.</summary>
    public static bool IsSafePath(string path)
    {
        if (path.Length == 0 || path[0] != '/' || path.Any(c => c == '\\' || c == ':' || char.IsControl(c)))
            return false;

        return path.Split('/').Skip(1).All(segment => segment is not ("." or "..") && segment.Trim().Length == segment.Length);
    }

    /// <summary>Finds the file of a URL path, still percent-encoded as it arrived; a path ending in <c>/</c> means its <c>index.html</c>.
    /// Returns null for a path outside the folder, through a symbolic link, of a kind the origin does not serve, or that does not
    /// exist.</summary>
    public static string? Resolve(string root, string rawPath)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(rawPath);
        var end = rawPath.IndexOfAny(['?', '#']);
        string path;
        try
        {
            path = Uri.UnescapeDataString(end < 0 ? rawPath : rawPath[..end]);
        }
        catch (UriFormatException)
        {
            return null;
        }

        if (!IsSafePath(path))
            return null;
        if (path.EndsWith('/'))
            path += "index.html";
        if (ContentTypeOf(path) is null)
            return null;

        var folder = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Join(folder, path.TrimStart('/')));
        var prefix = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.Ordinal) || !File.Exists(full))
            return null;

        for (var current = full; current.Length >= prefix.Length; current = Path.GetDirectoryName(current)!)
        {
            if (new FileInfo(current).LinkTarget is not null)
                return null;
        }

        return full;
    }
}
