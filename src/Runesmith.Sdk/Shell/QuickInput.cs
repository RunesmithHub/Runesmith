using Runesmith.Text;

namespace Runesmith.Sdk.Shell;

/// <summary>What a row of a quick pick is.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
public enum PickItemKind
{
    /// <summary>A row the user can pick.</summary>
    Item,

    /// <summary>A line that starts a group, with the group's name as its label; it cannot be picked.</summary>
    Separator,
}

/// <summary>A row of a quick pick.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
/// <param name="Label">The text of the row, which the typed text is matched against.</param>
/// <param name="Value">What the row stands for, given back when it is picked.</param>
public sealed record PickItem<T>(string Label, T Value)
{
    /// <summary>Gets whether the row is an item or a separator that starts a group.</summary>
    public PickItemKind Kind { get; init; }

    /// <summary>Gets a short text shown after the label, dimmed, such as a folder.</summary>
    public string? Description { get; init; }

    /// <summary>Gets a line shown under the label, such as an explanation.</summary>
    public string? Detail { get; init; }

    /// <summary>Gets the name of the row's icon, such as <c>git-branch</c>; see HammerUI's <c>Icons.Find</c>.</summary>
    public string? Icon { get; init; }

    /// <summary>Gets a file or folder path whose icon, from the file icon theme, the row shows when it has no <see cref="Icon"/>.</summary>
    public string? IconPath { get; init; }

    /// <summary>Gets whether the row is selected when the pick opens: checked in a pick of many, or the active row in a pick of one.</summary>
    public bool IsSelected { get; init; }
}

/// <summary>Creates rows of a quick pick that are not items.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
public static class PickItem
{
    /// <summary>Creates a separator that starts a group, named by <paramref name="label"/> or unnamed.</summary>
    public static PickItem<T> Separator<T>(string? label = null) => new(label ?? "", default!) { Kind = PickItemKind.Separator };
}

/// <summary>Finds the rows of a quick pick for what the user typed; Runesmith asks again as they type, after a short pause, and cancels the
/// previous search.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
public delegate Task<IReadOnlyList<PickItem<T>>> PickItemSource<T>(string query, CancellationToken cancellationToken);

/// <summary>How a quick pick looks and behaves.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
public sealed record QuickPickOptions
{
    /// <summary>Gets a title shown above the search box.</summary>
    public string? Title { get; init; }

    /// <summary>Gets what the search box shows while it is empty.</summary>
    public string? Placeholder { get; init; }

    /// <summary>Gets the text the search box starts with.</summary>
    public string? Value { get; init; }

    /// <summary>Gets whether typed text also matches a row's <see cref="PickItem{T}.Description"/>.</summary>
    public bool MatchOnDescription { get; init; }

    /// <summary>Gets whether typed text also matches a row's <see cref="PickItem{T}.Detail"/>.</summary>
    public bool MatchOnDetail { get; init; }

    /// <summary>Gets whether the pick stays open when the window loses the focus or the user clicks outside it; by default it closes as if
    /// cancelled.</summary>
    public bool KeepOpenOnFocusLost { get; init; }

    /// <summary>Gets how long typing has to pause before a <see cref="PickItemSource{T}"/> is asked again; 150 ms by default.</summary>
    public TimeSpan SearchDelay { get; init; } = TimeSpan.FromMilliseconds(150);

    /// <summary>Gets the text shown when no row matches, such as "No branches".</summary>
    public string? EmptyText { get; init; }
}

/// <summary>How an input box looks and checks what is typed.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
public sealed record InputBoxOptions
{
    /// <summary>Gets a title shown above the box.</summary>
    public string? Title { get; init; }

    /// <summary>Gets a line under the box that says what to type.</summary>
    public string? Prompt { get; init; }

    /// <summary>Gets what the box shows while it is empty.</summary>
    public string? Placeholder { get; init; }

    /// <summary>Gets the text the box starts with.</summary>
    public string? Value { get; init; }

    /// <summary>Gets the part of <see cref="Value"/> selected at first; by default all of it.</summary>
    public TextSpan? ValueSelection { get; init; }

    /// <summary>Gets whether the box hides what is typed, as for a password or a token.</summary>
    public bool IsPassword { get; init; }

    /// <summary>Gets whether the box stays open when the window loses the focus or the user clicks outside it.</summary>
    public bool KeepOpenOnFocusLost { get; init; }

    /// <summary>Gets a check of the text that returns what is wrong with it, shown under the box, or null when it can be used. It runs
    /// after typing pauses and again before the text is accepted; Enter does nothing while it reports a problem.</summary>
    public Func<string, CancellationToken, Task<string?>>? Validate { get; init; }
}

/// <summary>Asks the user to pick from a list or to type a value, in the box at the top of the window that the command palette uses.</summary>
/// <remarks>Its members can be called from any thread. A new question replaces one that is showing, which then returns null. Added in
/// plugin API 0.1.2.</remarks>
public interface IQuickInput
{
    /// <summary>Lets the user pick one row; returns it, or null when they cancel.</summary>
    Task<PickItem<T>?> PickAsync<T>(IReadOnlyList<PickItem<T>> items, QuickPickOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Lets the user pick one row of a list that <paramref name="source"/> finds for what they type; returns it, or null when they
    /// cancel.</summary>
    Task<PickItem<T>?> PickAsync<T>(PickItemSource<T> source, QuickPickOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Lets the user check any number of rows; returns the checked rows, or null when they cancel.</summary>
    Task<IReadOnlyList<PickItem<T>>?> PickManyAsync<T>(IReadOnlyList<PickItem<T>> items, QuickPickOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Lets the user check any number of rows of a list that <paramref name="source"/> finds for what they type; returns the checked
    /// rows, or null when they cancel.</summary>
    Task<IReadOnlyList<PickItem<T>>?> PickManyAsync<T>(PickItemSource<T> source, QuickPickOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Asks the user to type a line of text; returns it, or null when they cancel.</summary>
    Task<string?> InputAsync(InputBoxOptions? options = null, CancellationToken cancellationToken = default);
}

/// <summary>One step of the questions <see cref="IQuickInputService.RunStepsAsync"/> asks: its questions show the flow's title, the step's
/// number and a Back button.</summary>
/// <remarks>When the user goes back, the question returns null and the step should return false; the flow then runs the step before it again.
/// Added in plugin API 0.1.2.</remarks>
public interface IQuickInputStep : IQuickInput
{
    /// <summary>Gets the step's number, from 1.</summary>
    int Number { get; }

    /// <summary>Gets the number of steps.</summary>
    int Count { get; }
}

/// <summary>Asks the user questions in the quick input box, such as a quick pick, an input box or several of them in steps. Import it.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
public interface IQuickInputService : IQuickInput
{
    /// <summary>Asks questions in steps, such as "Step 2 of 3", with Back to return to the step before. Each step asks its questions through
    /// the <see cref="IQuickInputStep"/> it gets and returns true to go on, or false when a question was cancelled or went back.</summary>
    /// <returns>True when every step went on, false when the user cancelled.</returns>
    Task<bool> RunStepsAsync(string title, IReadOnlyList<Func<IQuickInputStep, Task<bool>>> steps, CancellationToken cancellationToken = default);
}
