using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Runesmith.Sdk.Languages;
using Runesmith.Workspace.Search;

namespace Runesmith.Shell.Symbols;

/// <summary>How the Outline orders the symbols of each level.</summary>
public enum OutlineSort
{
    /// <summary>In the order they appear in the file.</summary>
    Position,

    /// <summary>Alphabetically.</summary>
    Name,
}

/// <summary>A row of the Outline: a symbol and the rows of the symbols inside it.</summary>
public sealed class OutlineNode : INotifyPropertyChanged
{
    private bool isExpanded;

    internal OutlineNode(DocumentSymbol symbol, string key, OutlineNode? parent)
    {
        Symbol = symbol;
        Key = key;
        Parent = parent;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public DocumentSymbol Symbol { get; }

    /// <summary>Gets the row's place in the tree by names, such as <c>Program/Main</c>, which keeps it open across updates.</summary>
    public string Key { get; }

    public OutlineNode? Parent { get; }

    public string Name => Symbol.Name;

    public ObservableCollection<OutlineNode> Children { get; } = [];

    public bool IsExpanded
    {
        get => isExpanded;
        set => Set(ref isExpanded, value);
    }

    public override string ToString() => Key;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>The Outline's tree: the active file's symbols, filtered and sorted, keeping which rows are open as the symbols change.</summary>
public sealed class OutlineModel
{
    /// <summary>Rows deeper than this start closed.</summary>
    private const int OpenDepth = 2;

    private readonly Dictionary<string, bool> expanded = new(StringComparer.Ordinal);
    private IReadOnlyList<DocumentSymbol> symbols = [];
    private string filter = "";
    private OutlineSort sort;

    /// <summary>Gets the top-level rows.</summary>
    public ObservableCollection<OutlineNode> Roots { get; } = [];

    /// <summary>Gets or sets the text rows must match; their parents stay to show where they are.</summary>
    public string Filter
    {
        get => filter;
        set
        {
            value = value.Trim();
            if (filter == value)
                return;

            filter = value;
            Rebuild();
        }
    }

    public OutlineSort Sort
    {
        get => sort;
        set
        {
            if (sort == value)
                return;

            sort = value;
            Rebuild();
        }
    }

    /// <summary>Gets the number of symbols, at every level, before filtering.</summary>
    public int SymbolCount { get; private set; }

    /// <summary>Raised after the rows were rebuilt.</summary>
    public event EventHandler? Changed;

    /// <summary>Shows a new symbol tree, such as after an edit or for another file.</summary>
    /// <param name="sameFile">Whether the symbols are of the file shown before, whose open rows stay open.</param>
    public void SetSymbols(IReadOnlyList<DocumentSymbol>? value, bool sameFile = true)
    {
        if (!sameFile)
            expanded.Clear();
        symbols = value ?? [];
        SymbolCount = Count(symbols);
        Rebuild();
    }

    /// <summary>Gets the deepest row whose symbol contains an offset, or null.</summary>
    public OutlineNode? NodeAt(int offset)
    {
        OutlineNode? found = null;
        var level = (IEnumerable<OutlineNode>)Roots;
        while (level.FirstOrDefault(node => node.Symbol.Range.Start <= offset && offset <= node.Symbol.Range.End) is { } inner)
        {
            found = inner;
            level = inner.Children;
        }

        return found;
    }

    /// <summary>Opens the rows above a row, so it can be shown.</summary>
    public static void Reveal(OutlineNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
            parent.IsExpanded = true;
    }

    /// <summary>Gets every row, depth first.</summary>
    public IEnumerable<OutlineNode> All() => Roots.SelectMany(Flatten);

    private static IEnumerable<OutlineNode> Flatten(OutlineNode node) => [node, .. node.Children.SelectMany(Flatten)];

    private void Rebuild()
    {
        foreach (var node in All())
            node.PropertyChanged -= Remember;

        Roots.Clear();
        foreach (var node in Build(symbols, null, "", 0))
            Roots.Add(node);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private List<OutlineNode> Build(IReadOnlyList<DocumentSymbol> level, OutlineNode? parent, string parentKey, int depth)
    {
        var nodes = new List<OutlineNode>();
        var ordered = sort == OutlineSort.Name
            ? level.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Range.Start)
            : level.OrderBy(s => s.Range.Start);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var symbol in ordered)
        {
            var name = parentKey.Length == 0 ? symbol.Name : $"{parentKey}/{symbol.Name}";
            var key = seen.TryGetValue(name, out var count) ? $"{name}#{count}" : name;
            seen[name] = count + 1;

            var node = new OutlineNode(symbol, key, parent);
            var children = Build(symbol.Children, node, key, depth + 1);
            var matches = filter.Length == 0 || FuzzyMatcher.Score(symbol.Name, filter) > 0;
            if (!matches && children.Count == 0)
                continue;

            foreach (var child in children)
                node.Children.Add(child);
            node.IsExpanded = filter.Length > 0 && children.Count > 0 || (expanded.TryGetValue(key, out var open) ? open : depth < OpenDepth);
            node.PropertyChanged += Remember;
            nodes.Add(node);
        }

        return nodes;
    }

    private void Remember(object? sender, PropertyChangedEventArgs e)
    {
        if (filter.Length == 0 && sender is OutlineNode node && e.PropertyName == nameof(OutlineNode.IsExpanded))
            expanded[node.Key] = node.IsExpanded;
    }

    private static int Count(IReadOnlyList<DocumentSymbol> level) => level.Count + level.Sum(s => Count(s.Children));
}
