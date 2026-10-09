using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Resolution;

namespace Runesmith.Hub.Updates;

/// <summary>How an update may happen.</summary>
public enum UpdateKind
{
    /// <summary>It updates by itself in the background and takes effect at the next start.</summary>
    Automatic,

    /// <summary>It changes something the user must approve, such as a new capability or major version, so the install dialog shows it.</summary>
    NeedsReview,

    /// <summary>It is never automatic: an unverified plugin, or automatic updates are off.</summary>
    Manual,
}

/// <summary>An available update for an installed or bundled plugin.</summary>
/// <param name="From">The installed version.</param>
/// <param name="Result">The plan that applies the update, or why there is none.</param>
/// <param name="Reasons">Why it needs review, in short sentences; empty when it is automatic.</param>
/// <param name="NewCapabilities">The capabilities the update adds.</param>
/// <param name="IsBundled">Whether the installed version is the one Runesmith ships.</param>
public sealed record UpdateCandidate(
    string PluginId, string Name, PluginTier Tier, string From, VersionRecord To, UpdateKind Kind, ResolutionResult Result,
    IReadOnlyList<string> Reasons, IReadOnlyList<string> NewCapabilities, bool IsBundled);

/// <summary>A plugin Runesmith ships, as the hub needs to know it.</summary>
public sealed record BundledPlugin(string Id, string Version, IReadOnlyCollection<string> Capabilities);
