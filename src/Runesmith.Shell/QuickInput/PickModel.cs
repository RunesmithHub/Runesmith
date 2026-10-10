using System.ComponentModel;
using Runesmith.Sdk.Shell;
using Runesmith.Workspace.Search;

namespace Runesmith.Shell.QuickInput;

/// <summary>A row of a plugin's quick pick, with the item it came from.</summary>
internal sealed class PickRow(object item, string label) : INotifyPropertyChanged
{
    private bool isChecked;

    /// <summary>Gets the plugin's <see cref="PickItem{T}"/>.</summary>
    public object Item { get; } = item;

    public string Label { get; } = label;

    public string? Description { get; init; }

    public string? Detail { get; init; }

    public string? Icon { get; init; }

    public string? IconPath { get; init; }

    public bool IsSeparator { get; init; }

    public bool IsSelected { get; init; }

    /// <summary>Gets the indexes of the label's characters that matched the typed text.</summary>
    public IReadOnlyList<int> Matches { get; set; } = [];

    public bool IsChecked
    {
        get => isChecked;
        set
        {
            if (isChecked == value)
                return;

            isChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static PickRow From<T>(PickItem<T> item) => new(item, item.Label)
    {
        Description = item.Description,
        Detail = item.Detail,
        Icon = item.Icon,
        IconPath = item.IconPath,
        IsSeparator = item.Kind == PickItemKind.Separator,
        IsSelected = item.IsSelected,
    };
}

/// <summary>The state of a quick pick: the rows that match what was typed, the active row and, in a pick of many, the checked rows. Its
/// rows come from a list it filters itself, or from a source it asks again as the user types, after a pause, cancelling the search before.</summary>
/// <remarks>Use it on the UI thread.</remarks>
internal sealed class PickModel : IDisposable
{
    private readonly IReadOnlyList<PickRow>? items;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<PickRow>>>? source;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly List<PickRow> picked = [];
    private readonly HashSet<object> touched = [];
    private CancellationTokenSource? search;
    private bool hasSearched;

    /// <summary>Creates a pick of a list it filters as the user types.</summary>
    public PickModel(IReadOnlyList<PickRow> items, QuickPickOptions options, bool canPickMany)
    {
        this.items = items;
        Options = options;
        CanPickMany = canPickMany;
        delay = Task.Delay;
        foreach (var row in items.Where(r => r.IsSelected && !r.IsSeparator && canPickMany))
            Check(row, true);
    }

    /// <summary>Creates a pick whose rows a source finds for the typed text.</summary>
    /// <param name="delay">Waits the pause before a search; the tests replace it.</param>
    public PickModel(Func<string, CancellationToken, Task<IReadOnlyList<PickRow>>> source, QuickPickOptions options, bool canPickMany,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        this.source = source;
        Options = options;
        CanPickMany = canPickMany;
        this.delay = delay ?? Task.Delay;
    }

    public QuickPickOptions Options { get; }

    public bool CanPickMany { get; }

    public string Query { get; private set; } = "";

    /// <summary>Gets the rows shown: the matching items and the separators of their groups.</summary>
    public IReadOnlyList<PickRow> Rows { get; private set; } = [];

    public int ActiveIndex { get; private set; } = -1;

    public PickRow? Active => ActiveIndex >= 0 && ActiveIndex < Rows.Count ? Rows[ActiveIndex] : null;

    /// <summary>Gets whether a source is searching.</summary>
    public bool IsBusy { get; private set; }

    /// <summary>Gets why the last search failed, or null.</summary>
    public string? Error { get; private set; }

    /// <summary>Gets the checked rows, in the order they were checked.</summary>
    public IReadOnlyList<PickRow> Picked => picked;

    /// <summary>Raised when the rows, the busy state or the error change.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when the active row changes.</summary>
    public event EventHandler? ActiveChanged;

    /// <summary>Raised when a row is checked or unchecked.</summary>
    public event EventHandler? PickedChanged;

    /// <summary>Raised with the exception when the source fails.</summary>
    public event EventHandler<Exception>? Failed;

    /// <summary>Shows the rows for a typed text: filters the list at once, or asks the source after the pause, at once the first time.</summary>
    public Task SetQueryAsync(string query)
    {
        Query = query;
        if (items is not null)
        {
            Rows = Filter(items, query, Options);
            Changed?.Invoke(this, EventArgs.Empty);
            SetActive(InitialActive());
            return Task.CompletedTask;
        }

        var immediate = !hasSearched;
        hasSearched = true;
        return SearchAsync(query, immediate);
    }

    /// <summary>Moves the active row, skipping separators; one row at a time wraps around at the ends.</summary>
    public void Move(int rows)
    {
        if (Rows.Count == 0 || !Rows.Any(r => !r.IsSeparator))
            return;

        var index = ActiveIndex < 0 ? (rows > 0 ? -1 : Rows.Count) : ActiveIndex;
        var step = Math.Sign(rows);
        if (Math.Abs(rows) == 1)
        {
            do
                index = ((index + step) % Rows.Count + Rows.Count) % Rows.Count;
            while (Rows[index].IsSeparator);
        }
        else
        {
            index = Math.Clamp(index + rows, 0, Rows.Count - 1);
            while (Rows[index].IsSeparator && index + step >= 0 && index + step < Rows.Count)
                index += step;
            while (Rows[index].IsSeparator)
                index -= step;
        }

        SetActive(index);
    }

    /// <summary>Makes a row the active one, such as one the user clicked.</summary>
    public void Activate(PickRow row)
    {
        var index = Rows.ToList().IndexOf(row);
        if (index >= 0 && !row.IsSeparator)
            SetActive(index);
    }

    /// <summary>Checks or unchecks a row, or the active row, in a pick of many.</summary>
    public void Toggle(PickRow? row = null)
    {
        row ??= Active;
        if (!CanPickMany || row is null || row.IsSeparator)
            return;

        touched.Add(row.Item);
        Check(row, !row.IsChecked);
        PickedChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Checks or unchecks every row shown.</summary>
    public void SetAllChecked(bool value)
    {
        if (!CanPickMany)
            return;

        foreach (var row in Rows.Where(r => !r.IsSeparator))
        {
            touched.Add(row.Item);
            Check(row, value);
        }

        PickedChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Gets what accepting gives: the active row in a pick of one; the checked rows in a pick of many, or the active row when none
    /// is checked. Null when there is nothing to accept.</summary>
    public IReadOnlyList<PickRow>? Accept()
    {
        if (CanPickMany && picked.Count > 0)
            return [.. picked];

        return Active is { IsSeparator: false } active ? [active] : null;
    }

    public void Dispose()
    {
        search?.Cancel();
        search?.Dispose();
        search = null;
    }

    /// <summary>Gets the rows of a list that match a typed text: by label, and by description and detail when the options say so. Rows keep
    /// their order where the list has separators, so groups stay together; otherwise the best matches come first.</summary>
    internal static IReadOnlyList<PickRow> Filter(IReadOnlyList<PickRow> rows, string query, QuickPickOptions options)
    {
        query = query.Trim();
        if (query.Length == 0)
        {
            foreach (var row in rows)
                row.Matches = [];
            return rows;
        }

        var matched = new List<(PickRow Row, double Score, int Order)>();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.IsSeparator)
                continue;

            var score = FuzzyMatcher.Score(row.Label, query, out var matches);
            if (score <= 0)
            {
                matches = [];
                if (options.MatchOnDescription && row.Description is { } description)
                    score = FuzzyMatcher.Score(description, query, out _);
                if (score <= 0 && options.MatchOnDetail && row.Detail is { } detail)
                    score = FuzzyMatcher.Score(detail, query, out _);
                score = score > 0 ? score / 2 : 0;
            }

            row.Matches = matches;
            if (score > 0)
                matched.Add((row, score, i));
        }

        if (!rows.Any(r => r.IsSeparator))
            return [.. matched.OrderByDescending(m => m.Score).ThenBy(m => m.Order).Select(m => m.Row)];

        var kept = matched.Select(m => m.Row).ToHashSet();
        var result = new List<PickRow>();
        PickRow? separator = null;
        foreach (var row in rows)
        {
            if (row.IsSeparator)
            {
                separator = row;
            }
            else if (kept.Contains(row))
            {
                if (separator is not null)
                    result.Add(separator);
                separator = null;
                result.Add(row);
            }
        }

        return result;
    }

    private async Task SearchAsync(string query, bool immediate)
    {
        search?.Cancel();
        search?.Dispose();
        search = new CancellationTokenSource();
        var token = search.Token;
        if (!IsBusy)
        {
            IsBusy = true;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        try
        {
            if (!immediate && Options.SearchDelay > TimeSpan.Zero)
                await delay(Options.SearchDelay, token);
            token.ThrowIfCancellationRequested();
            var rows = await source!(query, token);
            if (token.IsCancellationRequested)
                return;

            _ = Filter(rows, query, Options);
            foreach (var row in rows.Where(r => !r.IsSeparator))
            {
                if (picked.Any(p => Equals(p.Item, row.Item)))
                    row.IsChecked = true;
                else if (CanPickMany && row.IsSelected && !touched.Contains(row.Item))
                    Check(row, true);
            }

            Rows = rows;
            Error = null;
            IsBusy = false;
            Changed?.Invoke(this, EventArgs.Empty);
            SetActive(InitialActive());
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (!token.IsCancellationRequested)
        {
            Rows = [];
            Error = exception.Message;
            IsBusy = false;
            Changed?.Invoke(this, EventArgs.Empty);
            SetActive(-1);
            Failed?.Invoke(this, exception);
        }
    }

    private void Check(PickRow row, bool value)
    {
        row.IsChecked = value;
        picked.RemoveAll(p => ReferenceEquals(p, row) || Equals(p.Item, row.Item));
        if (value)
            picked.Add(row);
    }

    private int InitialActive()
    {
        var rows = Rows;
        if (Query.Trim().Length == 0 && !CanPickMany)
        {
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i] is { IsSelected: true, IsSeparator: false })
                    return i;
            }
        }

        for (var i = 0; i < rows.Count; i++)
        {
            if (!rows[i].IsSeparator)
                return i;
        }

        return -1;
    }

    private void SetActive(int index)
    {
        ActiveIndex = index;
        ActiveChanged?.Invoke(this, EventArgs.Empty);
    }
}
