using Runesmith.Hub.State;
using RunesmithHub.Protocol.Catalog;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Versioning;

namespace Runesmith.Hub.Enforcement;

/// <summary>What the hub's states mean for what is installed.</summary>
public static class Moderation
{
    /// <summary>Gets the reason category as users read it.</summary>
    public static string Label(ModerationReason reason) => reason switch
    {
        ModerationReason.Malicious => "Malicious",
        ModerationReason.CompromisedAccount => "Compromised account",
        ModerationReason.Vulnerability => "Vulnerability",
        ModerationReason.Broken => "Broken",
        ModerationReason.Policy => "Breaks the hub's rules",
        ModerationReason.Legal => "Legal",
        ModerationReason.DeveloperRequest => "At its developer's request",
        _ => reason.ToString(),
    };

    /// <summary>Gets the reason category from how the installed state stores it, or null.</summary>
    public static string? Label(string? stored) =>
        Enum.TryParse<ModerationReason>(stored, ignoreCase: false, out var reason) ? Label(reason) : null;

    /// <summary>Gets the effective state of an installed plugin's version, or null when the index does not have it.</summary>
    public static EffectiveState? StateOf(HubCatalog catalog, InstalledHubPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(plugin);
        return SemanticVersion.TryParse(plugin.Version, out var version) ? catalog.GetEffectiveState(plugin.Id, version) : null;
    }

    /// <summary>Gets the installed plugins that need any of some plugins, directly or through others, from their installed versions' records.</summary>
    public static IReadOnlyList<InstalledHubPlugin> Dependents(HubCatalog catalog, IEnumerable<InstalledHubPlugin> installed, IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var candidates = installed.ToList();
        var needed = new HashSet<string>(ids, StringComparer.Ordinal);
        var found = new List<InstalledHubPlugin>();
        bool added;
        do
        {
            added = false;
            foreach (var plugin in candidates.Where(p => !needed.Contains(p.Id)))
            {
                if (Record(catalog, plugin) is { } record && record.Dependencies.Any(dependency => needed.Contains(dependency.Id)))
                {
                    needed.Add(plugin.Id);
                    found.Add(plugin);
                    added = true;
                }
            }
        }
        while (added);

        return found;
    }

    /// <summary>Gets the version record of an installed plugin's version, or null.</summary>
    public static VersionRecord? Record(HubCatalog catalog, InstalledHubPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(plugin);
        return SemanticVersion.TryParse(plugin.Version, out var version) ? catalog.FindPlugin(plugin.Id)?.FindVersion(version) : null;
    }
}
