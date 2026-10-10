using System.Text;
using Runesmith.Sdk.Testing;
using Runesmith.Shell.Debugging;

namespace Runesmith.Shell.Testing;

/// <summary>Where a test is: not run, waiting in a run, running, or its last result.</summary>
public enum TestState
{
    None,
    Queued,
    Running,
    Passed,
    Skipped,
    Failed,
    Errored,
}

/// <summary>A test's last result: its state, how long it took, why it failed or was skipped, and what it wrote.</summary>
public sealed record TestResult(TestState State)
{
    public TimeSpan? Duration { get; init; }

    public TestFailure? Failure { get; init; }

    /// <summary>Gets why the test was skipped, or null.</summary>
    public string? SkipReason { get; init; }

    /// <summary>Gets what the test wrote while it ran.</summary>
    public string Output { get; init; } = "";
}

/// <summary>An item of the test tree: a provider's <see cref="TestItem"/>, its result and the items under it.</summary>
/// <remarks>Read and change it only while holding the tree's lock.</remarks>
public sealed class TestNode
{
    private readonly List<TestNode> children = [];

    internal TestNode(ITestProvider provider, TestItem item, TestNode? parent)
    {
        Provider = provider;
        Item = item;
        Parent = parent;
    }

    public ITestProvider Provider { get; }

    /// <summary>Gets the item as the provider last described it; its children are this node's <see cref="Children"/>.</summary>
    public TestItem Item { get; internal set; }

    public TestNode? Parent { get; internal set; }

    public IReadOnlyList<TestNode> Children => children;

    public string Id => Item.Id;

    /// <summary>Gets the node's own result, or null before it has one.</summary>
    public TestResult? Result { get; internal set; }

    /// <summary>Gets whether the node is a test that runs on its own: a test or a case, or any item without children.</summary>
    public bool IsTest => children.Count == 0;

    /// <summary>Gets the node's state: its own for a test, otherwise the most telling state of the tests under it.</summary>
    public TestState State
    {
        get
        {
            if (children.Count == 0)
                return Result?.State ?? TestState.None;

            var states = Tests().Select(t => t.Result?.State ?? TestState.None).ToList();
            if (Result is { } own && own.State != TestState.None)
                states.Add(own.State);
            return Aggregate(states);
        }
    }

    /// <summary>Gets the duration of the node's own result, or the sum of its tests' durations.</summary>
    public TimeSpan? Duration =>
        children.Count == 0 ? Result?.Duration : Tests().Select(t => t.Result?.Duration).Where(d => d is not null).Aggregate((TimeSpan?)null, (sum, d) => (sum ?? TimeSpan.Zero) + d);

    /// <summary>Gets the node and every node under it, depth first.</summary>
    public IEnumerable<TestNode> Descendants()
    {
        yield return this;
        foreach (var child in children)
        {
            foreach (var node in child.Descendants())
                yield return node;
        }
    }

    /// <summary>Gets the tests under the node, or the node itself when it is one.</summary>
    public IEnumerable<TestNode> Tests() => Descendants().Where(n => n.IsTest);

    /// <summary>Gets the item with its current children, for a run request.</summary>
    public TestItem ToItem() => Item with { Children = [.. children.Select(c => c.ToItem())] };

    /// <summary>Gets the labels from the top of the tree down to the node, such as for a tooltip.</summary>
    public string Path
    {
        get
        {
            var parts = new List<string>();
            for (var node = this; node is not null; node = node.Parent)
                parts.Insert(0, node.Item.Label);
            return string.Join(" › ", parts);
        }
    }

    internal void Add(TestNode child) => children.Add(child);

    internal void Insert(int index, TestNode child) => children.Insert(Math.Clamp(index, 0, children.Count), child);

    internal bool Remove(TestNode child) => children.Remove(child);

    internal int IndexOf(TestNode child) => children.IndexOf(child);

    /// <summary>Gets the state a group shows for the states of its tests: errors and failures first, then running and waiting tests, then
    /// passes, then skipped tests.</summary>
    public static TestState Aggregate(IReadOnlyCollection<TestState> states) => states.Count == 0 ? TestState.None : states.MaxBy(Rank);

    private static int Rank(TestState state) => state switch
    {
        TestState.Errored => 6,
        TestState.Failed => 5,
        TestState.Running => 4,
        TestState.Queued => 3,
        TestState.Passed => 2,
        TestState.Skipped => 1,
        _ => 0,
    };
}

/// <summary>The tests every provider found, as one tree per provider, with their results. Discoveries of the whole folder or of one file
/// are merged into it by id, so results stay with tests that are still there.</summary>
/// <remarks>Its members can be called from any thread; they take <see cref="Gate"/>, which callers reading nodes hold too.</remarks>
public sealed class TestTree
{
    private readonly Dictionary<ITestProvider, List<TestNode>> roots = [];
    private readonly Dictionary<(ITestProvider, string), TestNode> index = [];

    /// <summary>Gets the lock that guards the tree and its nodes.</summary>
    public Lock Gate { get; } = new();

