using Runesmith.Hub;
using Runesmith.Hub.Enforcement;
using RunesmithHub.Protocol.Catalog;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Versioning;

namespace Runesmith.Shell.Hub;

/// <summary>Why a plugin page offers no install, and how loud it says so.</summary>
public enum PageNoticeKind
{
    Info,
    Warning,
    Danger,
    Quiet,
}

/// <summary>A notice at the top of a plugin page.</summary>
public sealed record PageNotice(PageNoticeKind Kind, string Title, string? Message = null)
{
    public string? Advisory { get; init; }

    /// <summary>Gets the name of the notice's icon, or null for its kind's.</summary>
    public string? Icon { get; init; }
}

/// <summary>What a plugin's page shows, built from the signed records only.</summary>
public sealed record PluginPageModel
{
    public required string PluginId { get; init; }

    /// <summary>Gets the plugin's record, or null when the hub has no plugin with the id.</summary>
    public PluginRecord? Record { get; init; }

    public string? Publisher { get; init; }

    /// <summary>Gets the selected version.</summary>
    public VersionRecord? Version { get; init; }

    /// <summary>Gets every version the page lists, with its state, highest first.</summary>
    public IReadOnlyList<(VersionRecord Version, EffectiveState State)> Versions { get; init; } = [];

    /// <summary>Gets the notices to show above the page, most important first.</summary>
    public IReadOnlyList<PageNotice> Notices { get; init; } = [];

    /// <summary>Gets the version that runs now, or null when the plugin is not installed.</summary>
    public string? InstalledVersion { get; init; }

    /// <summary>Gets whether the hub installed it.</summary>
    public bool IsFromHub { get; init; }

    /// <summary>Gets why the installed plugin does not run, or null when it runs.</summary>
    public string? Problem { get; init; }

    /// <summary>Gets whether a change to it takes effect at the next start.</summary>
    public bool IsPending { get; init; }

    /// <summary>Gets whether the selected version can be installed, or installed in place of the running one.</summary>
    public bool CanInstall { get; init; }

    /// <summary>Gets the review state as users read it.</summary>
    public string? Review { get; init; }

    /// <summary>Gets whether the selected version was reviewed by a person.</summary>
    public bool IsReviewed { get; init; }

    public bool IsInstalled => InstalledVersion is not null;

    /// <summary>Gets whether installing the selected version moves the plugin to a newer version.</summary>
    public bool IsUpdate => IsInstalled && Version is not null && SemanticVersion.TryParse(InstalledVersion, out var installed) && Version.Version > installed;

    /// <summary>Gets whether the selected version is the one that runs.</summary>
    public bool IsSelectedInstalled => IsInstalled && Version is not null && Version.Version.ToString() == InstalledVersion;

