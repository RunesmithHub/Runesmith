using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Runesmith.Workspace.Files;

/// <summary>A file or folder in the Explorer's tree.</summary>
/// <remarks>A folder's children are read when it is first expanded. Until then it holds one placeholder child, so the tree shows that it can be
/// expanded.</remarks>
public sealed class FileTreeNode : INotifyPropertyChanged
{
    private readonly FileTree? _tree;
    private bool _isExpanded;

    internal FileTreeNode(FileTree? tree, FileTreeNode? parent, string fullPath, bool isDirectory, bool isPlaceholder = false)
    {
        _tree = tree;
        Parent = parent;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        IsPlaceholder = isPlaceholder;
        Name = Path.GetFileName(fullPath) is { Length: > 0 } name ? name : fullPath;
        if (isDirectory)
            Children.Add(new FileTreeNode(null, this, fullPath, isDirectory: false, isPlaceholder: true));
    }

    public string Name { get; }

    public string FullPath { get; }

    public bool IsDirectory { get; }

    /// <summary>Gets whether this is the stand-in child of a folder that has not been read yet.</summary>
    public bool IsPlaceholder { get; }

    public FileTreeNode? Parent { get; }

    /// <summary>Gets the children: folders first, then files, each sorted by name.</summary>
    public ObservableCollection<FileTreeNode> Children { get; } = [];

    /// <summary>Gets whether the folder's children have been read.</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>Gets or sets whether the folder is expanded; expanding it the first time reads its children.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (value && IsDirectory && !IsLoaded)
                Refresh();
            Set(ref _isExpanded, value && IsDirectory);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Reads the folder's entries again and updates the children in place, so expanded folders below stay expanded.</summary>
    public void Refresh()
    {
        if (!IsDirectory || _tree is null)
            return;

        var entries = _tree.ReadEntries(FullPath);
        if (!IsLoaded)
        {
            IsLoaded = true;
            Children.Clear();
        }

        var wanted = new HashSet<string>(entries.Select(e => e.Path), PathComparison.Comparer);
        for (var i = Children.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(Children[i].FullPath))
                Children.RemoveAt(i);
        }

        var existing = Children.ToDictionary(c => c.FullPath, PathComparison.Comparer);
        for (var i = 0; i < entries.Count; i++)
        {
            var (path, isDirectory) = entries[i];
            if (existing.TryGetValue(path, out var child) && child.IsDirectory == isDirectory)
            {
                var at = Children.IndexOf(child);
                if (at != i)
                    Children.Move(at, i);
            }
            else
            {
                if (child is not null)
                    Children.Remove(child);
                Children.Insert(i, new FileTreeNode(_tree, this, path, isDirectory));
            }
        }
    }

    /// <summary>Finds the loaded node of a path among this node's descendants, or returns null.</summary>
    public FileTreeNode? FindLoaded(string fullPath)
    {
        if (PathComparison.Comparer.Equals(FullPath, fullPath))
            return this;
        if (!IsLoaded || !PathComparison.IsInside(fullPath, FullPath))
            return null;

        foreach (var child in Children)
        {
            if (child.FindLoaded(fullPath) is { } found)
                return found;
        }

        return null;
    }

    public override string ToString() => FullPath;

    private void Set(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
