using System.Text;

namespace Runesmith.GitHub.GitHub;

/// <summary>A repository on github.com, named by its owner and name.</summary>
internal sealed record GitHubRemote(string Owner, string Name)
{
    private const string Host = "github.com";

    /// <summary>Gets the repository's page.</summary>
    public Uri WebUri => new($"https://{Host}/{Uri.EscapeDataString(Owner)}/{Uri.EscapeDataString(Name)}");

    /// <summary>Reads a remote URL on github.com: <c>https://github.com/owner/repo.git</c>, <c>git@github.com:owner/repo.git</c> or
    /// <c>ssh://git@github.com/owner/repo</c>; null for other hosts.</summary>
    public static GitHubRemote? Parse(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        url = url.Trim();
        string path;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" or "ssh" or "git")
        {
            if (!uri.Host.Equals(Host, StringComparison.OrdinalIgnoreCase))
                return null;
            path = uri.AbsolutePath;
        }
        else if (url.IndexOf(':', StringComparison.Ordinal) is var colon and > 0 && !url.Contains("://", StringComparison.Ordinal))
        {
            var host = url[..colon];
            var at = host.LastIndexOf('@');
            if (!host[(at + 1)..].Equals(Host, StringComparison.OrdinalIgnoreCase))
                return null;
            path = url[(colon + 1)..];
        }
        else
        {
            return null;
        }

        var parts = Uri.UnescapeDataString(path).Trim('/').Split('/');
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
            return null;

        var name = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];
        return name.Length == 0 ? null : new GitHubRemote(parts[0], name);
    }

    /// <summary>Whether a remote URL is an <c>https</c> one on github.com, the only kind the signed-in token is given to.</summary>
    public static bool IsHttps(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host.Equals(Host, StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets the page of a commit.</summary>
    public Uri CommitUri(string sha) => new(WebUri.AbsoluteUri + "/commit/" + Uri.EscapeDataString(sha));

    /// <summary>Gets the page of a branch.</summary>
    public Uri BranchUri(string branch) => new(WebUri.AbsoluteUri + "/tree/" + EscapePath(branch));

    /// <summary>Gets the page of a folder at a branch, tag or commit.</summary>
    public Uri FolderUri(string reference, string path) =>
        new(new StringBuilder(WebUri.AbsoluteUri).Append("/tree/").Append(EscapePath(reference)).Append('/').Append(EscapePath(path.Replace('\\', '/'))).ToString());

    /// <summary>Gets the link to a file at a branch or commit, with a range of lines when given, as GitHub's blob pages take it.</summary>
    /// <param name="reference">A branch name or a commit hash.</param>
    /// <param name="path">The file's path from the repository's top folder, with either kind of slash.</param>
    /// <param name="firstLine">The first line, from 1, or null for the whole file.</param>
    /// <param name="lastLine">The last line, from 1.</param>
    public Uri FileUri(string reference, string path, int? firstLine = null, int? lastLine = null)
    {
        var link = new StringBuilder(WebUri.AbsoluteUri).Append("/blob/").Append(EscapePath(reference)).Append('/').Append(EscapePath(path.Replace('\\', '/')));
        if (firstLine is { } first)
        {
            link.Append("#L").Append(first.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (lastLine is { } last && last > first)
                link.Append("-L").Append(last.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return new Uri(link.ToString());
    }

    private static string EscapePath(string path) => string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
}
