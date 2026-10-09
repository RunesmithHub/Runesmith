using System.Composition;
using Runesmith.Sdk.Messaging;
using Runesmith.Sdk.Settings;
using Runesmith.Sdk.Workspace;

namespace Runesmith.Workspace.Files;

/// <summary>The open folder: its solutions, its files and the changes made to them on disk.</summary>
[Export(typeof(IWorkspace))]
[Shared]
public sealed class Workspace : IWorkspace, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IMessageBus _messageBus;
    private readonly RecentFolders _recentFolders;
    private GlobMatcher _exclude;
    private FileIndex? _index;
    private FileWatcher? _watcher;

    [ImportingConstructor]
    public Workspace(ISettingsService settings, IMessageBus messageBus, RecentFolders recentFolders)
    {
        _settings = settings;
        _messageBus = messageBus;
        _recentFolders = recentFolders;
        _exclude = GlobMatcher.Parse(settings.Get<string>(SettingKeys.Exclude));
        settings.Changed += OnSettingChanged;
    }

    public string? RootPath { get; private set; }

    public string? Name => RootPath is null ? null : Path.GetFileName(RootPath) is { Length: > 0 } name ? name : RootPath;

    public IReadOnlyList<string> Solutions { get; private set; } = [];

    /// <summary>Gets whether changes made on disk outside Runesmith are followed; they are not when the system has no file watches left.</summary>
    public bool IsWatching => _watcher?.IsWatching ?? false;

    public event EventHandler? Changed;

    public Task OpenAsync(string folderPath)
    {
        var root = PathComparison.Normalize(folderPath);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"The folder {root} does not exist.");

        Unload();
        RootPath = root;
        Solutions = FindSolutions(root);
        Watch(root);
        _recentFolders.Add(root);
        AnnounceChange();
        return Task.CompletedTask;
    }

    public void Close()
    {
        if (RootPath is null)
            return;

        Unload();
        AnnounceChange();
    }

    public bool Contains(string path)
    {
        if (RootPath is null || _index is null)
            return false;

        var full = PathComparison.Normalize(path);
        return PathComparison.IsInside(full, RootPath) && !_index.IsExcluded(full);
    }

    public Task<IReadOnlyList<string>> GetFilesAsync(CancellationToken cancellationToken = default) =>
        _index?.GetFilesAsync(cancellationToken) ?? Task.FromResult<IReadOnlyList<string>>([]);

    public void Dispose()
    {
        _settings.Changed -= OnSettingChanged;
        _watcher?.Dispose();
    }

    private void Unload()
    {
        _watcher?.Dispose();
        _watcher = null;
        _index = null;
        RootPath = null;
        Solutions = [];
    }

    private void Watch(string root)
    {
        _watcher?.Dispose();
        var index = _index = new FileIndex(root, _exclude);
        index.Start();
        _watcher = new FileWatcher(root, recursive: true, (changes, overflowed) => OnFilesChanged(root, index, changes, overflowed));
    }

    private void AnnounceChange()
    {
        var root = RootPath;
        UiThread.Run(() =>
        {
            Changed?.Invoke(this, EventArgs.Empty);
            _messageBus.Publish(new WorkspaceOpenedMessage(root));
        });
    }

    private void OnFilesChanged(string root, FileIndex index, IReadOnlyList<FileChange> changes, bool overflowed)
    {
        if (index != _index)
            return;

        if (overflowed)
            index.Invalidate();
        else
            index.Apply(changes);

        // Losing changes means anything may have changed, which the root stands for.
        string[] paths = overflowed
            ? [root]
            : [.. changes.SelectMany(c => c.OldPath is null ? [c.Path] : new[] { c.OldPath, c.Path })
                .Where(path => !index.IsExcluded(path)).Distinct(PathComparison.Comparer)];
        if (paths.Length > 0)
            UiThread.Run(() => _messageBus.Publish(new FilesChangedMessage(paths)));
    }

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.Key != SettingKeys.Exclude)
            return;

        _exclude = GlobMatcher.Parse(_settings.Get<string>(SettingKeys.Exclude));
        if (RootPath is { } root)
        {
            Watch(root);
            _messageBus.Publish(new FilesChangedMessage([root]));
        }
    }

    private static string[] FindSolutions(string root)
    {
        try
        {
            string[] solutions = [.. Directory.EnumerateFiles(root, "*.slnx"), .. Directory.EnumerateFiles(root, "*.sln")];
            if (solutions.Length == 0)
                solutions = [.. Directory.EnumerateFiles(root, "*.csproj")];
            Array.Sort(solutions, NaturalComparer.Instance);
            return solutions;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
