using Runesmith.Sdk.ToolWindows;

namespace Runesmith.Shell.ToolWindows;

/// <summary>Where the tool windows are: which area each belongs to and its place on the stripe, which one each area shows, and how large the
/// areas are. One window per area shows at a time.</summary>
internal sealed class ToolWindowLayout
{
    public const double DefaultSideSize = 280;
    public const double DefaultBottomSize = 240;
    public const double MinimumSize = 120;

    private readonly Dictionary<DockSide, List<string>> windows = new()
    {
        [DockSide.Left] = [],
        [DockSide.Right] = [],
        [DockSide.Bottom] = [],
    };

    private readonly Dictionary<DockSide, string?> shown = new() { [DockSide.Left] = null, [DockSide.Right] = null, [DockSide.Bottom] = null };
    private readonly Dictionary<DockSide, double> sizes = new()
    {
        [DockSide.Left] = DefaultSideSize,
        [DockSide.Right] = DefaultSideSize,
        [DockSide.Bottom] = DefaultBottomSize,
    };

    private readonly HashSet<string> known;
    private readonly HashSet<string> unavailable = new(StringComparer.Ordinal);

    /// <summary>Starts from the saved state, placing windows it does not know on their default side, or from the defaults when there is none.</summary>
    public ToolWindowLayout(IReadOnlyList<ToolWindowDefinition> definitions, ToolWindowSession? saved = null)
    {
        known = definitions.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        if (saved is not null)
        {
            Restore(DockSide.Left, saved.Left, saved.ShownLeft, saved.LeftSize);
            Restore(DockSide.Right, saved.Right, saved.ShownRight, saved.RightSize);
            Restore(DockSide.Bottom, saved.Bottom, saved.ShownBottom, saved.BottomSize);
        }

        var placed = windows.Values.SelectMany(w => w).ToHashSet(StringComparer.Ordinal);
        foreach (var definition in definitions.Where(d => !placed.Contains(d.Id)).OrderBy(d => d.Order))
        {
            windows[definition.Side].Add(definition.Id);
            if (saved is null && definition.IsVisibleByDefault)
                shown[definition.Side] ??= definition.Id;
        }
    }

    /// <summary>Raised when a window moves, shows or hides; not when an area is resized.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the available windows of an area, in stripe order.</summary>
    public IReadOnlyList<string> WindowsIn(DockSide area) => [.. windows[area].Where(id => known.Contains(id) && !unavailable.Contains(id))];

    /// <summary>Gets the area a window belongs to.</summary>
    public DockSide AreaOf(string id) => windows.First(w => w.Value.Contains(id)).Key;

    /// <summary>Gets the window an area shows, or null while it is closed or its window is unavailable.</summary>
    public string? ShownIn(DockSide area) => shown[area] is { } id && !unavailable.Contains(id) ? id : null;

    public bool IsShown(string id) => known.Contains(id) && !unavailable.Contains(id) && shown[AreaOf(id)] == id;

    /// <summary>Gets how wide a side area or how high the bottom area is, in pixels.</summary>
    public double SizeOf(DockSide area) => sizes[area];

    public void Resize(DockSide area, double size) => sizes[area] = Math.Max(MinimumSize, size);

    /// <summary>Shows a window in its area, in place of the one the area showed.</summary>
    public void Show(string id)
    {
        if (!known.Contains(id) || unavailable.Contains(id) || IsShown(id))
            return;

        shown[AreaOf(id)] = id;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Hide(string id)
    {
        if (!IsShown(id))
            return;

        shown[AreaOf(id)] = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Takes a window off its stripe while it does not apply, or puts it back. An area that showed it closes, and shows it again
    /// once it is available, unless the area shows another window by then.</summary>
    public void SetAvailable(string id, bool available)
    {
        if (!known.Contains(id) || (available ? !unavailable.Remove(id) : !unavailable.Add(id)))
            return;

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Gets whether a window is on its stripe, rather than taken off while it does not apply.</summary>
    public bool IsAvailable(string id) => !unavailable.Contains(id);

    /// <summary>Shows a window, or hides it when it is the one its area shows.</summary>
    public void Toggle(string id)
    {
        if (IsShown(id))
            Hide(id);
        else
            Show(id);
    }

    /// <summary>Moves a window to an area, at a place among the area's windows counted without it; it shows there when it showed before.</summary>
    public void Move(string id, DockSide area, int index)
    {
        if (!known.Contains(id))
            return;

        var from = AreaOf(id);
        var wasShown = shown[from] == id;
        windows[from].Remove(id);
        if (wasShown)
            shown[from] = null;

        var target = windows[area];
        // The index counts the windows the stripe shows; unknown and unavailable ones sit among them unseen.
        var visible = target.Where(w => known.Contains(w) && !unavailable.Contains(w)).ToList();
        var insertAt = index < visible.Count ? target.IndexOf(visible[Math.Max(0, index)]) : target.Count;
        target.Insert(insertAt, id);
        if (wasShown)
            shown[area] = id;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Gets the state to save.</summary>
    public ToolWindowSession Save() => new(
        [.. windows[DockSide.Left]], [.. windows[DockSide.Right]], [.. windows[DockSide.Bottom]],
        shown[DockSide.Left], shown[DockSide.Right], shown[DockSide.Bottom],
        sizes[DockSide.Left], sizes[DockSide.Right], sizes[DockSide.Bottom]);

    private void Restore(DockSide area, IReadOnlyList<string>? ids, string? shownId, double size)
    {
        var placed = windows.Values.SelectMany(w => w).ToHashSet(StringComparer.Ordinal);
        windows[area].AddRange((ids ?? []).Where(id => !placed.Contains(id)).Distinct(StringComparer.Ordinal));
        shown[area] = shownId is not null && known.Contains(shownId) && windows[area].Contains(shownId) ? shownId : null;
        if (size >= MinimumSize)
            sizes[area] = size;
    }
}

/// <summary>The saved state of the tool windows: each area's windows in stripe order, the one it shows, and its size. Windows of plugins that
/// are not loaded keep their place.</summary>
internal sealed record ToolWindowSession(
    IReadOnlyList<string>? Left,
    IReadOnlyList<string>? Right,
    IReadOnlyList<string>? Bottom,
    string? ShownLeft,
    string? ShownRight,
    string? ShownBottom,
    double LeftSize,
    double RightSize,
    double BottomSize);
