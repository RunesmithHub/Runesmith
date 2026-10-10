using Runesmith.Sdk.Workspace;

namespace Runesmith.Workspace.Files;

/// <summary>Merges the changes the watcher saw into at most one event per path, the way they add up: created and then changed is created,
/// created and then deleted is nothing, and renamed twice is one rename.</summary>
internal sealed class FileChangeCoalescer
{
    private readonly Dictionary<string, Entry> _entries = new(PathComparison.Comparer);
    private long _order;

    public bool IsEmpty => _entries.Count == 0;

    public void Add(FileChange change)
    {
        switch (change.Kind)
        {
            case WatcherChangeTypes.Created:
                if (_entries.TryGetValue(change.Path, out var existing))
                {
                    if (existing.Kind == FileChangeKind.Deleted)
                        Set(change.Path, FileChangeKind.Changed);
                }
                else
                {
                    Set(change.Path, FileChangeKind.Created);
                }

                break;
            case WatcherChangeTypes.Changed:
                if (_entries.TryGetValue(change.Path, out var changed))
                {
                    if (changed.Kind == FileChangeKind.Renamed)
                        changed.IsChangedAfterRename = true;
                    else if (changed.Kind == FileChangeKind.Deleted)
                        Set(change.Path, FileChangeKind.Changed);
                }
                else
                {
                    Set(change.Path, FileChangeKind.Changed);
                }

                break;
            case WatcherChangeTypes.Deleted:
                if (_entries.Remove(change.Path, out var deleted))
                {
                    if (deleted.Kind == FileChangeKind.Created)
                        break;
                    if (deleted.Kind == FileChangeKind.Renamed)
                    {
                        Remove(deleted.OldPath!);
                        break;
                    }
                }

                Set(change.Path, FileChangeKind.Deleted);
                break;
            case WatcherChangeTypes.Renamed when change.OldPath is { } oldPath:
                Rename(oldPath, change.Path);
                break;
        }
    }

    /// <summary>Gets the merged events, in the order their paths first changed, and starts over.</summary>
    public List<FileChangeEvent> Drain()
    {
        var entries = _entries.Values.OrderBy(e => e.Order).ToList();
        _entries.Clear();
        PairMoves(entries);

        var events = new List<FileChangeEvent>(entries.Count);
        foreach (var entry in entries)
        {
            events.Add(new FileChangeEvent(entry.Kind, entry.Path) { OldPath = entry.OldPath });
            if (entry.IsChangedAfterRename)
                events.Add(new FileChangeEvent(FileChangeKind.Changed, entry.Path));
        }

        return events;
    }

    private void Rename(string oldPath, string newPath)
    {
        // Whether a file was at the new path before this batch, so the rename replaced it.
        var replaced = false;
        if (_entries.Remove(newPath, out var target))
        {
            replaced = target.Kind is FileChangeKind.Deleted or FileChangeKind.Changed;
            if (target is { Kind: FileChangeKind.Renamed, OldPath: { } overwritten })
                Remove(overwritten);
        }

        if (!_entries.Remove(oldPath, out var source))
        {
            Put(new Entry(FileChangeKind.Renamed, newPath, oldPath, replaced));
            return;
        }

        switch (source.Kind)
        {
            case FileChangeKind.Created:
                Put(new Entry(replaced ? FileChangeKind.Changed : FileChangeKind.Created, newPath, null, false));
                break;
            case FileChangeKind.Renamed when PathComparison.Comparer.Equals(source.OldPath, newPath):
                if (source.IsChangedAfterRename || replaced)
                    Put(new Entry(FileChangeKind.Changed, newPath, null, false));
                break;
            case FileChangeKind.Renamed:
                Put(new Entry(FileChangeKind.Renamed, newPath, source.OldPath, source.IsChangedAfterRename || replaced));
                break;
            default:
                Put(new Entry(FileChangeKind.Renamed, newPath, oldPath, source.Kind == FileChangeKind.Changed || replaced));
                break;
        }
    }

    // A file that was at a path before this batch is gone: deleted, unless the batch created a file there since.
    private void Remove(string path)
    {
        if (_entries.TryGetValue(path, out var entry) && entry.Kind == FileChangeKind.Created)
            entry.Kind = FileChangeKind.Changed;
        else
            Set(path, FileChangeKind.Deleted);
    }

    // Some systems report a move between folders as a deletion and a creation; one of each with the same name is a rename.
    private static void PairMoves(List<Entry> entries)
    {
        var deleted = entries.Where(e => e.Kind == FileChangeKind.Deleted).ToLookup(e => Path.GetFileName(e.Path), PathComparison.Comparer);
        var created = entries.Where(e => e.Kind == FileChangeKind.Created).ToLookup(e => Path.GetFileName(e.Path), PathComparison.Comparer);
        foreach (var group in created)
        {
            if (group.Count() != 1 || deleted[group.Key].ToList() is not [var source])
                continue;

            var target = group.First();
            entries.Remove(source);
            target.Kind = FileChangeKind.Renamed;
            target.OldPath = source.Path;
        }
    }

    private void Set(string path, FileChangeKind kind)
    {
        if (_entries.TryGetValue(path, out var entry))
            entry.Kind = kind;
        else
            Put(new Entry(kind, path, null, false));
    }

    private void Put(Entry entry)
    {
        entry.Order = _order++;
        _entries[entry.Path] = entry;
    }

    private sealed class Entry(FileChangeKind kind, string path, string? oldPath, bool isChangedAfterRename)
    {
        public FileChangeKind Kind { get; set; } = kind;

        public string Path { get; } = path;

        public string? OldPath { get; set; } = oldPath;

        public bool IsChangedAfterRename { get; set; } = isChangedAfterRename;

        public long Order { get; set; }
    }
}
