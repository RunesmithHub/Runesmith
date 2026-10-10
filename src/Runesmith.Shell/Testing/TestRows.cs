using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Runesmith.Sdk.Testing;

namespace Runesmith.Shell.Testing;

/// <summary>How the Test Explorer groups tests.</summary>
public enum TestGrouping
{
    /// <summary>As the providers arrange them: projects, namespaces, classes, tests.</summary>
    Structure,

    /// <summary>By the file each test is declared in.</summary>
    File,

    /// <summary>By the last result: failed, passed, skipped, not run.</summary>
    Status,

    /// <summary>By tag; a test with several tags shows under each.</summary>
    Tag,
}

/// <summary>A row of the Test Explorer's tree: a test or group of the test tree, or a group the explorer made, such as a file.</summary>
/// <remarks>Rows are kept by <see cref="Key"/> between refreshes, so the tree keeps what is expanded and selected.</remarks>
public sealed class TestRow : INotifyPropertyChanged
{
    private string label = "";
    private string? description;
    private TestState state;
    private string? duration;
    private bool isExpanded;
    private string? tooltip;

    internal TestRow(string key) => Key = key;

    public string Key { get; }

    /// <summary>Gets the test or group of the tree the row shows, or null for a group the explorer made.</summary>
    public TestNode? Node { get; internal set; }

    /// <summary>Gets the nodes running the row runs.</summary>
    public IReadOnlyList<TestNode> Nodes { get; internal set; } = [];

    public ObservableCollection<TestRow> Children { get; } = [];

    public string Label
    {
        get => label;
        internal set => Set(ref label, value);
    }

    public string? Description
    {
        get => description;
        internal set => Set(ref description, value);
    }

    public TestState State
    {
        get => state;
        internal set => Set(ref state, value);
    }

    /// <summary>Gets the row's duration as shown, such as <c>12 ms</c>, or null.</summary>
    public string? Duration
    {
        get => duration;
        internal set => Set(ref duration, value);
    }

    public string? ToolTip
    {
        get => tooltip;
        internal set => Set(ref tooltip, value);
    }

