namespace Runesmith.Text;

/// <summary>A run that differs between an old and a new version of a text, in the units compared: lines or characters.</summary>
/// <param name="OldStart">Where the run starts in the old version.</param>
/// <param name="OldLength">How many units of the old version it replaces; 0 when units were only added.</param>
/// <param name="NewStart">Where the run starts in the new version; for a deletion, the unit the deleted ones stood before.</param>
/// <param name="NewLength">How many units of the new version take their place; 0 when units were only deleted.</param>
public readonly record struct DiffHunk(int OldStart, int OldLength, int NewStart, int NewLength)
{
    /// <summary>Gets the end of the run in the old version.</summary>
    public int OldEnd => OldStart + OldLength;

    /// <summary>Gets the end of the run in the new version.</summary>
    public int NewEnd => NewStart + NewLength;

    /// <summary>Gets whether the run only adds units.</summary>
    public bool IsAddition => OldLength == 0;

    /// <summary>Gets whether the run only deletes units.</summary>
    public bool IsDeletion => NewLength == 0;
}
