using System.Composition;
using Runesmith.Composition;
using Runesmith.Sdk.Settings;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.Workspace;
using RunesmithHub.Protocol;

namespace Runesmith.Workspace.Files;

/// <summary>Plugins' file watchers: subscriptions to the open folder's one watcher, and to one shared watcher per other folder.</summary>
[Export(typeof(IWorkspaceFiles))]
[Shared]
public sealed class WorkspaceFiles : IWorkspaceFiles, IDisposable
{
    private readonly Workspace _workspace;
    private readonly ISettingsService _settings;
    private readonly Func<PluginInfo?> _caller;
    private readonly Action<string> _log;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, SharedWatcher> _shared = new(PathComparison.Comparer);
    private volatile Subscription[] _subscriptions = [];

    [ImportingConstructor]
    public WorkspaceFiles(Workspace workspace, ISettingsService settings, [Import(AllowDefault = true)] Lazy<IOutputService>? output = null)
        : this(workspace, settings, PluginCallers.Current, line => output?.Value.GetChannel(PluginAccess.ChannelName).AppendLine(line))
    {
    }

    internal WorkspaceFiles(Workspace workspace, ISettingsService settings, Func<PluginInfo?> caller, Action<string> log)
    {
        _workspace = workspace;
        _settings = settings;
        _caller = caller;
        _log = log;
        _workspace.ChangesObserved += OnWorkspaceChanges;
        _workspace.Changed += OnWorkspaceChanged;
    }

    /// <summary>Gets how many system watchers of folders outside the open folder run for subscriptions.</summary>
    internal int SharedWatcherCount
    {
        get
        {
            lock (_gate)
                return _shared.Count;
        }
    }

    public IFileWatcher Watch(IReadOnlyList<string> globs, Action<IReadOnlyList<FileChangeEvent>> onChanged, FileWatchOptions? options = null)
    {
        var caller = _caller();
        ArgumentNullException.ThrowIfNull(globs);
        ArgumentNullException.ThrowIfNull(onChanged);
        options ??= new FileWatchOptions();

        var folder = options.Folder is { } requested ? PathComparison.Normalize(requested) : null;
        if (folder is not null && !Directory.Exists(folder))
            throw new DirectoryNotFoundException($"The folder {folder} does not exist.");
        if (folder is not null && !IsInsideOpenFolder(folder))
            PluginAccess.Demand(caller, Capabilities.FileSystem, "file watcher for folders outside the open folder", _log);

        var subscription = new Subscription(this, folder, caller is null || caller.Manifest.Allows(Capabilities.FileSystem), GlobMatcher.Parse(string.Join(';', globs)),
            GlobMatcher.Parse(string.Join(';', options.Exclude)), options.IncludeExcluded, options.Delay, onChanged, caller);
        lock (_gate)
        {
            _subscriptions = [.. _subscriptions, subscription];
            Attach(subscription);
        }

        return subscription;
    }

    public void Dispose()
    {
        _workspace.ChangesObserved -= OnWorkspaceChanges;
        _workspace.Changed -= OnWorkspaceChanged;
        lock (_gate)
        {
            foreach (var subscription in _subscriptions)
                subscription.Stop();
            _subscriptions = [];
            foreach (var shared in _shared.Values)
                shared.Watcher.Dispose();
            _shared.Clear();
        }
    }

    /// <summary>Hands a batch from a watcher to the subscriptions it serves; the workspace's watcher passes its root.</summary>
    internal void Report(string source, IReadOnlyList<FileChange> changes, bool overflowed)
    {
        foreach (var subscription in _subscriptions)
        {
            if (PathComparison.Comparer.Equals(subscription.Source, source))
                subscription.Add(changes, overflowed);
        }
    }

    private bool IsInsideOpenFolder(string folder) => _workspace.RootPath is { } root && PathComparison.IsInside(folder, root);

    private void OnWorkspaceChanges(string root, IReadOnlyList<FileChange> changes, bool overflowed) => Report(root, changes, overflowed);