    /// <summary>Raised, on the changing thread, after the tree's items change; its argument lists the files whose tests changed, or is null
    /// when any may have.</summary>
    public event EventHandler<IReadOnlyCollection<string>?>? Changed;

    /// <summary>Raised, on the changing thread, after results change; its argument lists the files of the tests whose results changed.</summary>
    public event EventHandler<IReadOnlyCollection<string>>? ResultsChanged;

    /// <summary>Gets the providers that have tests, in the order they were added.</summary>
    public IReadOnlyList<ITestProvider> Providers
    {
        get
        {
            lock (Gate)
                return [.. roots.Where(r => r.Value.Count > 0).Select(r => r.Key)];
        }
    }

    /// <summary>Gets a provider's top nodes; hold <see cref="Gate"/> while reading them.</summary>
    public IReadOnlyList<TestNode> RootsOf(ITestProvider provider)
    {
        lock (Gate)
            return roots.TryGetValue(provider, out var list) ? [.. list] : [];
    }

    /// <summary>Gets every provider's top nodes; hold <see cref="Gate"/> while reading them.</summary>
    public IReadOnlyList<TestNode> AllRoots
    {
        get
        {
            lock (Gate)
                return [.. roots.Values.SelectMany(r => r)];
        }
    }

    /// <summary>Gets every node; hold <see cref="Gate"/> while reading them.</summary>
    public IReadOnlyList<TestNode> AllNodes
    {
        get
        {
            lock (Gate)
                return [.. roots.Values.SelectMany(r => r).SelectMany(r => r.Descendants())];
        }
    }

    /// <summary>Finds a provider's node by its item's id.</summary>
    public TestNode? Find(ITestProvider provider, string id)
    {
        lock (Gate)
            return index.GetValueOrDefault((provider, id));
    }

    /// <summary>Gets the nodes declared in a file.</summary>
    public IReadOnlyList<TestNode> InFile(string filePath)
    {
        lock (Gate)
            return [.. index.Values.Where(n => PathKey.Equals(n.Item.FilePath, filePath))];
    }

    /// <summary>Replaces what a provider found in the whole folder: new items are added, items that are gone are removed, and items that are
    /// still there keep their results.</summary>
    public void ReplaceAll(ITestProvider provider, IReadOnlyList<TestItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        lock (Gate)
        {
            if (!roots.TryGetValue(provider, out var list))
                roots[provider] = list = [];

            var keep = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items)
                Collect(item, keep);
            foreach (var gone in list.SelectMany(r => r.Descendants()).Where(n => !keep.Contains(n.Id)).ToList())
                Detach(provider, gone);

            for (var i = 0; i < items.Count; i++)
                Merge(provider, null, items[i], i);
        }

