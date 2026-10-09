using System.Composition;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Workspace;
using Runesmith.Workspace.Files;

namespace Runesmith.Workspace.Search;

/// <summary>A file that matches a Quick Open query.</summary>
/// <param name="RelativePath">The path from the open folder, as shown in the list.</param>
/// <param name="NameStart">Where the file name starts in <paramref name="RelativePath"/>.</param>
/// <param name="Matches">The indices of the matched characters in <paramref name="RelativePath"/>, for highlighting.</param>
public sealed record QuickOpenMatch(string FullPath, string RelativePath, int NameStart, IReadOnlyList<int> Matches, double Score);

/// <summary>Finds the files of the open folder whose names match what the user types in Quick Open.</summary>
[Export(typeof(QuickOpenSearch))]
[Shared]
public sealed class QuickOpenSearch : IDisposable
{
    private const int RecentCapacity = 50;

    private readonly IWorkspace _workspace;
    private readonly IDocumentService _documents;
    private readonly LinkedList<string> _recent = new();
    private readonly Lock _gate = new();
    private IReadOnlyList<string>? _indexedFiles;
    private Entry[] _entries = [];

    [ImportingConstructor]
    public QuickOpenSearch(IWorkspace workspace, IDocumentService documents)
    {
        _workspace = workspace;
        _documents = documents;
        _documents.Opened += OnDocumentOpened;
    }

    /// <summary>Gets the best matches, best first. An empty query lists the recently opened files first, then the others by path.</summary>
    /// <remarks>A query with a <c>/</c> or <c>\</c> is matched against the whole path; otherwise the file name counts most.</remarks>
    public async Task<IReadOnlyList<QuickOpenMatch>> SearchAsync(string query, int limit = 50, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (_workspace.RootPath is not { } root)
            return [];

        var files = await _workspace.GetFilesAsync(cancellationToken).ConfigureAwait(false);
        var entries = Index(root, files);
        query = query.Trim();
        return query.Length == 0 ? Recent(entries, limit) : await Task.Run(() => Match(entries, query, limit, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() => _documents.Opened -= OnDocumentOpened;

    private Entry[] Index(string root, IReadOnlyList<string> files)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(files, _indexedFiles))
            {
                _entries = [.. files.Select(path =>
                {
                    var relative = Path.GetRelativePath(root, path);
                    return new Entry(path, relative, relative.Length - Path.GetFileName(relative).Length);
                })];
                _indexedFiles = files;
            }

            return _entries;
        }
    }

    private List<QuickOpenMatch> Recent(Entry[] entries, int limit)
    {
        string[] recent;
        lock (_gate)
            recent = [.. _recent];

        var byPath = entries.ToDictionary(e => e.FullPath, PathComparison.Comparer);
        var results = new List<QuickOpenMatch>(limit);
        var seen = new HashSet<string>(PathComparison.Comparer);
        foreach (var path in recent)
        {
            if (results.Count == limit)
                return results;
            if (byPath.TryGetValue(path, out var entry) && seen.Add(path))
                results.Add(entry.ToMatch([], 0));
        }

        foreach (var entry in entries)
        {
            if (results.Count == limit)
                break;
            if (seen.Add(entry.FullPath))
                results.Add(entry.ToMatch([], 0));
        }

        return results;
    }

    private static List<QuickOpenMatch> Match(Entry[] entries, string query, int limit, CancellationToken cancellationToken)
    {
        var wholePath = query.Contains('/') || query.Contains('\\');
        if (wholePath && Path.DirectorySeparatorChar == '\\')
            query = query.Replace('/', '\\');
        else if (wholePath)
            query = query.Replace('\\', '/');

        var best = new PriorityQueue<Entry, double>(limit + 1);
        var scores = new Dictionary<Entry, double>(limit + 1);
        for (var i = 0; i < entries.Length; i++)
        {
            if ((i & 4095) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            var entry = entries[i];
            var score = wholePath ? FuzzyMatcher.Score(entry.RelativePath, query) : ScoreByName(entry, query);
            if (score <= 0 || (best.Count == limit && best.TryPeek(out _, out var lowest) && score <= lowest))
                continue;

            best.Enqueue(entry, score);
            scores[entry] = score;
            if (best.Count > limit)
                scores.Remove(best.Dequeue());
        }

        return [.. scores.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key.RelativePath.Length)
            .Select(pair => pair.Key.ToMatch(Highlight(pair.Key, query, wholePath), pair.Value))];
    }

    private static double ScoreByName(Entry entry, string query)
    {
        var name = entry.RelativePath.AsSpan(entry.NameStart);
        var byName = FuzzyMatcher.Score(name, query);
        return byName > 0 ? byName + 20 : FuzzyMatcher.Score(entry.RelativePath, query) * 0.5;
    }

    private static int[] Highlight(Entry entry, string query, bool wholePath)
    {
        if (!wholePath && FuzzyMatcher.Score(entry.RelativePath[entry.NameStart..], query, out var inName) > 0)
            return [.. inName.Select(i => i + entry.NameStart)];

        FuzzyMatcher.Score(entry.RelativePath, query, out var inPath);
        return inPath;
    }

    private void OnDocumentOpened(object? sender, DocumentEventArgs e)
    {
        if (e.Document.FilePath is not { } path)
            return;

        lock (_gate)
        {
            if (_recent.Find(path) is { } existing)
                _recent.Remove(existing);
            _recent.AddFirst(path);
            if (_recent.Count > RecentCapacity)
                _recent.RemoveLast();
        }
    }

    private sealed record Entry(string FullPath, string RelativePath, int NameStart)
    {
        public QuickOpenMatch ToMatch(int[] matches, double score) => new(FullPath, RelativePath, NameStart, matches, score);
    }
}
