namespace Runesmith.Sdk.Workspace;

/// <summary>What happened to a file or folder that a <see cref="IFileWatcher"/> reports.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
public enum FileChangeKind
{
    /// <summary>The file or folder was created, or moved into the watched folder.</summary>
    Created,

    /// <summary>The file's content or size changed.</summary>
    Changed,

    /// <summary>The file or folder was deleted, or moved out of the watched folder.</summary>
    Deleted,

    /// <summary>The file or folder was renamed or moved inside the watched folder; <see cref="FileChangeEvent.OldPath"/> is where it was.</summary>
    Renamed,

    /// <summary>The system dropped changes, so anything in the folder may have changed: read again what you keep about it.
    /// <see cref="FileChangeEvent.Path"/> is the watched folder.</summary>
    Rescan,
}

/// <summary>A change to a file or folder that a <see cref="IFileWatcher"/> reports.</summary>
/// <param name="Path">The full path of the file or folder; for <see cref="FileChangeKind.Renamed"/> its new path, and for
/// <see cref="FileChangeKind.Rescan"/> the watched folder.</param>
/// <remarks>Added in plugin API 0.1.2.</remarks>
public sealed record FileChangeEvent(FileChangeKind Kind, string Path)
{
    /// <summary>Gets the full path before a rename, or null for the other kinds.</summary>
    public string? OldPath { get; init; }
}

/// <summary>Which changes a <see cref="IFileWatcher"/> reports and how.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
public sealed record FileWatchOptions
{
    /// <summary>Gets the folder to watch, or null for the open folder, followed as the user opens other folders. A folder outside the open
    /// folder needs the <c>filesystem</c> capability.</summary>
    public string? Folder { get; init; }

    /// <summary>Gets globs of files and folders to leave out, relative to the watched folder, on top of <c>files.exclude</c>.</summary>
    public IReadOnlyList<string> Exclude { get; init; } = [];

    /// <summary>Gets whether to report changes in what <c>files.exclude</c> leaves out too, such as <c>bin</c> and <c>.git</c>.</summary>
    public bool IncludeExcluded { get; init; }

    /// <summary>Gets how long to wait for more changes after the last one before reporting a batch; the default reports each batch the
    /// workspace watcher makes, about 100 milliseconds after a burst of changes ends.</summary>
    public TimeSpan Delay { get; init; }
}

/// <summary>A subscription to the changes in a folder, from <see cref="IWorkspaceFiles.Watch"/>. Dispose it to stop.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
public interface IFileWatcher : IDisposable
{
    /// <summary>Gets the watched folder, or null while it follows the open folder and none is open.</summary>
    string? Folder { get; }

    /// <summary>Gets whether changes are reported now. They are not after <see cref="IDisposable.Dispose"/>, while no folder is open, when
    /// the system has no file watches left, or when the open folder changed so that a plugin without the <c>filesystem</c> capability
    /// would watch a folder outside it.</summary>
    bool IsWatching { get; }
}

/// <summary>Follows changes to files on disk, such as a build writing output or the user switching branches.</summary>
/// <remarks>Added in plugin API 0.1.2. Runesmith watches the open folder once, with one system watcher, and shares it with every
/// subscription; another folder gets one system watcher however many subscriptions it has.</remarks>
public interface IWorkspaceFiles
{
    /// <summary>Reports the changes to the files that match <paramref name="globs"/> to <paramref name="onChanged"/>, in batches, on a
    /// background thread, one batch at a time and in order.</summary>
    /// <param name="globs">Globs relative to the watched folder, such as <c>**/*.csproj</c> or <c>src/**</c>; empty for every file and
    /// folder. <c>**</c> matches any number of folders, <c>*</c> any part of a name and <c>?</c> one character; a glob without a <c>/</c>
    /// matches a name at any depth. A rename matches when its old or new path does: one that leaves the globs is reported as a
    /// <see cref="FileChangeKind.Deleted"/> and one that enters them as a <see cref="FileChangeKind.Created"/>.</param>
    /// <param name="onChanged">Receives each batch. A batch has at most one event per path: a file created and then changed is
    /// <see cref="FileChangeKind.Created"/>, one created and then deleted is not reported, and a rename followed by another is one
    /// rename. Renaming, moving or deleting a folder reports the folder and each file Runesmith knew to be inside it.</param>
    /// <param name="options">The folder, excludes and delay, or null for the open folder and the defaults.</param>
    /// <exception cref="UnauthorizedAccessException">The folder is outside the open folder and the calling plugin did not declare the
    /// <c>filesystem</c> capability.</exception>
    /// <exception cref="DirectoryNotFoundException"><see cref="FileWatchOptions.Folder"/> does not exist.</exception>
    IFileWatcher Watch(IReadOnlyList<string> globs, Action<IReadOnlyList<FileChangeEvent>> onChanged, FileWatchOptions? options = null);
}
