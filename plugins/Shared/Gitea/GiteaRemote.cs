using System.Globalization;
using System.Text;
using Runesmith.Sdk.VersionControl;

namespace Runesmith.Plugins.Gitea;

/// <summary>A repository on a Forgejo or Gitea server, named by its owner and name.</summary>
internal sealed record GiteaRemote(GiteaServer Server, string Owner, string Name)
{
    /// <summary>Gets the repository's page.</summary>
    public Uri WebUri => Server.Page($"{Uri.EscapeDataString(Owner)}/{Uri.EscapeDataString(Name)}");

    /// <summary>Reads a remote URL of a repository on the server: HTTPS ones under the server's address, with its port and path, and SSH ones on
    /// its host, such as <c>git@codeberg.org:alice/notes.git</c> or <c>ssh://git@git.example.com:2222/alice/notes.git</c>.</summary>
    /// <returns>The repository, or null when the URL is not one of the server's.</returns>
    public static GiteaRemote? Parse(GiteaServer server, string? url)
    {
        ArgumentNullException.ThrowIfNull(server);
        if (string.IsNullOrWhiteSpace(url))
            return null;

        url = url.Trim();
        string path;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" or "ssh" or "git+ssh" or "ssh+git")
        {
            if (!uri.IdnHost.Equals(server.Host, StringComparison.OrdinalIgnoreCase))
                return null;

            path = uri.AbsolutePath;
            if (uri.Scheme is "https" or "http")
            {
                if (!SamePort(uri, server.BaseUri) || !StartsWithPrefix(path, server.PathPrefix))
                    return null;
                path = path[server.PathPrefix.Length..];
            }
            else if (server.PathPrefix.Length > 0 && StartsWithPrefix(path, server.PathPrefix))
            {
                path = path[server.PathPrefix.Length..];
            }
        }
        else if (url.IndexOf(':', StringComparison.Ordinal) is var colon and > 0 && !url.Contains("://", StringComparison.Ordinal))
        {
            // scp-like SSH: [user@]host:owner/name.git
            var host = url[..colon];
            if (!host[(host.LastIndexOf('@') + 1)..].Equals(server.Host, StringComparison.OrdinalIgnoreCase))
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
        return name.Length == 0 ? null : new GiteaRemote(server, parts[0], name);
    }

    /// <summary>Whether a remote URL is an HTTPS (or, for a server without HTTPS, HTTP) one under the server's address, the only kind the
    /// account's token is given to.</summary>
    public static bool IsWebRemote(GiteaServer server, string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == server.BaseUri.Scheme && Parse(server, url) is not null;

    /// <summary>Gets the web page of something in the repository.</summary>
    public Uri WebUrl(WebTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var link = new StringBuilder(WebUri.AbsoluteUri);
        switch (target.Kind)
        {
            case WebTargetKind.Commit when target.Revision is { Length: > 0 } commit:
                link.Append("/commit/").Append(Uri.EscapeDataString(commit));
                break;
            case WebTargetKind.Branch when target.Revision is { Length: > 0 } branch:
                link.Append("/src/branch/").Append(EscapePath(branch));
                break;
            case WebTargetKind.File or WebTargetKind.Folder when target.Revision is { Length: > 0 } revision:
                link.Append(IsCommit(revision) ? "/src/commit/" : "/src/branch/").Append(EscapePath(revision));
                if (target.Path is { Length: > 0 } path && EscapePath(path.Replace('\\', '/')) is { Length: > 0 } escaped)
                    link.Append('/').Append(escaped);
                if (target.Kind == WebTargetKind.File && target.FirstLine is { } first)
                {
                    link.Append("#L").Append(first.ToString(CultureInfo.InvariantCulture));
                    if (target.LastLine is { } last && last > first)
                        link.Append("-L").Append(last.ToString(CultureInfo.InvariantCulture));
                }

                break;
        }

        return new Uri(link.ToString());
    }

    /// <summary>Whether a revision is a full commit hash rather than a branch name, since the two have different pages.</summary>
    internal static bool IsCommit(string revision) => revision.Length is 40 or 64 && revision.All(char.IsAsciiHexDigit);

    private static bool SamePort(Uri remote, Uri server) =>
        remote.Port == server.Port || (remote.IsDefaultPort && server.IsDefaultPort);

    private static bool StartsWithPrefix(string path, string prefix) =>
        prefix.Length == 0 || (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && path.Length > prefix.Length && path[prefix.Length] == '/');

    private static string EscapePath(string path) => string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
}
