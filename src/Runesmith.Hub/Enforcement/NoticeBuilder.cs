using Runesmith.Hub.State;
using RunesmithHub.Protocol.Catalog;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Versioning;

namespace Runesmith.Hub.Enforcement;

/// <summary>Turns the hub's states of what is installed into notices for the plugin manager.</summary>
internal static class NoticeBuilder
{
    public static IReadOnlyList<PluginNotice> Build(
        HubCatalog? catalog, HubState state, IReadOnlyDictionary<string, IReadOnlyList<string>> changedFiles, RunesmithHost host, HubPreferences preferences)
    {
        var notices = new List<PluginNotice>();
        foreach (var entry in state.Quarantined.Where(q => state.Find(q.Id) is null))
        {
            var dependents = entry.Dependents.Select(id => state.Find(id)).OfType<InstalledHubPlugin>().ToList();
            notices.Add(new PluginNotice(NoticeKind.Quarantined, entry.Id, entry.Name, entry.Version, $"{entry.Name} was disabled because Runesmith found it harmful.")
            {
                Message = Join(Moderation.Label(entry.Reason) is { } reason ? $"Reason: {reason}." : null, entry.Note, entry.Remediation is { } fix ? $"What to do: {fix}" : null),
                Advisory = entry.Advisory,
                UpdateTo = catalog is null ? null : Fix(catalog, entry.Id, host, preferences),
                Dependents = [.. dependents.Select(d => catalog?.FindPlugin(d.Id)?.Name ?? d.Id)],
                DependentIds = [.. dependents.Select(d => d.Id)],
            });
        }

        foreach (var plugin in state.Plugins)
        {
            var record = catalog?.FindPlugin(plugin.Id);
            var name = record?.Name ?? plugin.Id;
            if (changedFiles.TryGetValue(plugin.Id, out var problems))
            {
                notices.Add(new PluginNotice(NoticeKind.FilesChanged, plugin.Id, name, plugin.Version, $"{name} is off because its files changed since it was installed.")
                {
                    Message = string.Join(" ", problems.Take(3)) + (problems.Count > 3 ? $" And {problems.Count - 3} more." : ""),
                });
                continue;
            }

            if (catalog is null || record is null || state.IsPending(plugin.Id) || Moderation.StateOf(catalog, plugin) is not { } effective)
                continue;

            var fix = Fix(catalog, plugin.Id, host, preferences);
            switch (effective.State)
            {
                case VersionState.Blocked:
                    var dependents = Moderation.Dependents(catalog, state.Plugins, [plugin.Id]);
                    notices.Add(new PluginNotice(NoticeKind.Blocked, plugin.Id, name, plugin.Version, $"{name} was disabled because Runesmith found it harmful.")
                    {
                        Message = Join(effective.Moderation is { } m ? $"Reason: {Moderation.Label(m.Reason)}." : null, effective.Moderation?.Note,
                            "Restart Runesmith to finish turning it off."),
                        Advisory = effective.Moderation?.Advisory,
                        UpdateTo = fix,
                        Dependents = [.. dependents.Select(d => catalog.FindPlugin(d.Id)?.Name ?? d.Id)],
                        DependentIds = [.. dependents.Select(d => d.Id)],
                    });
                    break;
                case VersionState.Frozen:
                    notices.Add(new PluginNotice(NoticeKind.Frozen, plugin.Id, name, plugin.Version,
                        $"New installs of {name} are paused while Runesmith looks into a report. You can keep using it."));
                    break;
                case VersionState.Yanked:
                    notices.Add(new PluginNotice(NoticeKind.Yanked, plugin.Id, name, plugin.Version, $"{name} {plugin.Version} was withdrawn by its developer.")
                    {
                        UpdateTo = fix,
                    });
                    break;
                case VersionState.Deleted:
                    notices.Add(new PluginNotice(NoticeKind.Deleted, plugin.Id, name, plugin.Version,
                        $"{name} is no longer available on the hub. It keeps working, but won't get updates."));
                    break;
                default:
                    if ((record.State == PluginState.Deprecated || record.Deprecated is not null) && record.Deprecated is { } deprecated)
                    {
                        var replacement = deprecated.Replacement is { } id ? catalog.FindPlugin(id)?.Name ?? id : null;
                        notices.Add(new PluginNotice(NoticeKind.Deprecated, plugin.Id, name, plugin.Version, $"{name} is no longer maintained.")
                        {
                            Message = Join(deprecated.Reason, replacement is null ? null : $"Its developer suggests {replacement} instead."),
                            Replacement = deprecated.Replacement,
                        });
                    }

                    break;
            }
        }

        return [.. notices.OrderBy(n => n.IsDanger ? 0 : n.Kind == NoticeKind.Frozen ? 1 : 2).ThenBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static SemanticVersion? Fix(HubCatalog catalog, string id, RunesmithHost host, HubPreferences preferences) =>
        catalog.GetInstallableVersions(id, host, preferences.AllowedTiers, preferences.PreReleases.Contains(id)) is [var newest, ..] ? newest.Version : null;

    private static string? Join(params string?[] parts)
    {
        var text = string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
        return text.Length == 0 ? null : text;
    }
}
