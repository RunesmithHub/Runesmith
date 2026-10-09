using Avalonia.Media;

namespace Runesmith.Shell.Palette;

/// <summary>A row of the quick pick: a command, a file, a line to go to.</summary>
/// <param name="Value">What the row stands for, given back when it is picked.</param>
public sealed record QuickPickItem(string Title, object? Value)
{
    /// <summary>Gets a second line under the title, such as a file's folder.</summary>
    public string? Subtitle { get; init; }

    /// <summary>Gets text at the right of the row, such as a command's category.</summary>
    public string? Detail { get; init; }

    /// <summary>Gets the key binding shown at the right of the row.</summary>
    public string? Gesture { get; init; }

    public Geometry? Icon { get; init; }

    /// <summary>Gets the icon's own color, such as a file type's, or null for the row's text color.</summary>
    public IBrush? IconBrush { get; init; }

    public bool IsIconFilled { get; init; }

    /// <summary>Gets the indexes of the title's characters that matched the query, drawn highlighted.</summary>
    public IReadOnlyList<int> Matches { get; init; } = [];

    public bool IsEnabled { get; init; } = true;

    public bool HasSubtitle => !string.IsNullOrEmpty(Subtitle);

    public bool HasGesture => !string.IsNullOrEmpty(Gesture);
}

/// <summary>A source of quick pick rows for one prefix of the query, such as <c>&gt;</c> for commands.</summary>
public interface IQuickPickSource
{
    /// <summary>Gets the prefix that selects this source, or an empty text for the source of queries without a known prefix.</summary>
    string Prefix { get; }

    /// <summary>Gets the name shown in the hints at the bottom of the quick pick, such as "Commands".</summary>
    string Name { get; }

    /// <summary>Gets what the search box shows while it is empty.</summary>
    string Placeholder { get; }

    /// <summary>Finds the rows for the query, without the prefix.</summary>
    Task<IReadOnlyList<QuickPickItem>> SearchAsync(string query, CancellationToken cancellationToken);

    /// <summary>Does what picking a row means, such as running the command.</summary>
    Task AcceptAsync(QuickPickItem item);
}