    private void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            foreach (var subscription in _subscriptions)
            {
                Detach(subscription);
                Attach(subscription);
            }
        }
    }

    // Callers hold _gate.
    private void Attach(Subscription subscription)
    {
        var root = _workspace.RootPath;
        var folder = subscription.RequestedFolder ?? root;
        if (folder is null)
        {
            subscription.Attach(null, null, null, () => false);
        }
        else if (root is not null && PathComparison.IsInside(folder, root))
        {
            subscription.Attach(root, folder, _workspace.IsExcluded, () => _workspace.IsWatching);
        }
        else if (subscription.RequestedFolder is not null && subscription.MayWatchOutside && Directory.Exists(folder))
        {
            if (!_shared.TryGetValue(folder, out var shared))
            {
                var exclude = GlobMatcher.Parse(_settings.Get<string>(SettingKeys.Exclude));
                shared = _shared[folder] = new SharedWatcher(
                    new FileWatcher(folder, recursive: true, (changes, overflowed) => Report(folder, changes, overflowed)),
                    path => path.Length > folder.Length && exclude.IsMatchOrInside(GlobMatcher.ToGlobPath(Path.GetRelativePath(folder, path))));
            }

            shared.Count++;
            subscription.Attach(folder, folder, shared.IsExcluded, () => shared.Watcher.IsWatching, isShared: true);
        }
        else
        {
            subscription.Attach(null, folder, null, () => false);
        }
    }

    // Callers hold _gate.
    private void Detach(Subscription subscription)
    {
        if (subscription is { IsShared: true, Source: { } source } && _shared.TryGetValue(source, out var shared) && --shared.Count == 0)
        {
            shared.Watcher.Dispose();
            _shared.Remove(source);
        }

        subscription.Attach(null, null, null, () => false);
    }

    private void Remove(Subscription subscription)
    {
        lock (_gate)
        {
            if (!_subscriptions.Contains(subscription))
                return;

            _subscriptions = [.. _subscriptions.Where(s => s != subscription)];
            Detach(subscription);
        }
    }

    private sealed class SharedWatcher(FileWatcher watcher, Func<string, bool> isExcluded)
    {
        public FileWatcher Watcher { get; } = watcher;

        public Func<string, bool> IsExcluded { get; } = isExcluded;

        public int Count { get; set; }
    }

    private sealed class Subscription(
        WorkspaceFiles owner, string? folder, bool mayWatchOutside, GlobMatcher globs, GlobMatcher exclude, bool includeExcluded, TimeSpan delay,
        Action<IReadOnlyList<FileChangeEvent>> onChanged, PluginInfo? plugin) : IFileWatcher
    {
        private readonly Lock _gate = new();
        private readonly FileChangeCoalescer _pending = new();
        private Timer? _timer;
        private Task _delivery = Task.CompletedTask;
        private Func<string, bool>? _isExcluded;
        private Func<bool> _isSourceWatching = () => false;
        private string? _watched;
        private bool _rescan;
        private bool _stopped;
        private DateTime _firstPending;

        public string? RequestedFolder { get; } = folder;

        public bool MayWatchOutside { get; } = mayWatchOutside;

        /// <summary>Gets whether a watcher of a folder outside the open folder serves this subscription.</summary>
        public bool IsShared { get; private set; }

        /// <summary>Gets the folder of the watcher this subscription is served by, or null.</summary>
        public string? Source { get; private set; }

        string? IFileWatcher.Folder
        {
            get
            {
                lock (_gate)
                    return _watched;
            }
        }

        public bool IsWatching
        {
            get
            {
                lock (_gate)
                    return !_stopped && Source is not null && _isSourceWatching();
            }
        }

        public void Attach(string? source, string? watched, Func<string, bool>? isExcluded, Func<bool> isSourceWatching, bool isShared = false)
        {
            lock (_gate)
            {
                Source = source;
                IsShared = isShared;
                _watched = watched;
                _isExcluded = isExcluded;
                _isSourceWatching = isSourceWatching;
                _pending.Drain();
                _rescan = false;
            }
        }

        public void Add(IReadOnlyList<FileChange> changes, bool overflowed)
        {
            lock (_gate)
            {
                if (_stopped || _watched is not { } watched)
                    return;

                if (_pending.IsEmpty && !_rescan)
                    _firstPending = DateTime.UtcNow;
                if (overflowed)
                {
                    _pending.Drain();
                    _rescan = true;
                }
                else if (!_rescan)
                {
                    foreach (var change in changes)
                    {
                        if (IsInside(change.Path, watched) || (change.OldPath is { } old && IsInside(old, watched)))
                            _pending.Add(change);
                    }
                }

                if (delay <= TimeSpan.Zero || DateTime.UtcNow - _firstPending >= delay * 4)
                    Flush();
                else
                    (_timer ??= new Timer(_ => OnTimer())).Change(delay, Timeout.InfiniteTimeSpan);
            }
        }

        public void Dispose()
        {
            Stop();
            owner.Remove(this);
        }

        public void Stop()
        {
            lock (_gate)
                _stopped = true;
            _timer?.Dispose();
        }

        // Callers hold _gate.
        private void Flush()
        {
            if (_watched is not { } watched)
                return;

            List<FileChangeEvent> events;
            if (_rescan)
            {
                events = [new FileChangeEvent(FileChangeKind.Rescan, watched)];
                _rescan = false;
            }
            else
            {
                events = [.. _pending.Drain().Select(e => Filter(e, watched)).OfType<FileChangeEvent>()];
            }

            if (events.Count > 0)
                _delivery = _delivery.ContinueWith(_ => Deliver(events), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }

        private void OnTimer()
        {
            lock (_gate)
            {
                if (!_stopped)
                    Flush();
            }
        }

        private FileChangeEvent? Filter(FileChangeEvent change, string watched)
        {
            if (change.OldPath is not { } oldPath)
                return Matches(change.Path, watched) ? change : null;

            return (Matches(oldPath, watched), Matches(change.Path, watched)) switch
            {
                (true, true) => change,
                (true, false) => new FileChangeEvent(FileChangeKind.Deleted, oldPath),
                (false, true) => new FileChangeEvent(FileChangeKind.Created, change.Path),
                _ => null,
            };
        }

        private bool Matches(string path, string watched)
        {
            if (!IsInside(path, watched) || (!includeExcluded && _isExcluded is { } isExcluded && isExcluded(path)))
                return false;

            var relative = GlobMatcher.ToGlobPath(Path.GetRelativePath(watched, path));
            return !exclude.IsMatchOrInside(relative) && (globs.IsEmpty || globs.IsMatch(relative));
        }

        private void Deliver(IReadOnlyList<FileChangeEvent> events)
        {
            lock (_gate)
            {
                if (_stopped)
                    return;
            }

            try
            {
                onChanged(events);
            }
            catch (Exception exception)
            {
                var who = plugin is null ? "A file watcher" : $"{plugin.Manifest.Name} ({plugin.Manifest.Id})";
                owner._log($"{who} failed handling file changes: {exception.Message}");
            }
        }

        private static bool IsInside(string path, string folder) => path.Length > folder.Length && PathComparison.IsInside(path, folder);
    }
}
