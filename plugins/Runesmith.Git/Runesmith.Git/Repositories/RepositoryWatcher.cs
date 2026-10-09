namespace Runesmith.Git.Repositories;

/// <summary>Watches a repository's Git folder for the files whose changes change its status: the index, HEAD, the references and the
/// configuration. Changes to the working tree come from the workspace's own watcher.</summary>
internal sealed class RepositoryWatcher : IDisposable
{
    private static readonly HashSet<string> TopFiles = new(StringComparer.Ordinal)
    {
        "index", "HEAD", "packed-refs", "config", "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "ORIG_HEAD", "FETCH_HEAD",
    };

    private readonly List<FileSystemWatcher> watchers = [];

    /// <summary>Starts watching a Git folder.</summary>
    /// <param name="changed">Called, on any thread, when something that changes the status changed.</param>
    /// <param name="indexChanged">Called, on any thread, when the index or HEAD changed.</param>
    public RepositoryWatcher(string gitDirectory, Action changed, Action indexChanged)
    {
        Add(gitDirectory, recursive: false, name =>
        {
            if (TopFiles.Contains(name) || name.StartsWith("rebase-", StringComparison.Ordinal))
            {
                changed();
                if (name is "index" or "HEAD")
                    indexChanged();
            }
        });
        var refs = Path.Combine(gitDirectory, "refs");
        if (Directory.Exists(refs))
            Add(refs, recursive: true, name => changed());
    }

    private RepositoryWatcher()
    {
    }

    /// <summary>Watches a folder that is not a repository for a <c>.git</c> to appear in it, such as from <c>git init</c> in a terminal.</summary>
    public static RepositoryWatcher ForMissingRepository(string folder, Action created)
    {
        var watcher = new RepositoryWatcher();
        watcher.Add(folder, recursive: false, name =>
        {
            if (name == ".git")
                created();
        });
        return watcher;
    }

    public void Dispose()
    {
        foreach (var watcher in watchers)
            watcher.Dispose();
        watchers.Clear();
    }

    private void Add(string folder, bool recursive, Action<string> onChange)
    {
        try
        {
            var watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = recursive,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            void Handle(string? path)
            {
                if (path is null)
                    return;
                var name = recursive ? Path.GetRelativePath(folder, path) : Path.GetFileName(path);
                if (!name.EndsWith(".lock", StringComparison.Ordinal))
                    onChange(name);
            }

            watcher.Changed += (_, e) => Handle(e.FullPath);
            watcher.Created += (_, e) => Handle(e.FullPath);
            watcher.Deleted += (_, e) => Handle(e.FullPath);
            watcher.Renamed += (_, e) => Handle(e.FullPath);
            watcher.EnableRaisingEvents = true;
            watchers.Add(watcher);
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException)
        {
        }
    }
}
