using Runesmith.Sdk.Documents;
using Runesmith.Text;

namespace Runesmith.Editor.Decorations;

/// <summary>A decoration and where it is in the current text.</summary>
public readonly record struct PlacedDecoration(Decoration Decoration, TextSpan Span);

/// <summary>The decorations of one editor, kept apart per source, such as per provider, each sorted by where it starts. Edits move them with
/// the text and drop those whose text was deleted.</summary>
public sealed class DecorationStore
{
    private readonly Dictionary<object, Set> sets = [];
    private int lensCount;
    private int inlayCount;

    /// <summary>Gets the number of decorations of every source.</summary>
    public int Count { get; private set; }

    /// <summary>Gets whether any source has a code lens.</summary>
    public bool HasCodeLenses => lensCount > 0;

    /// <summary>Gets whether any source has an inlay hint.</summary>
    public bool HasInlayHints => inlayCount > 0;

    /// <summary>Raised when decorations are replaced, removed or moved by an edit.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets a source's decorations, in order.</summary>
    public IReadOnlyList<PlacedDecoration> Of(object source) => sets.TryGetValue(source, out var set) ? set.Items : [];

    /// <summary>Places decorations computed for an older text in the current one, by moving them through the edits made since, each a list
    /// of changes sorted by start; ones that do not fit the older text, or whose text was deleted, are left out.</summary>
    public static IReadOnlyList<PlacedDecoration> Place(IReadOnlyList<Decoration> decorations, int textLength, IEnumerable<IReadOnlyList<TextChange>> since)
    {
        ArgumentNullException.ThrowIfNull(decorations);
        ArgumentNullException.ThrowIfNull(since);
        var placed = decorations.Where(d => d.Span.Start >= 0 && d.Span.End <= textLength).Select(d => new PlacedDecoration(d, d.Span)).ToList();
        foreach (var changes in since)
            placed = [.. placed.Where(p => !IsDeleted(changes, p.Span)).Select(p => p with { Span = OffsetMapping.Map(changes, p.Span) })];
        return placed;
    }

    /// <summary>Replaces a source's decorations with ones placed in the current text.</summary>
    /// <returns>Whether the source's decorations differ from before.</returns>
    public bool Replace(object source, IReadOnlyList<PlacedDecoration> placedDecorations)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(placedDecorations);
        var placed = placedDecorations.OrderBy(p => p.Span.Start).ToArray();
        var previous = sets.TryGetValue(source, out var set) ? set.Items : [];
        if (previous.AsSpan().SequenceEqual(placed))
            return false;

        if (placed.Length == 0)
            sets.Remove(source);
        else
            sets[source] = new Set(placed);
        Recount();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Replaces a source's decorations; their spans must be in the current text, and ones that do not fit it are left out.</summary>
    /// <returns>Whether the source's decorations differ from before.</returns>
    public bool Replace(object source, IReadOnlyList<Decoration> decorations, int textLength) => Replace(source, Place(decorations, textLength, []));

    /// <summary>Removes a source's decorations.</summary>
    public void Remove(object source)
    {
        if (sets.Remove(source))
        {
            Recount();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Clear()
    {
        if (sets.Count == 0)
            return;

        sets.Clear();
        Recount();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Moves every decoration through an edit's changes, sorted by start in the coordinates of the text before them.</summary>
    /// <remarks>Mapping keeps the order of starts, so the sets stay sorted without sorting them again.</remarks>
    public void Map(IReadOnlyList<TextChange> changes)
    {
        if (sets.Count == 0 || changes.Count == 0)
            return;

        var emptied = false;
        var dropped = false;
        foreach (var set in sets.Values)
        {
            var items = set.Items;
            var kept = 0;
            var maxLength = 0;
            for (var i = 0; i < items.Length; i++)
            {
                if (IsDeleted(changes, items[i].Span))
                    continue;

                var span = OffsetMapping.Map(changes, items[i].Span);
                items[kept++] = items[i] with { Span = span };
                maxLength = Math.Max(maxLength, span.Length);
            }

            if (kept < items.Length)
            {
                set.Items = items[..kept];
                dropped = true;
            }

            set.MaxLength = maxLength;
            emptied |= kept == 0;
        }

        if (emptied)
        {
            foreach (var source in sets.Where(pair => pair.Value.Items.Length == 0).Select(pair => pair.Key).ToList())
                sets.Remove(source);
        }

        if (dropped)
            Recount();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Gets the decorations of a type that touch a span, from every source.</summary>
    public IEnumerable<PlacedDecoration> In<T>(TextSpan span)
        where T : Decoration
    {
        foreach (var set in sets.Values)
        {
            var items = set.Items;
            for (var i = FirstStartingAtOrAfter(items, span.Start - set.MaxLength); i < items.Length && items[i].Span.Start <= span.End; i++)
            {
                if (items[i].Decoration is T && items[i].Span.End >= span.Start)
                    yield return items[i];
            }
        }
    }

    /// <summary>Gets every decoration of a type, from every source.</summary>
    public IEnumerable<PlacedDecoration> All<T>()
        where T : Decoration =>
        sets.Values.SelectMany(set => set.Items).Where(p => p.Decoration is T);

    // A decoration is dropped when all of its text was replaced, or, for an empty one, when the text on both sides of it was.
    private static bool IsDeleted(IReadOnlyList<TextChange> changes, TextSpan span)
    {
        foreach (var change in changes)
        {
            if (change.Span.Start > span.End)
                break;

            var deleted = span.IsEmpty
                ? change.Span.Start < span.Start && span.Start < change.Span.End
                : change.Span.Length > 0 && change.Span.Contains(span);
            if (deleted)
                return true;
        }

        return false;
    }

    private static int FirstStartingAtOrAfter(PlacedDecoration[] items, int position)
    {
        int low = 0, high = items.Length;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (items[middle].Span.Start < position)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private void Recount()
    {
        var count = 0;
        var lenses = 0;
        var inlays = 0;
        foreach (var set in sets.Values)
        {
            count += set.Items.Length;
            foreach (var placed in set.Items)
            {
                if (placed.Decoration is CodeLens)
                    lenses++;
                else if (placed.Decoration is InlayHint)
                    inlays++;
            }
        }

        Count = count;
        lensCount = lenses;
        inlayCount = inlays;
    }

    private sealed class Set(PlacedDecoration[] items)
    {
        public PlacedDecoration[] Items { get; set; } = items;

        /// <summary>Gets or sets the length of the longest span, so a search for spans that reach a position knows how far back to start.</summary>
        public int MaxLength { get; set; } = items.Length == 0 ? 0 : items.Max(p => p.Span.Length);
    }
}
