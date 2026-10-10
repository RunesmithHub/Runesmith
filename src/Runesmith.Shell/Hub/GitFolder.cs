namespace Runesmith.Shell.Hub;

/// <summary>What a folder's Git files say about it, read from disk without running Git.</summary>
internal static class GitFolder
{
    /// <summary>Gets whether the folder is a Git repository: it has a <c>.git</c> folder, or a <c>.git</c> file as worktrees and submodules do.</summary>
    public static bool IsRepository(string folder) =>
        Directory.Exists(Path.Combine(folder, ".git")) || File.Exists(Path.Combine(folder, ".git"));

    /// <summary>Gets the URLs of the repository's remotes, from its <c>config</c>; empty when it has none or it can't be read.</summary>
    public static IReadOnlyList<string> RemoteUrls(string folder)
    {
        try
        {
            return GitDirectory(folder) is { } directory && File.Exists(Path.Combine(directory, "config"))
                ? ParseRemoteUrls(File.ReadAllLines(Path.Combine(directory, "config")))
                : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return [];
        }
    }

    /// <summary>Gets whether any remote of the repository is on github.com.</summary>
    public static bool HasGitHubRemote(string folder) =>
        RemoteUrls(folder).Any(url => string.Equals(Host(url), "github.com", StringComparison.OrdinalIgnoreCase));

    /// <summary>Gets the <c>url</c> values of the <c>[remote "name"]</c> sections of a Git config file.</summary>
    internal static IReadOnlyList<string> ParseRemoteUrls(IEnumerable<string> lines)
    {
        var urls = new List<string>();
        var inRemote = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                inRemote = line.StartsWith("[remote ", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            var equals = line.IndexOf('=', StringComparison.Ordinal);
            if (inRemote && equals > 0 && string.Equals(line[..equals].Trim(), "url", StringComparison.OrdinalIgnoreCase))
                urls.Add(line[(equals + 1)..].Trim().Trim('"'));
        }

        return urls;
    }

    /// <summary>Gets the host of a remote URL, such as <c>https://github.com/a/b</c>, <c>ssh://git@github.com/a/b</c> or
    /// <c>git@github.com:a/b</c>; null for a local path.</summary>
    internal static string? Host(string url)
    {
        if (url.Contains("://", StringComparison.Ordinal))
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) && !uri.IsFile ? uri.Host : null;

        var colon = url.IndexOf(':', StringComparison.Ordinal);
        var slash = url.IndexOf('/', StringComparison.Ordinal);
        if (colon <= 1 || (slash >= 0 && slash < colon))
            return null;

        var host = url[..colon];
        return host[(host.LastIndexOf('@') + 1)..];
    }

    // A .git file names the real folder; a worktree's folder names the shared one, which holds the config, in commondir.
    private static string? GitDirectory(string folder)
    {
        var dotGit = Path.Combine(folder, ".git");
        if (Directory.Exists(dotGit))
            return dotGit;
        if (!File.Exists(dotGit) || File.ReadLines(dotGit).FirstOrDefault() is not { } first || !first.StartsWith("gitdir:", StringComparison.Ordinal))
            return null;

        var directory = Path.GetFullPath(first["gitdir:".Length..].Trim(), folder);
        var common = Path.Combine(directory, "commondir");
        return File.Exists(common) ? Path.GetFullPath(File.ReadAllText(common).Trim(), directory) : directory;
    }
}
