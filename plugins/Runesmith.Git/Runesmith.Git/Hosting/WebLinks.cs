using Runesmith.Sdk.VersionControl;
using Runesmith.Text;

namespace Runesmith.Git.Hosting;

/// <summary>Says what a link on a hosting service should show for a file or folder of the open repository and an editor's selection.</summary>
internal static class WebLinks
{
    /// <summary>Gets the target of a file of the repository with a range of lines, or of a folder: at the current branch when it has an upstream,
    /// so the link keeps following the branch, and at the commit otherwise.</summary>
    /// <returns>The target, or null when the path is outside the repository or the repository has no commit yet.</returns>
    public static WebTarget? File(RepositoryInfo repository, string filePath, (int First, int Last)? lines, bool isDirectory = false)
    {
        ArgumentNullException.ThrowIfNull(repository);
        var relative = Path.GetRelativePath(repository.Root, filePath);
        if (Path.IsPathRooted(relative) || relative == "." || relative.StartsWith("..", StringComparison.Ordinal))
            return null;

        var reference = repository is { Branch: { } branch, Upstream: { } upstream } ? UpstreamBranch(upstream) ?? branch : repository.Head ?? repository.Branch;
        if (reference is null)
            return null;

        var path = relative.Replace('\\', '/');
        return isDirectory
            ? new WebTarget(WebTargetKind.Folder, reference, path)
            : new WebTarget(WebTargetKind.File, reference, path) { FirstLine = lines?.First, LastLine = lines?.Last };
    }

    /// <summary>Gets a repository's name as its remote URL gives it, such as <c>owner/name</c> for <c>https://host/owner/name.git</c> or
    /// <c>git@host:owner/name.git</c>.</summary>
    public static string RepositoryName(string remoteUrl)
    {
        ArgumentNullException.ThrowIfNull(remoteUrl);
        var path = Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri) && uri.Host.Length > 0 ? Uri.UnescapeDataString(uri.AbsolutePath)
            : remoteUrl.IndexOf(':', StringComparison.Ordinal) is var colon and > 0 ? remoteUrl[(colon + 1)..] : remoteUrl;
        path = path.Trim('/');
        return path.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? path[..^4] : path;
    }

    /// <summary>Gets the lines a selection covers, counted from 1; a selection ending at the start of a line leaves that line out.</summary>
    public static (int First, int Last) Lines(TextSnapshot snapshot, TextSpan selection)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var start = snapshot.GetPosition(selection.Start);
        var end = snapshot.GetPosition(selection.End);
        var last = selection.Length > 0 && end.Column == 0 && end.Line > start.Line ? end.Line - 1 : end.Line;
        return (start.Line + 1, last + 1);
    }

    // The upstream is the remote's name and the branch, such as origin/feature/login.
    private static string? UpstreamBranch(string upstream) =>
        upstream.IndexOf('/', StringComparison.Ordinal) is var slash and > 0 && slash < upstream.Length - 1 ? upstream[(slash + 1)..] : null;
}
