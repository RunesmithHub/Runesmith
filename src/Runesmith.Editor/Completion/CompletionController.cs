using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Runesmith.Sdk.Languages;
using Runesmith.Text;

namespace Runesmith.Editor.Completion;

/// <summary>Asks for completions while the user types, filters them as the word grows and inserts the one the user accepts.</summary>
internal sealed class CompletionController : IDisposable
{
    private const int MaximumEntries = 400;

    private readonly TextArea area;
    private readonly ILanguageFeatures features;
    private readonly CompletionPopup popup;
    private readonly List<int> matchedScratch = [];
    private readonly Dictionary<CompletionItem, CompletionItem> resolved = [];
    private readonly DispatcherTimer resolveTimer;
    private CancellationTokenSource? request;
    private CancellationTokenSource? resolving;
    private CompletionList items = CompletionList.Empty;

    // Suggestions refer to the text as it was when they were asked for; these are the edits made since, to move their spans through.
    private List<IReadOnlyList<TextChange>> editsSinceItems = [];
    private List<IReadOnlyList<TextChange>> editsSinceRequest = [];
    private int wordStart;
    private long requestStart;
    private bool isListShown;
    private readonly DispatcherTimer refreshTimer;
    private CompletionList preparedFor = CompletionList.Empty;
    private List<Prepared> prepared = [];
    private List<Prepared> lastMatches = [];
    private string? lastTyped;
    private bool keepSelection;
    private bool isUpdatePending;
    private Avalonia.Rect placement;
    private bool isRequesting;

    public CompletionController(TextArea area, ILanguageFeatures features)
    {
        this.area = area;
        this.features = features;
        popup = new CompletionPopup(area);
        popup.Accepted += (_, _) => Accept();
        popup.SelectedChanged += (_, _) =>
        {
            resolveTimer!.Stop();
            resolveTimer.Start();
            popup.ShowDetails(Current);
        };
        resolveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        resolveTimer.Tick += (_, _) =>
        {
            resolveTimer.Stop();
            _ = ResolveSelectedAsync();
        };
        refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        refreshTimer.Tick += (_, _) =>
        {
            refreshTimer.Stop();
            if (IsOpen && items.IsIncomplete && !isRequesting)
            {
                keepSelection = true;
                Trigger(CompletionTrigger.Incomplete);
            }
        };
        area.Document.Buffer.Changed += (_, e) =>
        {
            editsSinceItems.Add(e.ChangeSet.Changes);
            editsSinceRequest.Add(e.ChangeSet.Changes);
        };
    }

    /// <summary>Gets the popup, which the editor places in its visual tree so it gets the theme's styles.</summary>
    public Avalonia.Controls.Primitives.Popup Popup => popup;

    public bool IsOpen => popup.IsOpen;

    private CompletionItem? Current => popup.Selected is { } entry ? resolved.GetValueOrDefault(entry.Item, entry.Item) : null;

    /// <summary>Asks for completions at the caret.</summary>
    public void Trigger(CompletionTrigger trigger, char? character = null)
    {
        var token = Cancellation.Renew(ref request);
        var snapshot = area.Snapshot;
        var caret = area.Selection.Caret;
        editsSinceRequest = [];
        wordStart = WordStart(snapshot, caret);
        lastTyped = null;
        if (!IsOpen)
        {
            requestStart = System.Diagnostics.Stopwatch.GetTimestamp();
            isListShown = false;
        }

        isRequesting = true;
        _ = RequestAsync(new CompletionRequest(area.Document, snapshot, caret, trigger, character), token);
    }

    /// <summary>Opens completion when the typed character asks for it, and otherwise refilters an open list.</summary>
    public void OnCharacterTyped(char character, bool completeOnType)
    {
        if (features.IsCompletionTrigger(area.Document, character))
        {
            Trigger(CompletionTrigger.Character, character);
            return;
        }

        if (!WordBoundaries.IsWordCharacter(character))
        {
            Close();
            return;
        }

        // The caret move of the typed character refiltered the list already; an incomplete list refreshes after a pause, or at once when
        // nothing matches any more.
        if (IsOpen || isRequesting)
        {
            if (items.IsIncomplete && !isRequesting)
            {
                if (IsOpen)
                {
                    refreshTimer.Stop();
                    refreshTimer.Start();
                }
                else
                {
                    Trigger(CompletionTrigger.Incomplete);
                }
            }
        }
        else if (completeOnType && area.Selection.Caret - WordStart(area.Snapshot, area.Selection.Caret) == 1)
        {
            Trigger(CompletionTrigger.Invoked);
        }
    }

