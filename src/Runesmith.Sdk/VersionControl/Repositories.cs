namespace Runesmith.Sdk.VersionControl;

/// <summary>A remote of a repository.</summary>
/// <param name="Name">Its name, such as <c>origin</c>.</param>
/// <param name="FetchUrl">The URL it fetches from.</param>
/// <param name="PushUrl">The URL it pushes to.</param>
public sealed record GitRemote(string Name, string FetchUrl, string PushUrl);

/// <summary>The repository of the open folder as it is now.</summary>
/// <param name="Root">The repository's top folder.</param>
/// <param name="Branch">The current branch, or null when HEAD is detached.</param>
/// <param name="Head">The commit HEAD points at, or null in a repository without commits.</param>
/// <param name="Upstream">The branch the current one tracks, such as <c>origin/main</c>, or null.</param>
/// <param name="Ahead">How many commits the current branch has that its upstream does not.</param>
/// <param name="Behind">How many commits its upstream has that the current branch does not.</param>
/// <param name="Remotes">The repository's remotes.</param>
public sealed record RepositoryInfo(string Root, string? Branch, string? Head, string? Upstream, int Ahead, int Behind, IReadOnlyList<GitRemote> Remotes);

/// <summary>Follows the Git repository of the open folder; the Git plugin provides it.</summary>
public interface IRepositoryService
{
    /// <summary>Gets the repository of the open folder, or null when the folder is not in one or none is open.</summary>
    RepositoryInfo? Current { get; }

    /// <summary>Reads the repository's state again now, such as after a command changed it.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>Raised on the UI thread when <see cref="Current"/> changed.</summary>
    event EventHandler? Changed;
}
