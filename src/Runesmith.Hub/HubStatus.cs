
namespace Runesmith.Hub;

/// <summary>Whether the hub's data can be trusted right now, which decides whether installs and updates are offered.</summary>
public enum HubStatusKind
{
    /// <summary>This build of Runesmith has no trust root; only local plugins load.</summary>
    NotSetUp,

    /// <summary>The user turned the hub off.</summary>
    TurnedOff,

    /// <summary>The hub has not been checked in this session yet.</summary>
    Checking,

    /// <summary>The index was verified and is current.</summary>
    Verified,

    /// <summary>The index could not be reached; the saved view is shown.</summary>
    Offline,

    /// <summary>The index or the saved view has expired, so the safety status of plugins can't be confirmed.</summary>
    Unverifiable,

    /// <summary>The index failed a signature, hash or version check; nothing new was saved.</summary>
    VerificationFailed,

    /// <summary>The index needs a newer Runesmith.</summary>
    RunesmithTooOld,
}

/// <summary>The hub's status for the plugin manager and the plugin page.</summary>
/// <param name="CheckedAt">When the index was last verified, in this session or an earlier one.</param>
/// <param name="Since">For <see cref="HubStatusKind.Unverifiable"/>, since when the status can't be confirmed.</param>
/// <param name="Detail">What went wrong, for the log.</param>
public sealed record HubStatus(HubStatusKind Kind, DateTimeOffset? CheckedAt = null, DateTimeOffset? Since = null, string? Detail = null)
{
    /// <summary>Gets whether installs and updates may run: only with an index verified in this session.</summary>
    public bool CanInstall => Kind == HubStatusKind.Verified;

    /// <summary>Gets whether the catalog can be browsed, from a fresh or a saved view.</summary>
    public bool HasCatalog => Kind is not (HubStatusKind.NotSetUp or HubStatusKind.TurnedOff);

    /// <summary>Gets a sentence for the user about what the status means.</summary>
    public string Describe() => Kind switch
    {
        HubStatusKind.NotSetUp => Strings.HubStatusNotSetUp,
        HubStatusKind.TurnedOff => Strings.HubStatusTurnedOff,
        HubStatusKind.Checking => Strings.HubStatusChecking,
        HubStatusKind.Verified => Strings.HubStatusVerified,
        HubStatusKind.Offline => Strings.HubStatusOffline,
        HubStatusKind.Unverifiable => Since is { } since ? Strings.HubStatusUnverifiable(since) : Strings.HubStatusUnverifiableNever,
        HubStatusKind.VerificationFailed => Strings.HubStatusVerificationFailed,
        HubStatusKind.RunesmithTooOld => Strings.HubStatusRunesmithTooOld,
        _ => "",
    };
}
