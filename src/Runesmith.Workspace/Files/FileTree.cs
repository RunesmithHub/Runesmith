using System.ComponentModel;
using System.Composition;
using Runesmith.Sdk.Messaging;
using Runesmith.Sdk.Workspace;

namespace Runesmith.Workspace.Files;

/// <summary>The tree of the open folder that the Explorer shows, kept up to date as files change on disk.</summary>
/// <remarks>Use it on the UI thread.</remarks>
[Export(typeof(FileTree))]
[Shared]
public sealed class FileTree : INotifyPropertyChanged, IDisposable
{
    private static readonly EnumerationOptions Options = new() { IgnoreInaccessible = true, AttributesToSkip = 0 };

    private readonly IWorkspace _workspace;
    private readonly IDisposable _subscription;

    [ImportingConstructor]
    public FileTree(IWorkspace workspace, IMessageBus messageBus)
    {
        _workspace = workspace;
        _workspace.Changed += OnWorkspaceChanged;
        _subscription = messageBus.Subscribe<FilesChangedMessage>(OnFilesChanged);
        Rebuild();
    }

    /// <summary>Gets the node of the open folder, expanded, or null when no folder is open.</summary>
    public FileTreeNode? Root { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Expands the folders that lead to a file or folder and returns its node, or null when it is not in the tree.</summary>
    public FileTreeNode? Reveal(string path)
    {
        if (Root is null)
            return null;

        var full = PathComparison.Normalize(path);
        if (!PathComparison.IsInside(full, Root.FullPath) || !_workspace.Contains(full))
            return null;

        var node = Root;
        while (!PathComparison.Comparer.Equals(node.FullPath, full))
        {
            node.IsExpanded = true;
            var next = node.Children.FirstOrDefault(c => !c.IsPlaceholder && PathComparison.IsInside(full, c.FullPath));
            if (next is null)
                return null;
            node = next;
        }

        return node;
    }

    public void Dispose()
    {
        _workspace.Changed -= OnWorkspaceChanged;
        _subscription.Dispose();
    }

    internal List<(string Path, bool IsDirectory)> ReadEntries(string folder)
    {
        try
        {
            var entries = new DirectoryInfo(folder).EnumerateFileSystemInfos("*", Options)
                .Where(info => _workspace.Contains(info.FullName))
                .Select(info => (info.FullName, info is DirectoryInfo && info.LinkTarget is null))
                .ToList();
            entries.Sort((a, b) => a.Item2 != b.Item2 ? b.Item2.CompareTo(a.Item2) : NaturalComparer.Instance.Compare(Path.GetFileName(a.Item1), Path.GetFileName(b.Item1)));
            return entries;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void OnWorkspaceChanged(object? sender, EventArgs e) => Rebuild();

    private void Rebuild()
    {
        Root = _workspace.RootPath is { } root ? new FileTreeNode(this, null, root, isDirectory: true) { IsExpanded = true } : null;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Root)));
    }

    private void OnFilesChanged(FilesChangedMessage message)
    {
        if (Root is null)
            return;

        var folders = new HashSet<FileTreeNode>();
        foreach (var path in message.Paths)
        {
            var full = PathComparison.Normalize(path);
            if (Root.FindLoaded(full) is { IsDirectory: true, IsLoaded: true } folder && PathComparison.Comparer.Equals(full, Root.FullPath))
            {
                RefreshLoaded(folder);
                continue;
            }

            if (Path.GetDirectoryName(full) is { } parentPath && Root.FindLoaded(parentPath) is { IsLoaded: true } parent)
                folders.Add(parent);
        }

        foreach (var folder in folders)
            folder.Refresh();
    }

    private static void RefreshLoaded(FileTreeNode folder)
    {
        folder.Refresh();
        foreach (var child in folder.Children.Where(c => c.IsLoaded).ToList())
            RefreshLoaded(child);
    }
}
