using System.Text.RegularExpressions;
using Runesmith.Git.Git;
using Runesmith.Sdk.VersionControl;

namespace Runesmith.Git.Clone;

/// <summary>Clones repositories with <c>git clone</c>, reporting Git's own progress.</summary>
internal static partial class RepositoryCloner
{
    /// <summary>Whether text looks like a Git URL: <c>https</c>, <c>http</c>, <c>ssh</c>, <c>git</c> or <c>file</c> URLs, or the
    /// <c>user@host:path</c> form of SSH.</summary>
    public static bool IsValidUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Any(char.IsWhiteSpace))
            return false;

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" or "ssh" or "git" or "file")
            return uri.IsFile || (uri.Host.Length > 0 && uri.AbsolutePath.Trim('/').Length > 0);

        return ScpPattern().IsMatch(url);
    }

    /// <summary>Gets the folder name Git gives a clone of a URL: its last part without <c>.git</c>.</summary>
    public static string? FolderName(string? url)
    {
        if (!IsValidUrl(url))
            return null;

        var path = url!.TrimEnd('/', '\\');
        var cut = path.LastIndexOfAny(['/', ':', '\\']);
        var name = cut >= 0 ? path[(cut + 1)..] : path;
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return name.Length > 0 && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 ? name : null;
    }

    /// <summary>Whether a folder exists and has something in it, so Git would refuse to clone into it.</summary>
    public static bool IsOccupied(string folder) => Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any();

    /// <summary>Clones a repository into a folder, creating its parent; a cancelled or failed clone leaves no folder behind.</summary>
    /// <exception cref="GitException">Git failed; the message is Git's own.</exception>
    public static async Task CloneAsync(string url, string target, GitCredentials? credentials, IProgress<CloneStep> progress, CancellationToken cancellationToken)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(target))!;
        Directory.CreateDirectory(parent);
        var existed = Directory.Exists(target);
        try
        {
            await GitProcess.RunCheckedAsync(parent, ["clone", "--progress", "--", url, target], cancellationToken, credentials,
                new LineProgress(line => progress.Report(CloneProgress.Parse(line)))).ConfigureAwait(false);
        }
        catch when (!existed)
        {
            TryDelete(target);
            throw;
        }
    }

    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9._-]+@[A-Za-z0-9.-]+:[^\s]+$")]
    private static partial Regex ScpPattern();

    // Reports on Git's reading thread, so lines arrive in order; the receiver moves them to the UI thread.
    private sealed class LineProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
}
