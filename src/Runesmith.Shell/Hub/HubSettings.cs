using System.Composition;
using Runesmith.Hub;
using Runesmith.Sdk.Settings;
using RunesmithHub.Protocol.Catalog;

namespace Runesmith.Shell.Hub;

/// <summary>The plugin hub's settings.</summary>
[Export(typeof(ISettingContributor))]
public sealed class HubSettings : ISettingContributor
{
    public const string Category = "Plugins";

    /// <summary>Whether Runesmith uses the hub; when it is off, only bundled and local plugins load.</summary>
    public const string Enabled = "plugins.hub.enabled";

    /// <summary>Which tiers may be installed: <c>all-with-warnings</c>, <c>official-and-verified</c> or <c>official-only</c>.</summary>
    public const string AllowedTiers = "plugins.hub.allowedTiers";

    /// <summary>Whether official and verified plugins update by themselves.</summary>
    public const string AutoUpdate = "plugins.hub.autoUpdate";

    /// <summary>How many hours apart Runesmith checks the hub.</summary>
    public const string CheckInterval = "plugins.hub.checkInterval";

    /// <summary>Whether Runesmith registers itself for <c>runesmith://</c> links on Windows.</summary>
    public const string RegisterLinks = "plugins.hub.registerLinks";

    /// <summary>The ids of the plugins the user gets pre-releases of, separated by semicolons.</summary>
    public const string PreReleases = "plugins.hub.preReleases";

    public IEnumerable<SettingDefinition> Settings { get; } =
    [
        new(Enabled, "Use the plugin hub", Category, true)
        {
            Description = "Turn this off to use only the plugins that come with Runesmith and the ones in your plugins folder. Takes effect when Runesmith starts again.",
        },
        new(AllowedTiers, "Allowed plugins", Category, "all-with-warnings")
        {
            Description = "Which plugins may be installed. Unverified plugins always ask you to confirm before they install.",
            Choices = ["all-with-warnings", "official-and-verified", "official-only"],
        },
        new(AutoUpdate, "Update automatically", Category, true)
        {
            Description = "Official and verified plugins update by themselves within their version line. Unverified plugins never do.",
        },
        new(CheckInterval, "Check for changes every", Category, 6) { Description = "How many hours apart Runesmith checks the plugin hub, from 1 to 24.", Minimum = 1, Maximum = 24 },
        new(RegisterLinks, "Open runesmith:// links", Category, true)
        {
            Description = "Whether Runesmith registers itself on Windows to open runesmith:// links from the hub's website. Takes effect when Runesmith starts again.",
        },
        new(PreReleases, "Pre-releases", Category, "") { Description = "The ids of the plugins you get pre-releases of, separated by semicolons." },
    ];

    /// <summary>Reads the allowed tiers; anything unknown allows every tier, as the default does.</summary>
    public static AllowedTiers ParseAllowedTiers(string? value) => value switch
    {
        "official-and-verified" => RunesmithHub.Protocol.Catalog.AllowedTiers.OfficialAndVerified,
        "official-only" => RunesmithHub.Protocol.Catalog.AllowedTiers.OfficialOnly,
        _ => RunesmithHub.Protocol.Catalog.AllowedTiers.All,
    };

    /// <summary>Reads the user's hub settings.</summary>
    public static HubPreferences Preferences(ISettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new HubPreferences(
            ParseAllowedTiers(settings.Get<string>(AllowedTiers)),
            settings.Get<bool>(AutoUpdate),
            settings.Get<string>(PreReleases).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal));
    }
}
