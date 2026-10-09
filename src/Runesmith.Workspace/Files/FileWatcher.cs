namespace Runesmith.Workspace.Files;

/// <summary>A change to a file or folder that the watcher saw.</summary>
/// <param name="OldPath">The path before a rename, or null.</param>
internal readonly record struct FileChange(WatcherChangeTypes Kind, string Path, string? OldPath = null);

/// <summary>Watches a folder and reports changes in batches, so a burst of changes, such as a build or a checkout, arrives as one.</summary>
internal sealed class FileWatcher : IDisposable
{
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan Longest = TimeSpan.FromMilliseconds(500);

    private readonly FileSystemWatcher? _watcher;
    private readonly Action<IReadOnlyList<FileChange>, bool> _report;
    private readonly Timer _timer;
    private readonly Lock _gate = new();
    private List<FileChange> _pending = [];
    private bool _overflowed;
    private DateTime _firstPending;

    /// <param name="report">Receives each batch, on a background thread, and whether changes were lost so the folder must be read again.</param>
    /// <param name="filter">The file name to watch, or null for every file.</param>
    public FileWatcher(string folder, bool recursive, Action<IReadOnlyList<FileChange>, bool> report, string? filter = null)
    {
        _report = report;
        _timer = new Timer(_ => Flush());
        try
        {
            _watcher = new FileSystemWatcher(folder, filter ?? "*")
            {
                IncludeSubdirectories = recursive,
                InternalBufferSize = 64 * 1024,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            _watcher.Created += (_, e) => Add(new FileChange(WatcherChangeTypes.Created, e.FullPath));
            _watcher.Deleted += (_, e) => Add(new FileChange(WatcherChangeTypes.Deleted, e.FullPath));
            _watcher.Changed += (_, e) => Add(new FileChange(WatcherChangeTypes.Changed, e.FullPath));
            _watcher.Renamed += (_, e) => Add(new FileChange(WatcherChangeTypes.Renamed, e.FullPath, e.OldFullPath));
            _watcher.Error += (_, _) => Overflow();
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException)
        {
            // Out of watches, such as inotify's per-user limit: the folder works, it just does not follow outside changes.
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    /// <summary>Gets whether the folder is watched; it is not when the system has no watches left.</summary>
    public bool IsWatching => _watcher is not null;

    public void Dispose()
    {
        _watcher?.Dispose();
        _timer.Dispose();
    }

    private void Add(FileChange change)
    {
        lock (_gate)
        {
            if (_pending.Count == 0)
                _firstPending = DateTime.UtcNow;
            _pending.Add(change);
            ScheduleFlush();
        }
    }

    private void Overflow()
    {
        lock (_gate)
        {
            _overflowed = true;
            _firstPending = _pending.Count == 0 ? DateTime.UtcNow : _firstPending;
            ScheduleFlush();
        }
    }

    private void ScheduleFlush()
    {
        var waited = DateTime.UtcNow - _firstPending;
        _timer.Change(waited >= Longest ? TimeSpan.Zero : Quiet, Timeout.InfiniteTimeSpan);
    }

    private void Flush()
    {
        List<FileChange> changes;
        bool overflowed;
        lock (_gate)
        {
            (changes, _pending) = (_pending, []);
            (overflowed, _overflowed) = (_overflowed, false);
        }

        if (changes.Count > 0 || overflowed)
            _report(changes, overflowed);
    }
}