    /// <summary>Gets or sets whether the row's children show; the tree binds it.</summary>
    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (Set(ref isExpanded, value))
                ExpandedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Gets whether <see cref="IsExpanded"/> was set by the explorer, not the user.</summary>
    internal bool HasExpansion { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal event EventHandler? ExpandedChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

/// <summary>Builds the Test Explorer's rows from the test tree: grouped, filtered by name or tag, and optionally only failed tests.</summary>
public sealed class TestRows
{
    private readonly Dictionary<string, TestRow> rows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> expanded = new(StringComparer.Ordinal);

    /// <summary>Gets the top rows.</summary>
    public ObservableCollection<TestRow> Roots { get; } = [];

    /// <summary>Gets the number of tests the rows show.</summary>
    public int Shown { get; private set; }

    /// <summary>Finds a row by key.</summary>
    public TestRow? Find(string key) => rows.GetValueOrDefault(key);

    /// <summary>Gets the key of the row that shows a node in <see cref="TestGrouping.Structure"/>.</summary>
    public static string KeyOf(TestNode node) => $"{node.Provider.GetHashCode()}/{node.Id}";

    /// <summary>Updates the rows from the tree.</summary>
    /// <param name="filter">Words a test's path must contain, and <c>@tag</c> words its tags must contain; empty shows every test.</param>
    /// <param name="root">The open folder, for showing files relative to it.</param>
    public void Update(TestTree tree, TestGrouping grouping, string filter, bool failedOnly, string? root)
    {
        ArgumentNullException.ThrowIfNull(tree);
        var words = filter.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var tags = words.Where(w => w.StartsWith('@') && w.Length > 1).Select(w => w[1..]).ToList();
        var names = words.Where(w => !w.StartsWith('@')).ToList();
        var used = new HashSet<string>(StringComparer.Ordinal);
        List<TestRow> top;
        lock (tree.Gate)
        {
            bool Matches(TestNode test) =>
                (!failedOnly || test.State is TestState.Failed or TestState.Errored)
                && names.All(n => test.Path.Contains(n, StringComparison.OrdinalIgnoreCase))
                && tags.All(t => TagsOf(test).Any(tag => tag.Contains(t, StringComparison.OrdinalIgnoreCase)));

            var matching = tree.AllRoots.SelectMany(r => r.Tests()).Where(Matches).ToHashSet();
            Shown = matching.Count;
            top = grouping switch
            {
                TestGrouping.File => ByFile(matching, root, used),
                TestGrouping.Status => ByStatus(matching, used),
                TestGrouping.Tag => ByTag(matching, used),
                _ => ByStructure(tree, matching, used),
            };
        }

        foreach (var gone in rows.Keys.Where(k => !used.Contains(k)).ToList())
            rows.Remove(gone);
        Sync(Roots, top);
        var filtering = words.Length > 0 || failedOnly;
        foreach (var row in top)
            Expand(row, 0, filtering, grouping);
    }

    /// <summary>Collapses every row.</summary>
    public void CollapseAll()
    {
        foreach (var row in rows.Values)
        {
            row.IsExpanded = false;
            expanded[row.Key] = false;
        }
    }

    private static IEnumerable<string> TagsOf(TestNode node)
    {
        for (var at = node; at is not null; at = at.Parent)
        {
            foreach (var tag in at.Item.Tags)
                yield return tag;
        }
    }

    private List<TestRow> ByStructure(TestTree tree, HashSet<TestNode> matching, HashSet<string> used)
    {
        var providers = tree.Providers;
        if (providers.Count == 1)
            return [.. tree.RootsOf(providers[0]).Select(n => Structure(n, matching, used)).OfType<TestRow>()];

        var top = new List<TestRow>();
        foreach (var provider in providers)
        {
            var children = tree.RootsOf(provider).Select(n => Structure(n, matching, used)).OfType<TestRow>().ToList();
            if (children.Count == 0)
                continue;

            var nodes = tree.RootsOf(provider);
            var row = Row($"provider/{provider.GetHashCode()}", used);
            row.Node = null;
            row.Nodes = nodes;
            Fill(row, provider.Name, null, nodes.SelectMany(n => n.Tests()).ToList(), children);
            top.Add(row);
        }

        return top;
    }

    private TestRow? Structure(TestNode node, HashSet<TestNode> matching, HashSet<string> used)
    {
        if (node.IsTest && !matching.Contains(node))
            return null;

        var children = node.Children.Select(c => Structure(c, matching, used)).OfType<TestRow>().ToList();
        if (!node.IsTest && children.Count == 0)
            return null;

        var row = Row(KeyOf(node), used);
        row.Node = node;
        row.Nodes = [node];
        row.Label = node.Item.Label;
        row.Description = node.Item.Description;
        row.State = node.State;
        row.Duration = node.Duration is { } duration ? TestDecorations.Format(duration) : null;
        row.ToolTip = Tip(node);
        Sync(row.Children, children);
        return row;
    }

    private List<TestRow> ByFile(HashSet<TestNode> matching, string? root, HashSet<string> used)
    {
        var top = new List<TestRow>();
        foreach (var file in matching.GroupBy(t => FileOf(t) ?? "").OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var tests = file.ToList();
            var row = Row($"file/{file.Key}", used);
            row.Node = null;
            row.Nodes = tests;
            var name = file.Key.Length == 0 ? "No file" : Path.GetFileName(file.Key);
            var folder = file.Key.Length == 0 || root is null ? null : Path.GetDirectoryName(Path.GetRelativePath(root, file.Key));
            Fill(row, name, string.IsNullOrEmpty(folder) ? null : folder, tests, [.. tests.Select(t => Leaf(t, $"file/{file.Key}/", used))]);
            row.ToolTip = file.Key.Length == 0 ? null : file.Key;
            top.Add(row);
        }

        return top;
    }

    private List<TestRow> ByStatus(HashSet<TestNode> matching, HashSet<string> used)
    {
        (TestState[] States, string Label)[] groups =
        [
            ([TestState.Failed, TestState.Errored], "Failed"),
            ([TestState.Running, TestState.Queued], "Running"),
            ([TestState.Passed], "Passed"),
            ([TestState.Skipped], "Skipped"),
            ([TestState.None], "Not run"),
        ];
        var top = new List<TestRow>();
        foreach (var (states, label) in groups)
        {
            var tests = matching.Where(t => states.Contains(t.State)).OrderBy(t => t.Path, StringComparer.OrdinalIgnoreCase).ToList();
            if (tests.Count == 0)
                continue;

            var row = Row($"status/{label}", used);
            row.Node = null;
            row.Nodes = tests;
            Fill(row, label, null, tests, [.. tests.Select(t => Leaf(t, $"status/{label}/", used))]);
            top.Add(row);
        }

        return top;
    }

    private List<TestRow> ByTag(HashSet<TestNode> matching, HashSet<string> used)
    {
        var top = new List<TestRow>();
        var byTag = matching.SelectMany(t => TagsOf(t).Distinct(StringComparer.OrdinalIgnoreCase).DefaultIfEmpty("").Select(tag => (Tag: tag, Test: t)))
            .GroupBy(p => p.Tag, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key.Length == 0)
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var group in byTag)
        {
            var tests = group.Select(p => p.Test).OrderBy(t => t.Path, StringComparer.OrdinalIgnoreCase).ToList();
            var row = Row($"tag/{group.Key}", used);
            row.Node = null;
            row.Nodes = tests;
            Fill(row, group.Key.Length == 0 ? "No tag" : group.Key, null, tests, [.. tests.Select(t => Leaf(t, $"tag/{group.Key}/", used))]);
            top.Add(row);
        }

        return top;
    }

    private TestRow Leaf(TestNode test, string prefix, HashSet<string> used)
    {
        var row = Row(prefix + KeyOf(test), used);
        row.Node = test;
        row.Nodes = [test];
        row.Label = test.Item.Label;
        row.Description = test.Parent is { } parent ? parent.Item.Label + (test.Item.Description is { } d ? $" {d}" : "") : test.Item.Description;
        row.State = test.State;
        row.Duration = test.Duration is { } duration ? TestDecorations.Format(duration) : null;
        row.ToolTip = Tip(test);
        row.Children.Clear();
        return row;
    }

    private static void Fill(TestRow row, string label, string? description, List<TestNode> tests, List<TestRow> children)
    {
        row.Label = label;
        var failed = tests.Count(t => t.State is TestState.Failed or TestState.Errored);
        row.Description = description is null ? Count(tests.Count, failed) : $"{description}  {Count(tests.Count, failed)}";
        row.State = TestNode.Aggregate([.. tests.Select(t => t.State)]);
        var durations = tests.Select(t => t.Result?.Duration).OfType<TimeSpan>().ToList();
        row.Duration = durations.Count == 0 ? null : TestDecorations.Format(durations.Aggregate(TimeSpan.Zero, (a, b) => a + b));
        Sync(row.Children, children);
    }

    private static string Count(int tests, int failed) =>
        (tests == 1 ? "1 test" : $"{tests} tests") + (failed > 0 ? $", {failed} failed" : "");

    private static string Tip(TestNode node)
    {
        var lines = new List<string> { node.Path };
        if (TestDecorations.Status(node, node.State) is { } status)
            lines.Add(status);
        if (node.Item.Tags.Count > 0)
            lines.Add("Tags: " + string.Join(", ", node.Item.Tags));
        return string.Join("\n", lines);
    }

    private static string? FileOf(TestNode node)
    {
        for (var at = node; at is not null; at = at.Parent)
        {
            if (at.Item.FilePath is { } path)
                return path;
        }

        return null;
    }

    private TestRow Row(string key, HashSet<string> used)
    {
        used.Add(key);
        if (!rows.TryGetValue(key, out var row))
        {
            rows[key] = row = new TestRow(key);
            row.ExpandedChanged += (_, _) =>
            {
                if (!row.HasExpansion)
                    expanded[row.Key] = row.IsExpanded;
            };
        }

        return row;
    }

    // Opens groups down to the classes, groups with failures, and everything that matches a filter, unless the user chose otherwise.
    private void Expand(TestRow row, int depth, bool filtering, TestGrouping grouping)
    {
        if (row.Children.Count == 0)
            return;

        var wanted = expanded.TryGetValue(row.Key, out var chosen) && !filtering
            ? chosen
            : filtering || row.State is TestState.Failed or TestState.Errored || grouping != TestGrouping.Structure || depth < 2
              || row.Node?.Item.Kind is TestItemKind.Project or TestItemKind.Namespace;
        row.HasExpansion = true;
        row.IsExpanded = wanted;
        row.HasExpansion = false;
        foreach (var child in row.Children)
            Expand(child, depth + 1, filtering, grouping);
    }

    private static void Sync(ObservableCollection<TestRow> target, List<TestRow> wanted)
    {
        if (target.SequenceEqual(wanted))
            return;

        var keep = wanted.ToHashSet();
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(target[i]))
                target.RemoveAt(i);
        }

        for (var i = 0; i < wanted.Count; i++)
        {
            var at = target.IndexOf(wanted[i]);
            if (at == i)
                continue;
            if (at >= 0)
                target.Move(at, i);
            else
                target.Insert(i, wanted[i]);
        }
    }
}
