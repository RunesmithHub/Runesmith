namespace Runesmith.Languages.Java.Syntax;

/// <summary>The Java release code is written for, such as 17, and whether it may use the preview features of that release.</summary>
public readonly record struct JavaVersion
{
    /// <summary>The oldest release Runesmith understands.</summary>
    public const int MinimumRelease = 8;

    /// <summary>The newest release Runesmith understands.</summary>
    public const int LatestRelease = 25;

    public JavaVersion(int release, bool isPreviewEnabled = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(release, MinimumRelease);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(release, LatestRelease);
        Release = release;
        IsPreviewEnabled = isPreviewEnabled;
    }

    public int Release { get; }

    public bool IsPreviewEnabled { get; }

    /// <summary>Gets the newest release, without preview features.</summary>
    public static JavaVersion Latest => new(LatestRelease);

    /// <summary>Gets a version for a release, bringing a release Runesmith does not know into its range: older ones become 8 and newer ones 25.</summary>
    public static JavaVersion FromRelease(int release, bool isPreviewEnabled = false) =>
        new(Math.Clamp(release, MinimumRelease, LatestRelease), isPreviewEnabled);

    public override string ToString() => IsPreviewEnabled ? $"Java {Release} with preview features" : $"Java {Release}";
}
