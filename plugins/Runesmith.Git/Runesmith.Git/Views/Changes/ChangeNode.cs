using System.ComponentModel;
using Runesmith.Git.Git;

namespace Runesmith.Git.Views.Changes;

/// <summary>The groups of the Changes tree.</summary>
internal enum ChangeGroup
{
    /// <summary>Files with merge conflicts.</summary>
    Conflicts,

    /// <summary>Changes that the next commit takes.</summary>
    Staged,

    /// <summary>Changes to tracked files that are not staged.</summary>
    Unstaged,

    /// <summary>Files Git does not track yet.</summary>
    Untracked,
}

/// <summary>A row of the Changes tree: a group, a folder, or a changed file.</summary>
internal sealed class ChangeNode : INotifyPropertyChanged
{
    private bool isExpanded = true;

    private ChangeNode(ChangeGroup group, string name, string key, StatusEntry? entry)
    {
        Group = group;
        Name = name;
        Key = key;
        Entry = entry;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the group the row is in.</summary>
    public ChangeGroup Group { get; }

    /// <summary>Gets what the row shows: the group's title, the folder's path or the file's name.</summary>
    public string Name { get; }

    /// <summary>Gets a key that stays the same across refreshes, to keep rows expanded and selected.</summary>
    public string Key { get; }

    /// <summary>Gets the file's status, or null for a group or folder.</summary>
    public StatusEntry? Entry { get; }

    /// <summary>Gets the rows under a group or folder.</summary>
    public List<ChangeNode> Children { get; } = [];

    /// <summary>Gets the number of files under the row.</summary>
    public int FileCount => Entry is not null ? 1 : Children.Sum(c => c.FileCount);

    /// <summary>Gets whether the row is a group.</summary>
    public bool IsGroup => Key.StartsWith("group:", StringComparison.Ordinal);

    /// <summary>Gets or sets whether the row shows its children.</summary>
    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (isExpanded == value)
                return;
            isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    /// <summary>Gets whether the next commit takes the row's changes, which its check box shows.</summary>
    public bool IsStaged => Group == ChangeGroup.Staged;

    /// <summary>Gets the letter shown for the file's change: <c>M</c>, <c>A</c>, <c>D</c>, <c>R</c>, <c>C</c>, <c>T</c>, <c>U</c> for untracked and
    /// <c>!</c> for a conflict.</summary>
    public char Letter => Entry switch
    {
        null => ' ',
        { Kind: StatusKind.Unmerged } => '!',
        { Kind: StatusKind.Untracked } => 'U',
        _ when Group == ChangeGroup.Staged => Entry.Index,
        _ => Entry.WorkTree,
    };

    /// <summary>Gets the files under the row, or the row's own file.</summary>
    public IEnumerable<ChangeNode> Files() => Entry is not null ? [this] : Children.SelectMany(c => c.Files());

    /// <summary>Builds the tree of a status: conflicts, staged, unstaged and untracked groups that have files, each with folders that hold
    /// only one folder joined into one row.</summary>
    /// <param name="collapsed">Keys of the rows the user collapsed.</param>
    /// <param name="collapseFoldersAbove">Folders start collapsed in groups with more files than this, so a large change set stays quick.</param>
    public static IReadOnlyList<ChangeNode> Build(StatusSnapshot status, IReadOnlySet<string> collapsed, int collapseFoldersAbove = 300)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(collapsed);
        var groups = new List<ChangeNode>();
        Add(ChangeGroup.Conflicts, "Conflicts", status.Conflicts);
        Add(ChangeGroup.Staged, "Staged", status.Staged);
        Add(ChangeGroup.Unstaged, "Changes", status.Unstaged);
        Add(ChangeGroup.Untracked, "Untracked Files", status.Untracked);
        return groups;

        void Add(ChangeGroup group, string title, IEnumerable<StatusEntry> entries)
        {
            var files = entries.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToList();
            if (files.Count == 0)
                return;

            var node = new ChangeNode(group, title, $"group:{group}", null);
            var folderDefault = files.Count <= collapseFoldersAbove;
            foreach (var file in files)
                Insert(node, group, file, folderDefault);
            Compress(node);
            Apply(node, collapsed, folderDefault);
            node.IsExpanded = !collapsed.Contains(node.Key);
            groups.Add(node);
        }
    }

    private static void Insert(ChangeNode root, ChangeGroup group, StatusEntry entry, bool expanded)
    {
        var parts = entry.Path.Split('/');
        var parent = root;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var key = $"{group}:{string.Join('/', parts.Take(i + 1))}/";
            var folder = parent.Children.FirstOrDefault(c => c.Key == key);
            if (folder is null)
            {
                folder = new ChangeNode(group, parts[i], key, null) { isExpanded = expanded };
                parent.Children.Add(folder);
            }

            parent = folder;
        }

        parent.Children.Add(new ChangeNode(group, parts[^1], $"{group}:{entry.Path}", entry));
    }

    // Joins a folder whose only child is a folder with that child, such as src and Runesmith into src/Runesmith.
    private static void Compress(ChangeNode node)
    {
        for (var i = 0; i < node.Children.Count; i++)
        {
            var child = node.Children[i];
            while (child.Entry is null && child.Children is [{ Entry: null } only])
            {
                var joined = new ChangeNode(child.Group, child.Name + "/" + only.Name, only.Key, null) { isExpanded = only.isExpanded };
                joined.Children.AddRange(only.Children);
                child = joined;
            }

            node.Children[i] = child;
            Compress(child);
        }

        node.Children.Sort((a, b) => (a.Entry is null) != (b.Entry is null) ? (a.Entry is null ? -1 : 1) : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
    }

    private static void Apply(ChangeNode node, IReadOnlySet<string> collapsed, bool folderDefault)
    {
        foreach (var child in node.Children.Where(c => c.Entry is null))
        {
            child.isExpanded = folderDefault ? !collapsed.Contains(child.Key) : collapsed.Contains("open:" + child.Key);
            Apply(child, collapsed, folderDefault);
        }
    }
}
