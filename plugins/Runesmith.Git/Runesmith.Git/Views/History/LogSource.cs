using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Runesmith.Git.Git;

namespace Runesmith.Git.Views.History;

/// <summary>A commit in the log with its row of the graph.</summary>
/// <param name="Commit">The commit.</param>
/// <param name="Graph">What the graph draws in its row, or null when the log is filtered so that it has no graph.</param>
internal sealed record LogEntry(GitCommit Commit, GraphRow? Graph);

/// <summary>The commits of the log, read a page at a time as the list scrolls, with the graph laid out page by page off the UI thread.</summary>
internal sealed class LogSource
{
    /// <summary>The number of commits read at a time.</summary>
    public const int PageSize = 200;

    private readonly Action<Action> post;
    private GraphLayout layout = new();
    private GitRepository? repository;
    private LogQuery query = new();
    private int generation;
    private bool isLoading;

    /// <summary>Creates a source that changes <see cref="Entries"/> through <paramref name="post"/>, which runs an action on the UI thread.</summary>
    public LogSource(Action<Action> post) => this.post = post;

    /// <summary>Gets the commits read so far; changed on the UI thread, a page at a time.</summary>
    public PagedList Entries { get; } = new();

    /// <summary>Gets whether every commit of the query has been read.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>Gets the widest the graph has been, in lanes.</summary>
    public int GraphWidth { get; private set; }

    /// <summary>Gets the query whose commits show.</summary>
    public LogQuery Query => query;

    /// <summary>Gets the last failure, such as an unknown revision in the branch filter, or null.</summary>
    public string? Error { get; private set; }

    /// <summary>Raised on the UI thread after a page was added, the log was reset or reading it failed.</summary>
    public event EventHandler? Changed;

    /// <summary>Starts over with another repository or query, reading the first page.</summary>
    public Task ResetAsync(GitRepository? repository, LogQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        this.repository = repository;
        this.query = query with { Skip = 0, Count = PageSize };
        generation++;
        isLoading = false;
        IsComplete = repository is null;
        GraphWidth = 0;
        Error = null;
        layout = new GraphLayout();
        Entries.Reset();
        Changed?.Invoke(this, EventArgs.Empty);
        return LoadMoreAsync();
    }

    /// <summary>Reads the next page, unless one is being read or every commit has been.</summary>
    public async Task LoadMoreAsync()
    {
        if (isLoading || IsComplete || repository is not { } current)
            return;

        isLoading = true;
        var started = generation;
        var page = query with { Skip = Entries.Count };
        var graph = layout;
        try
        {
            var entries = await Task.Run(async () =>
            {
                var commits = await current.GetLogAsync(page, CancellationToken.None).ConfigureAwait(false);
                return commits.Select(c => new LogEntry(c, page.HasGraph ? graph.Add(c) : null)).ToList();
            }).ConfigureAwait(false);
            post(() =>
            {
                if (started != generation)
                    return;
                isLoading = false;
                IsComplete = entries.Count < PageSize;
                GraphWidth = Math.Max(GraphWidth, entries.Count == 0 ? 0 : entries.Max(e => e.Graph?.Width ?? 0));
                Entries.AddRange(entries);
                Changed?.Invoke(this, EventArgs.Empty);
            });
        }
        catch (GitException exception)
        {
            post(() =>
            {
                if (started != generation)
                    return;
                isLoading = false;
                IsComplete = true;
                Error = exception.Message;
                Changed?.Invoke(this, EventArgs.Empty);
            });
        }
    }

    /// <summary>Finds the position of a commit read so far, or -1.</summary>
    public int IndexOf(string sha) => Entries.FindIndex(e => e.Commit.Sha == sha);

    /// <summary>A list that adds a page of entries in one change, so a page costs one layout of the list.</summary>
    internal sealed class PagedList : Collection<LogEntry>, INotifyCollectionChanged
    {
        public event NotifyCollectionChangedEventHandler? CollectionChanged;

        public void AddRange(IReadOnlyList<LogEntry> entries)
        {
            if (entries.Count == 0)
                return;
            var start = Count;
            ((List<LogEntry>)Items).AddRange(entries);
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, (IList)entries.ToList(), start));
        }

        public void Reset()
        {
            Items.Clear();
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        public int FindIndex(Predicate<LogEntry> match) => ((List<LogEntry>)Items).FindIndex(match);
    }
}
