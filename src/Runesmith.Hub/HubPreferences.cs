using RunesmithHub.Protocol.Catalog;

namespace Runesmith.Hub;

/// <summary>The user's hub settings that decide what may be installed and updated.</summary>
/// <param name="AutoUpdate">Whether official and verified plugins update by themselves.</param>
/// <param name="PreReleases">The plugins the user opted into pre-releases for.</param>
public sealed record HubPreferences(AllowedTiers AllowedTiers, bool AutoUpdate, IReadOnlySet<string> PreReleases)
{
    public static HubPreferences Default { get; } = new(AllowedTiers.All, true, new HashSet<string>(StringComparer.Ordinal));
}