        Changed?.Invoke(this, null);
    }

    /// <summary>Replaces what a provider found in one file: the file's items that are gone are removed, with groups left empty, and the items
    /// found are merged into the tree under the same ids.</summary>
    /// <param name="items">Top items, holding only the file's tests and the groups above them.</param>
    public void ReplaceFile(ITestProvider provider, string filePath, IReadOnlyList<TestItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        lock (Gate)
        {
            if (!roots.TryGetValue(provider, out var list))
                roots[provider] = list = [];

            var keep = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items)
                Collect(item, keep);
            foreach (var gone in list.SelectMany(r => r.Descendants()).Where(n => PathKey.Equals(n.Item.FilePath, filePath) && !keep.Contains(n.Id)).ToList())
                Detach(provider, gone);

            for (var i = 0; i < items.Count; i++)
                Merge(provider, null, items[i], int.MaxValue);
            Prune(provider, filePath, keep);
        }

        Changed?.Invoke(this, [filePath]);
    }

    /// <summary>Removes a provider's tests.</summary>
    public void Clear(ITestProvider provider)
    {
        lock (Gate)
        {
            if (!roots.Remove(provider))
                return;

            foreach (var key in index.Keys.Where(k => k.Item1 == provider).ToList())
                index.Remove(key);
        }

        Changed?.Invoke(this, null);
    }

    /// <summary>Removes every provider's tests.</summary>
    public void ClearAll()
    {
        lock (Gate)
        {
            roots.Clear();
            index.Clear();
        }

        Changed?.Invoke(this, null);
    }

    /// <summary>Adds an item under a node, such as a case found while running; an item with an id that exists replaces that one.</summary>
    /// <returns>The node, or null when the parent is not in the tree.</returns>
    public TestNode? AddChild(ITestProvider provider, string parentId, TestItem item)
    {
        TestNode? node;
        lock (Gate)
        {
            if (!index.TryGetValue((provider, parentId), out var parent))
                return null;

            node = Merge(provider, parent, item, int.MaxValue);
        }

        Changed?.Invoke(this, item.FilePath is { } path ? [path] : null);
        return node;
    }

    /// <summary>Sets nodes' results and raises <see cref="ResultsChanged"/> once for them.</summary>
    public void SetResults(IReadOnlyList<(TestNode Node, TestResult? Result)> results)
    {
        var files = new HashSet<string>(PathKey.Comparer);
        lock (Gate)
        {
            foreach (var (node, result) in results)
            {
                node.Result = result;
                for (var at = node; at is not null; at = at.Parent)
                {
                    if (at.Item.FilePath is { } path)
                        files.Add(path);
                }

                if (result?.Failure is { } failure && TestFailures.Location(failure, null) is { } location)
                    files.Add(location.FilePath);
            }
        }

        if (results.Count > 0)
            ResultsChanged?.Invoke(this, files);
    }

    /// <summary>Counts the tests by state.</summary>
    public IReadOnlyDictionary<TestState, int> Count()
    {
        lock (Gate)
            return roots.Values.SelectMany(r => r).SelectMany(r => r.Tests()).GroupBy(t => t.State).ToDictionary(g => g.Key, g => g.Count());
    }

    private static void Collect(TestItem item, HashSet<string> ids)
    {
        ids.Add(item.Id);
        foreach (var child in item.Children)
            Collect(child, ids);
    }

    private TestNode Merge(ITestProvider provider, TestNode? parent, TestItem item, int position)
    {
        if (index.TryGetValue((provider, item.Id), out var node))
        {
            if (node.Parent != parent)
            {
                Unlink(provider, node);
                Link(provider, parent, node, position);
            }

            node.Item = item with { Children = [] };
        }
        else
        {
            node = new TestNode(provider, item with { Children = [] }, parent);
            index[(provider, item.Id)] = node;
            Link(provider, parent, node, position);
        }

        for (var i = 0; i < item.Children.Count; i++)
            Merge(provider, node, item.Children[i], position == int.MaxValue ? int.MaxValue : i);
        if (position != int.MaxValue)
            Reorder(node, item);
        return node;
    }

    // A full discovery gives the order; a file's discovery keeps the order the tree has.
    private static void Reorder(TestNode node, TestItem item)
    {
        for (var i = 0; i < item.Children.Count; i++)
        {
            var child = node.Children.FirstOrDefault(c => c.Id == item.Children[i].Id);
            if (child is not null && node.IndexOf(child) != i)
            {
                node.Remove(child);
                node.Insert(i, child);
            }
        }
    }

    private void Link(ITestProvider provider, TestNode? parent, TestNode node, int position)
    {
        node.Parent = parent;
        if (parent is null)
        {
            var list = roots[provider];
            list.Insert(Math.Clamp(position, 0, list.Count), node);
        }
        else
            parent.Insert(position, node);
    }

    private void Unlink(ITestProvider provider, TestNode node)
    {
        if (node.Parent is { } parent)
            parent.Remove(node);
        else
            roots[provider].Remove(node);
        node.Parent = null;
    }

    private void Detach(ITestProvider provider, TestNode node)
    {
        if (!index.ContainsKey((provider, node.Id)))
            return;

        Unlink(provider, node);
        foreach (var gone in node.Descendants())
            index.Remove((provider, gone.Id));
    }

    // Removes groups a file's discovery left without tests, unless they belong to another file.
    private void Prune(ITestProvider provider, string filePath, HashSet<string> keep)
    {
        bool removed;
        do
        {
            removed = false;
            foreach (var node in roots[provider].SelectMany(r => r.Descendants()).ToList())
            {
                if (node.Children.Count > 0 || node.Item.Kind is TestItemKind.Test or TestItemKind.Case || keep.Contains(node.Id))
                    continue;
                if (node.Item.FilePath is { } path && !PathKey.Equals(path, filePath))
                    continue;

                Detach(provider, node);
                removed = true;
            }
        }
        while (removed);
    }
}

/// <summary>Where a failure happened and how it reads in one line.</summary>
internal static class TestFailures
{
    /// <summary>Gets the file and position a failure happened at: its own, or its first stack frame with a file inside <paramref name="root"/>,
    /// or any file when <paramref name="root"/> is null.</summary>
    public static (string FilePath, Runesmith.Text.TextPosition Position)? Location(TestFailure failure, string? root)
    {
        if (failure.FilePath is { } path && failure.Position is { } position)
            return (path, position);

        foreach (var frame in failure.StackTrace)
        {
            if (frame.FilePath is { } framePath && frame.Position is { } framePosition && (root is null || IsInside(framePath, root)))
                return (framePath, framePosition);
        }

        return null;
    }

    /// <summary>Gets the failure's message in one line, with the expected and actual values when it has them.</summary>
    public static string Summary(TestFailure failure)
    {
        var text = new StringBuilder(FirstLine(failure.Message));
        if (failure.Expected is not null || failure.Actual is not null)
            text.Append(" (expected ").Append(FirstLine(failure.Expected ?? "")).Append(", actual ").Append(FirstLine(failure.Actual ?? "")).Append(')');
        return text.ToString();
    }

    private static string FirstLine(string text)
    {
        var line = text.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
        return line.Length > 160 ? line[..157] + "..." : line;
    }

    private static bool IsInside(string path, string root) =>
        path.StartsWith(root.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, PathKey.Comparer == StringComparer.Ordinal ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
}