    /// <summary>Keeps the list in step with the caret: refilters while it stays in the word and closes when it leaves.</summary>
    /// <remarks>The update runs after the typed character is drawn, at most once per key press, so typing never waits for the list.</remarks>
    public void OnSelectionChanged()
    {
        if ((!IsOpen && !isRequesting) || isUpdatePending)
            return;

        isUpdatePending = true;
        Dispatcher.UIThread.Post(Flush, DispatcherPriority.Background);
    }

    /// <summary>Brings the list up to date with the text and the caret now, such as before a key acts on it.</summary>
    public void Flush()
    {
        if (!isUpdatePending)
            return;

        isUpdatePending = false;
        if (!IsOpen && !isRequesting)
            return;

        var caret = area.Selection.Caret;
        if (!area.Selection.IsEmpty || caret < wordStart || caret > area.Snapshot.Length || WordStart(area.Snapshot, caret) > wordStart && caret != wordStart)
        {
            Close();
            return;
        }

        Refilter();
    }

    /// <summary>Handles the keys that work on the open list; returns whether it took the key.</summary>
    public bool HandleKey(KeyEventArgs e)
    {
        Flush();
        if (!IsOpen || e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift))
            return false;

        switch (e.Key)
        {
            case Key.Down:
                popup.MoveSelection(1);
                return true;
            case Key.Up:
                popup.MoveSelection(-1);
                return true;
            case Key.PageDown:
                popup.MoveSelection(10);
                return true;
            case Key.PageUp:
                popup.MoveSelection(-10);
                return true;
            case Key.Enter or Key.Tab when e.KeyModifiers == KeyModifiers.None:
                Accept();
                return true;
            case Key.Escape:
                Close();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Accepts the selected item first when the character about to be typed is one of its commit characters.</summary>
    public void BeforeTextInput(string text)
    {
        Flush();
        if (IsOpen && text.Length == 1 && Current is { } item && item.CommitCharacters.Contains(text[0]))
            Accept();
    }

    public void Close()
    {
        Cancellation.Cancel(ref request);
        Cancellation.Cancel(ref resolving);
        resolveTimer.Stop();
        refreshTimer.Stop();
        isRequesting = false;
        isUpdatePending = false;
        lastTyped = null;
        keepSelection = false;
        popup.IsOpen = false;
        items = CompletionList.Empty;
        resolved.Clear();
    }

    public void Dispose() => Close();

    private async Task RequestAsync(CompletionRequest completionRequest, CancellationToken token)
    {
        try
        {
            var list = await features.GetCompletionsAsync(completionRequest, token);
            if (token.IsCancellationRequested)
                return;

            // Language servers answer for the text they were asked about; when the user typed on meanwhile, ask again about the current text.
            if (list.Items.Count == 0 && !ReferenceEquals(completionRequest.Snapshot, area.Snapshot))
            {
                Trigger(CompletionTrigger.Invoked);
                return;
            }

            isRequesting = false;
            items = list;
            editsSinceItems = editsSinceRequest;
            EditorMetrics.CompletionItems.Record(list.Items.Count);
            Refilter();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Refilter()
    {
        var snapshot = area.Snapshot;
        var caret = area.Selection.Caret;
        if (caret < wordStart || isRequesting && items.Items.Count == 0)
            return;

        var filterStart = System.Diagnostics.Stopwatch.GetTimestamp();
        if (!ReferenceEquals(preparedFor, items))
        {
            preparedFor = items;
            prepared = [.. items.Items.Select(item => new Prepared(item, item.FilterText ?? item.Label))];
            lastTyped = null;
        }

        var typed = snapshot.GetText(TextSpan.FromBounds(wordStart, caret));

        // A suggestion that did not match a word cannot match the word with more letters, so a growing word filters the last matches.
        var source = lastTyped is not null && typed.StartsWith(lastTyped, StringComparison.Ordinal) ? lastMatches : prepared;
        var typedMask = CompletionFilter.MaskOf(typed);
        var matches = new List<Prepared>();
        var entries = new List<CompletionEntry>();
        foreach (var candidate in source)
        {
            if ((typedMask & ~candidate.Mask) != 0 || CompletionFilter.Score(candidate.Filter, typed, matchedScratch) is not { } score)
                continue;

            matches.Add(candidate);
            var item = candidate.Item;
            var matched = item.FilterText is null || item.FilterText == item.Label ? matchedScratch.ToArray() : [];
            entries.Add(new CompletionEntry(item, score, matched));
        }

        lastMatches = matches;
        lastTyped = typed;
        if (entries.Count == 0)
        {
            // Ask again only when the text changed since this list was asked for; the same question would get the same answer.
            popup.IsOpen = false;
            if (items.IsIncomplete && !isRequesting && editsSinceItems.Count > 0)
                Trigger(CompletionTrigger.Incomplete);
            return;
        }

        entries.Sort((a, b) =>
        {
            var byScore = b.Score.CompareTo(a.Score);
            return byScore != 0 ? byScore : string.CompareOrdinal(a.Item.SortText ?? a.Item.Label, b.Item.SortText ?? b.Item.Label);
        });
        if (entries.Count > MaximumEntries)
            entries.RemoveRange(MaximumEntries, entries.Count - MaximumEntries);
        EditorMetrics.CompletionFilter.Record(EditorMetrics.Since(filterStart));

        var updateStart = System.Diagnostics.Stopwatch.GetTimestamp();
        var selectedIndex = typed.Length == 0 ? Math.Max(0, entries.FindIndex(e => e.Item.IsPreselected)) : 0;
        if (keepSelection && popup.Selected is { } selected && entries.FindIndex(e => e.Item.Label == selected.Item.Label) is var kept and >= 0)
            selectedIndex = kept;
        keepSelection = false;
        popup.SetEntries(entries, selectedIndex);
        var anchor = area.GetCharacterRect(wordStart);
        if (anchor != placement || !popup.IsOpen)
        {
            placement = anchor;
            popup.PlacementRect = anchor;
        }

        popup.IsOpen = true;
        EditorMetrics.CompletionPopupUpdate.Record(EditorMetrics.Since(updateStart));
        if (!isListShown)
        {
            isListShown = true;
            var start = requestStart;
            TopLevel.GetTopLevel(area)?.RequestAnimationFrame(_ => EditorMetrics.CompletionShown.Record(EditorMetrics.Since(start)));
        }
    }

    private void Accept()
    {
        if (Current is not { } item)
        {
            Close();
            return;
        }

        var snapshot = area.Snapshot;
        var caret = area.Selection.Caret;
        var start = WordStart(snapshot, caret);
        var end = caret;
        if (item.ReplaceSpan is { } span)
        {
            var mappedStart = MapSinceItems(span.Start);
            var mappedEnd = Math.Min(snapshot.Length, Math.Max(MapSinceItems(span.End), caret));
            if (mappedStart <= caret)
                (start, end) = (mappedStart, mappedEnd);
        }

        var text = item.InsertText ?? item.Label;
        var main = new TextChange(TextSpan.FromBounds(start, end), text);
        var changes = new List<TextChange> { main };
        var shift = 0;
        foreach (var original in item.AdditionalChanges)
        {
            var extraStart = MapSinceItems(original.Span.Start);
            var extra = new TextChange(TextSpan.FromBounds(extraStart, Math.Max(extraStart, MapSinceItems(original.Span.End))), original.NewText);
            if (extra.Span.End > start && extra.Span.Start < end || extra.Span.End > snapshot.Length)
                continue;

            changes.Add(extra);
            if (extra.Span.End <= start)
                shift += extra.NewText.Length - extra.Span.Length;
        }

        changes.Sort((a, b) => a.Span.Start.CompareTo(b.Span.Start));
        Close();
        area.ApplyChanges(changes, EditorSelection.At(start + shift + text.Length));
    }

    private async Task ResolveSelectedAsync()
    {
        if (popup.Selected is not { } entry || resolved.ContainsKey(entry.Item))
            return;

        var token = Cancellation.Renew(ref resolving);
        try
        {
            var item = await features.ResolveCompletionAsync(entry.Item, token);
            if (token.IsCancellationRequested || !IsOpen)
                return;

            resolved[entry.Item] = item;
            if (popup.Selected?.Item == entry.Item)
                popup.ShowDetails(item);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private int MapSinceItems(int offset)
    {
        foreach (var changes in editsSinceItems)
            offset = OffsetMapping.Map(changes, offset);
        return offset;
    }

    private static int WordStart(TextSnapshot snapshot, int caret)
    {
        var start = caret;
        while (start > 0 && WordBoundaries.IsWordCharacter(snapshot[start - 1]))
            start--;
        return start;
    }

    /// <summary>A suggestion with what filtering needs, computed once per list.</summary>
    private readonly record struct Prepared(CompletionItem Item, string Filter)
    {
        public ulong Mask { get; } = CompletionFilter.MaskOf(Filter);
    }
}
