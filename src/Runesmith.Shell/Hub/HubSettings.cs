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

    /// <summary>Whether Runesmith uses the hub; when it is off, only the plugins in the user's plugins folder load.</summary>
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

    /// <summary>The ids of the official plugins Runesmith no longer suggests, separated by semicolons.</summary>
    public const string DeclinedSuggestions = "plugins.hub.declinedSuggestions";

    public IEnumerable<SettingDefinition> Settings =>
    [
        new(Enabled, Strings.SettingsUseThePluginHub, Category, true)
        {
            Description = Strings.SettingsUseThePluginHubDescription,
        },
        new(AllowedTiers, Strings.SettingsAllowedPlugins, Category, "all-with-warnings")
        {
            Description = Strings.SettingsAllowedPluginsDescription,
            Choices = ["all-with-warnings", "official-and-verified", "official-only"],
            ChoiceTitles = [Strings.SettingsChoiceAllWithWarnings, Strings.SettingsChoiceOfficialAndVerified, Strings.SettingsChoiceOfficialOnly],
        },
        new(AutoUpdate, Strings.SettingsUpdateAutomatically, Category, true)
        {
            Description = Strings.SettingsUpdateAutomaticallyDescription,
        },
        new(CheckInterval, Strings.SettingsCheckForChangesEvery, Category, 6) { Description = Strings.SettingsCheckForChangesEveryDescription, Minimum = 1, Maximum = 24 },
        new(RegisterLinks, Strings.SettingsOpenRunesmithLinks, Category, true)
        {
            Description = Strings.SettingsOpenRunesmithLinksDescription,
        },
        new(PreReleases, Strings.SettingsPreReleases, Category, "") { Description = Strings.SettingsPreReleasesDescription },
        new(DeclinedSuggestions, Strings.SettingsPluginsNotToSuggest, Category, "")
        {
            Description = Strings.SettingsPluginsNotToSuggestDescription,
        },
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
