using System.IO.Enumeration;

namespace Runesmith.Workspace.Files;

/// <summary>The files of a folder that are not excluded, read once in the background and then kept up to date from the watcher.</summary>
internal sealed class FileIndex(string root, GlobMatcher exclude)
{
    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };

    private readonly Lock _gate = new();
    private readonly HashSet<string> _files = new(PathComparison.Comparer);
    private string[]? _sorted;
    private Task? _scan;

    /// <summary>Gets the files, sorted; the same array comes back until a file is added or removed.</summary>
    public async Task<IReadOnlyList<string>> GetFilesAsync(CancellationToken cancellationToken)
    {
        Task scan;
        lock (_gate)
            scan = _scan ??= Task.Run(Rescan, CancellationToken.None);
        await scan.WaitAsync(cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            if (_sorted is null)
            {
                _sorted = [.. _files];
                Array.Sort(_sorted, PathComparison.Comparer);
            }

            return _sorted;
        }
    }

    /// <summary>Starts reading the folder, if it has not started yet.</summary>
    public void Start()
    {
        lock (_gate)
            _scan ??= Task.Run(Rescan, CancellationToken.None);
    }

    /// <summary>Reads the whole folder again, such as after the watcher lost changes.</summary>
    public void Invalidate()
    {
        lock (_gate)
            _scan = Task.Run(Rescan, CancellationToken.None);
    }

    /// <summary>Whether a path inside the folder is left out by the exclude globs.</summary>
    public bool IsExcluded(string fullPath)
    {
        var relative = Path.GetRelativePath(root, fullPath);
        return relative != "." && exclude.IsMatchOrInside(GlobMatcher.ToGlobPath(relative));
    }

    public void Apply(IReadOnlyList<FileChange> changes)
    {
        lock (_gate)
        {
            if (_scan is null)
                return;

            foreach (var change in changes)
            {
                if (change.OldPath is { } oldPath)
                    Remove(oldPath);
                if (change.Kind == WatcherChangeTypes.Deleted)
                    Remove(change.Path);
                else
                    Add(change.Path);
            }
        }
    }

    private void Add(string path)
    {
        if (IsExcluded(path))
            return;

        if (File.Exists(path))
        {
            if (_files.Add(path))
                _sorted = null;
        }
        else if (Directory.Exists(path) && !IsLink(path))
        {
            foreach (var file in Enumerate(path))
                _files.Add(file);
            _sorted = null;
        }
    }

    private void Remove(string path)
    {
        if (_files.Remove(path))
        {
            _sorted = null;
            return;
        }

        var prefix = path + Path.DirectorySeparatorChar;
        if (_files.RemoveWhere(file => file.StartsWith(prefix, PathComparison.Comparison)) > 0)
            _sorted = null;
    }

    private void Rescan()
    {
        var files = Enumerate(root);
        lock (_gate)
        {
            _files.Clear();
            _files.UnionWith(files);
            _sorted = null;
        }
    }

    private List<string> Enumerate(string folder)
    {
        var files = new FileSystemEnumerable<string>(folder, (ref entry) => entry.ToFullPath(), Options)
        {
            ShouldIncludePredicate = (ref entry) => !entry.IsDirectory && !exclude.IsMatchOrInside(Relative(ref entry)),
            ShouldRecursePredicate = (ref entry) => (entry.Attributes & FileAttributes.ReparsePoint) == 0 && !exclude.IsMatchOrInside(Relative(ref entry)),
        };

        try
        {
            return [.. files];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private string Relative(ref FileSystemEntry entry)
    {
        var directory = entry.Directory;
        var name = entry.FileName;
        if (directory.Length <= root.Length)
            return GlobMatcher.ToGlobPath(name.ToString());

        var relativeFolder = directory[(root.Length + 1)..];
        return GlobMatcher.ToGlobPath(string.Concat(relativeFolder, Path.DirectorySeparatorChar.ToString(), name));
    }

    private static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}
