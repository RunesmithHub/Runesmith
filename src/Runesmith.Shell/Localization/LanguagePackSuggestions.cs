using System.Composition;
using System.Globalization;
using Runesmith.Localization;
using Runesmith.Sdk.Settings;
using Runesmith.Shell.Hub;
using RunesmithHub.Protocol.Catalog;
using RunesmithHub.Protocol.Index;

namespace Runesmith.Shell.Localization;

/// <summary>An official language pack on the hub that Runesmith suggests.</summary>
/// <param name="PluginId">The pack's id on the hub.</param>
/// <param name="Language">The BCP 47 tag of the language it adds.</param>
/// <param name="Name">The language's name in itself, such as Nederlands.</param>
/// <param name="EnglishName">The language's name in English, such as Dutch.</param>
public sealed record SuggestedLanguagePack(string PluginId, string Language, string Name, string EnglishName)
{
    /// <summary>Gets what the suggestion says, in the language that shows.</summary>
    public string Message => Strings.LocalizationSuggestion(Name, EnglishName);
}

/// <summary>Suggests the official language pack of the system's language, or of its base language, when the hub has one that is not
/// installed. It suggests once in a session, only while the language follows the system and the hub can install, and never again once the
/// user said not to.</summary>
[Export]
[Shared]
public sealed class LanguagePackSuggestions
{
    private readonly ISettingsService settings;
    private readonly Func<CultureInfo> system;
    private readonly Func<bool> canInstall;
    private readonly Func<IEnumerable<SuggestedLanguagePack>> official;
    private readonly Func<string, bool> isInstalled;
    private readonly Action<string> showPage;
    private bool suggested;

    [ImportingConstructor]
    public LanguagePackSuggestions(ISettingsService settings, Lazy<HubService> hub, Lazy<ShellLocalization> localization)
        : this(
            settings,
            () => localization.Value.SystemCulture,
            () => hub.Value.Client.Status.CanInstall,
            () => Official(hub.Value.Client.Catalog, hub.Value.Client.Host),
            id => hub.Value.Model.Installed().Any(row => string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase)),
            id => hub.Value.ShowPage(id))
    {
    }

    /// <param name="system">Gets the system's culture.</param>
    /// <param name="canInstall">Whether the hub is on and its index verified, so the pack can be installed.</param>
    /// <param name="official">Gets the official language packs the hub offers.</param>
    /// <param name="isInstalled">Whether a plugin is installed, from anywhere and turned on or off.</param>
    /// <param name="showPage">Opens a plugin's page in the plugin manager.</param>
    internal LanguagePackSuggestions(
        ISettingsService settings, Func<CultureInfo> system, Func<bool> canInstall, Func<IEnumerable<SuggestedLanguagePack>> official, Func<string, bool> isInstalled,
        Action<string> showPage)
    {
        this.settings = settings;
        this.system = system;
        this.canInstall = canInstall;
        this.official = official;
        this.isInstalled = isInstalled;
        this.showPage = showPage;
    }

    /// <summary>Gets the language pack to suggest now, or null, and counts it as suggested for the session.</summary>
    public SuggestedLanguagePack? Next()
    {
        if (suggested || !canInstall() || !string.Equals(settings.Get<string>(SettingKeys.Language), LocalizationOptions.SystemLanguage, StringComparison.OrdinalIgnoreCase))
            return null;

        var packs = official().ToList();
        var pack = LocalizationService.Chain(system().Name)
            .Where(tag => tag != "en")
            .Select(tag => packs.FirstOrDefault(candidate => string.Equals(candidate.Language, tag, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(candidate => candidate is not null);
        if (pack is null || isInstalled(pack.PluginId) || PluginSuggestions.Declined(settings).Contains(pack.PluginId))
            return null;

        suggested = true;
        return pack;
    }

    /// <summary>Opens the pack's page in the plugin manager, where the user can install it.</summary>
    public void Install(SuggestedLanguagePack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        showPage(pack.PluginId);
    }

    /// <summary>Stops suggesting the pack, in this session and later ones.</summary>
    public void StopSuggesting(SuggestedLanguagePack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        PluginSuggestions.Decline(settings, pack.PluginId);
    }

    /// <summary>Gets the official language packs a catalog offers for this Runesmith, by the newest version of each that can be installed.</summary>
    internal static IEnumerable<SuggestedLanguagePack> Official(HubCatalog? catalog, RunesmithHost host)
    {
        if (catalog is null)
            yield break;

        foreach (var plugin in catalog.Plugins.Where(p => p.Tier == PluginTier.Official).OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            if (catalog.GetInstallableVersions(plugin.Id, host) is [{ LanguagePack: { } pack }, ..])
                yield return new SuggestedLanguagePack(plugin.Id, pack.Language, pack.Name, pack.EnglishName);
        }
    }
}

/// <summary>The line over the window's content that suggests installing the official language pack of the system's language.</summary>
internal static class LanguagePackSuggestionBar
{
    /// <param name="close">Removes the line.</param>
    public static Avalonia.Controls.Control Create(SuggestedLanguagePack pack, LanguagePackSuggestions suggestions, Action close) =>
        PluginSuggestionBar.Create(
            new SuggestionText(() => pack.Message, () => Strings.LocalizationSuggestionInstall, () => Strings.LocalizationSuggestionDecline, () => Strings.LocalizationSuggestionClose),
            () => suggestions.Install(pack),
            () => suggestions.StopSuggesting(pack),
            close,
            icon: "globe");
}
