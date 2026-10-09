using Runesmith.Text;

namespace Runesmith.Editor.Editing;

/// <summary>The changes an editing operation makes and where the selection goes afterwards.</summary>
/// <param name="Changes">The changes, in the coordinates of the text before them.</param>
/// <param name="Selection">The selection in the text after the changes.</param>
public sealed record EditResult(IReadOnlyList<TextChange> Changes, EditorSelection Selection)
{
    /// <summary>Only moves the selection.</summary>
    public static EditResult Move(EditorSelection selection) => new([], selection);
}
