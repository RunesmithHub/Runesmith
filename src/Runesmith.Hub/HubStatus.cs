using System.Globalization;

namespace Runesmith.Hub;

/// <summary>Whether the hub's data can be trusted right now, which decides whether installs and updates are offered.</summary>
public enum HubStatusKind
{
    /// <summary>This build of Runesmith has no trust root; only bundled and local plugins load.</summary>
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
        HubStatusKind.NotSetUp => "The plugin hub is not set up in this build of Runesmith. Bundled and local plugins work as usual.",
        HubStatusKind.TurnedOff => "The plugin hub is turned off in Settings. Only bundled and local plugins load.",
        HubStatusKind.Checking => "Checking the plugin hub.",
        HubStatusKind.Verified => "The plugin hub's data is verified and current.",
        HubStatusKind.Offline => "Runesmith can't reach the plugin hub, so this is the saved catalog. Installs and updates are unavailable.",
        HubStatusKind.Unverifiable => $"Runesmith can't confirm the safety status of your plugins since {Date(Since)}. Installs and updates are unavailable. If your computer's date is wrong, correct it.",
        HubStatusKind.VerificationFailed => "The plugin hub's data could not be verified, so Runesmith kept what it had. Installs and updates are unavailable; try again later.",
        HubStatusKind.RunesmithTooOld => "The plugin hub needs a newer version of Runesmith. Update Runesmith to install and update plugins.",
        _ => "",
    };

    private static string Date(DateTimeOffset? when) =>
        when is { } value ? value.ToLocalTime().ToString("d MMMM yyyy", CultureInfo.CurrentCulture) : "the last check";
}
