using Runesmith.Hub.State;
using RunesmithHub.Protocol.Catalog;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Resolution;
using RunesmithHub.Protocol.Versioning;

namespace Runesmith.Hub.Updates;

/// <summary>Finds updates and decides which may happen by themselves.</summary>
/// <remarks>An update is automatic only for official and verified plugins with automatic updates on, within the same compatibility line, and
/// when nothing in its plan adds a capability, installs an unverified plugin, removes or downgrades a plugin. Unverified plugins never update
/// by themselves.</remarks>
internal static class UpdateFinder
{
    public static IReadOnlyList<UpdateCandidate> Find(
        HubCatalog catalog, Func<ResolutionRequest, ResolutionResult> resolve, HubState state, HubPreferences preferences, RunesmithHost host)
    {
        var candidates = new List<UpdateCandidate>();
        foreach (var plugin in state.Plugins)
        {
            if (!SemanticVersion.TryParse(plugin.Version, out var installed) || Newest(catalog, plugin.Id, host, preferences) is not { } newest || newest.Version <= installed)
                continue;

            var result = resolve(ResolutionRequest.Update(plugin.Id));
            var target = result.Plan?.Find(plugin.Id)?.Record ?? newest;
            if (target.Version <= installed)
                continue;

            candidates.Add(Classify(catalog, plugin.Id, installed, target, result, preferences, plugin.AutoUpdate, Capabilities(catalog, plugin.Id, installed)));
        }

        return [.. candidates.OrderBy(c => c.Kind).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static VersionRecord? Newest(HubCatalog catalog, string id, RunesmithHost host, HubPreferences preferences) =>
        catalog.GetInstallableVersions(id, host, preferences.AllowedTiers, preferences.PreReleases.Contains(id)) is [var newest, ..] ? newest : null;

    private static HashSet<string>? Capabilities(HubCatalog catalog, string id, SemanticVersion version) =>
        catalog.FindPlugin(id)?.FindVersion(version)?.Capabilities.Declared.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);

    private static UpdateCandidate Classify(
        HubCatalog catalog, string id, SemanticVersion installed, VersionRecord target, ResolutionResult result, HubPreferences preferences, bool pluginAutoUpdate,
        HashSet<string>? installedCapabilities)
    {
        var record = catalog.FindPlugin(id)!;
        var reasons = new List<string>();
        var added = installedCapabilities is null
            ? target.Capabilities.Declared.Select(c => c.Id).ToList()
            : [.. target.Capabilities.Declared.Select(c => c.Id).Where(c => !installedCapabilities.Contains(c))];

        if (added.Count > 0)
            reasons.Add(added.Count == 1 ? "It adds a capability." : "It adds capabilities.");
        if (installed.Line != target.Version.Line)
            reasons.Add($"It moves to a new line, {target.Version.Line}.");
        if (result.Plan is { } plan)
        {
            foreach (var operation in plan.Changes.Where(op => op.PluginId != id))
            {
                if (operation.Kind == OperationKind.Install && operation.Tier == PluginTier.Unverified)
                    reasons.Add($"It adds {operation.Name}, which is unverified.");
                else if (operation.Kind is OperationKind.Remove or OperationKind.Downgrade)
                    reasons.Add($"It {(operation.Kind == OperationKind.Remove ? "removes" : "downgrades")} {operation.Name}.");
                else if (operation.Record is not null && NewCapabilities(catalog, operation).Count > 0)
                    reasons.Add($"{operation.Name} gets new capabilities with it.");
            }
        }

        var kind = record.Tier == PluginTier.Unverified || !preferences.AutoUpdate || !pluginAutoUpdate ? UpdateKind.Manual
            : reasons.Count > 0 || !result.Succeeded ? UpdateKind.NeedsReview
            : UpdateKind.Automatic;
        return new UpdateCandidate(id, record.Name, record.Tier, installed.ToString(), target, kind, result, reasons, added);
    }

    private static List<string> NewCapabilities(HubCatalog catalog, PlanOperation operation)
    {
        var before = operation.From is { } from ? catalog.FindPlugin(operation.PluginId)?.FindVersion(from)?.Capabilities.Declared.Select(c => c.Id).ToHashSet(StringComparer.Ordinal) : null;
        return [.. operation.Record!.Capabilities.Declared.Select(c => c.Id).Where(c => before is null || !before.Contains(c))];
    }
}