    internal static PluginPageModel Create(HubClient client, PageTarget target, HubPreferences preferences, string? running)
    {
        var status = client.Status;
        var catalog = client.Catalog;
        var state = client.State;
        var notices = new List<PageNotice>();
        if (catalog?.FindPlugin(target.PluginId) is not { } record)
        {
            notices.Add(status.HasCatalog && catalog is not null
                ? new PageNotice(PageNoticeKind.Warning, Strings.PluginsPageNotFound(target.PluginId), Strings.PluginsPageNotFoundHint)
                : new PageNotice(PageNoticeKind.Info, status.Describe()));
            return new PluginPageModel { PluginId = target.PluginId, Notices = notices, InstalledVersion = running };
        }

        var versions = record.Versions
            .Select(v => (Version: v, State: catalog.GetEffectiveState(record, v)))
            .Where(v => v.State.State is not (VersionState.Review or VersionState.Held))
            .ToList();
        var installable = catalog.GetInstallableVersions(record.Id, client.Host, preferences.AllowedTiers, preferences.PreReleases.Contains(record.Id));
        var selected = target.Version is { } wanted ? record.FindVersion(wanted) : null;
        if (target.Version is { } missing && (selected is null || versions.All(v => v.Version != selected)))
        {
            notices.Add(new PageNotice(PageNoticeKind.Quiet, Strings.PluginsPageVersionMissing(record.Name, missing.ToString())));
            selected = null;
        }

        selected ??= installable is [var newest, ..] ? newest : versions.FirstOrDefault().Version;
        var selectedState = selected is null ? null : catalog.GetEffectiveState(record, selected);
        var hub = state.Find(record.Id);
        var canInstall = status.CanInstall && selected is not null && installable.Contains(selected) && preferences.AllowedTiers.Allows(record.Tier);

        if (hub is null && state.Quarantined.FirstOrDefault(q => q.Id == record.Id) is { } quarantined)
        {
            notices.Add(new PageNotice(PageNoticeKind.Danger, Strings.PluginsPageQuarantined(record.Name, quarantined.Version),
                Moderation.Label(quarantined.Reason) is { } reason ? string.Join(" ", new[] { Strings.PluginsPageReason(reason), quarantined.Note, quarantined.Remediation }.Where(part => !string.IsNullOrWhiteSpace(part))) : quarantined.Note)
            {
                Advisory = quarantined.Advisory,
            });
        }

        if (selectedState?.State == VersionState.Blocked || record.State == RunesmithHub.Protocol.Index.PluginState.Blocked)
        {
            var moderation = selectedState?.Moderation;
            notices.Add(new PageNotice(PageNoticeKind.Danger, Strings.PluginsPageBlocked(record.Name),
                moderation is null ? null : (Strings.PluginsPageReason(Moderation.Label(moderation.Reason) ?? "") + " " + moderation.Note).Trim())
            { Advisory = moderation?.Advisory });
        }
        else if (selectedState?.State == VersionState.Frozen)
        {
            notices.Add(new PageNotice(PageNoticeKind.Info, Strings.PluginsPageFrozen(record.Name), running is null ? null : Strings.PluginsPageFrozenRunning));
        }
        else if (selectedState?.State == VersionState.Deleted || record.State == RunesmithHub.Protocol.Index.PluginState.Deleted)
        {
            notices.Add(new PageNotice(PageNoticeKind.Quiet, Strings.PluginsPageDeleted(record.Name)));
        }
        else if (selectedState?.State == VersionState.Yanked)
        {
            notices.Add(new PageNotice(PageNoticeKind.Quiet, Strings.PluginsPageYanked(record.Name, selected!.Version.ToString())));
        }

        if (record.Deprecated is { } deprecated)
        {
            var replacement = deprecated.Replacement is { } id ? catalog.FindPlugin(id)?.Name ?? id : null;
            notices.Add(new PageNotice(PageNoticeKind.Quiet, Strings.PluginsPageDeprecated(record.Name),
                replacement is null ? deprecated.Reason : (deprecated.Reason + " " + Strings.PluginsPageDeprecatedReplacement(replacement)).Trim()));
        }

        if (!preferences.AllowedTiers.Allows(record.Tier))
            notices.Add(new PageNotice(PageNoticeKind.Warning, Strings.PluginsPageTierNotAllowed(name: record.Name, tier: Tier(record.Tier)), Strings.PluginsPageTiersAllowed(Allowed(preferences.AllowedTiers))));
        else if (!status.CanInstall)
            notices.Add(new PageNotice(status.Kind is HubStatusKind.Unverifiable or HubStatusKind.VerificationFailed ? PageNoticeKind.Warning : PageNoticeKind.Info, status.Describe()));
        else if (selected is not null && !client.Host.CanRun(selected))
            notices.Add(new PageNotice(PageNoticeKind.Info, Strings.PluginsPageIncompatible(record.Name, selected.Version.ToString()),
                Strings.PluginsPageIncompatibleDetail(selected.RunesmithApi.ToString(), client.Host.ApiVersion.ToString())));

        if (record.Tier == PluginTier.Unverified && canInstall && hub is null)
            notices.Add(new PageNotice(PageNoticeKind.Warning, Strings.PluginsPageUnverified, Strings.PluginsPageUnverifiedDetail) { Icon = "AlertTriangle" });

        return new PluginPageModel
        {
            PluginId = record.Id,
            Record = record,
            Publisher = catalog.FindPublisher(record.Publisher)?.DisplayName ?? record.Publisher,
            Version = selected,
            Versions = versions,
            Notices = notices,
            InstalledVersion = running ?? hub?.Version,
            IsFromHub = hub is not null,
            IsPending = state.IsPending(record.Id),
            CanInstall = canInstall,
            IsReviewed = selected?.Review.State == ReviewState.Reviewed,
            Review = selected?.Review is { State: ReviewState.Reviewed } review
                ? review.At is { } at ? Strings.PluginsReviewedOn(at) : Strings.PluginsReviewed
                : Strings.PluginsPageChecked,
        };
    }

    /// <summary>Gets a tier as a word, such as "unverified".</summary>
    public static string Tier(PluginTier tier) => tier switch
    {
        PluginTier.Official => "official",
        PluginTier.Verified => "verified",
        _ => "unverified",
    };

    private static string Allowed(AllowedTiers tiers) => tiers == AllowedTiers.OfficialOnly ? Strings.PluginsAllowedOfficial : Strings.PluginsAllowedOfficialVerified;

    /// <summary>Gets a tier's name, such as "Unverified".</summary>
    public static string TierName(PluginTier tier) => tier switch
    {
        PluginTier.Official => Strings.PluginsTierNameOfficial,
        PluginTier.Verified => Strings.PluginsTierNameVerified,
        _ => Strings.PluginsTierNameUnverified,
    };
}
