using System.Globalization;
using Runesmith.Localization;
using Runesmith.Sdk.Settings;
using Runesmith.Shell.Hub;
using Runesmith.Shell.Localization;
using Runesmith.Shell.Tests.Appearance;

namespace Runesmith.Shell.Tests.Localization;

public sealed class LanguagePackSuggestionsTests
{
    private readonly MemorySettings settings = new() { Values = { [SettingKeys.Language] = LocalizationOptions.SystemLanguage, [HubSettings.DeclinedSuggestions] = "" } };
    private readonly HashSet<string> installed = new(StringComparer.Ordinal);
    private readonly List<string> shownPages = [];
    private CultureInfo system = CultureInfo.GetCultureInfo("nl-BE");
    private bool canInstall = true;

    private static IEnumerable<SuggestedLanguagePack> Official =>
    [
        new("runesmith.dutch", "nl", "Nederlands", "Dutch"),
        new("runesmith.german", "de", "Deutsch", "German"),
    ];

    [Fact]
    public void SuggestsTheOfficialPackOfTheSystemsLanguageOrItsBaseLanguageOnce()
    {
        var suggestions = Create();

        var pack = suggestions.Next();

        Assert.Equal("runesmith.dutch", pack?.PluginId);
        Assert.Equal("Runesmith is available in Nederlands. Install the official Dutch language pack to use it.", pack?.Message);
        Assert.Null(suggestions.Next());
    }

    [Fact]
    public void SuggestsNothingForEnglishOrALanguageWithoutAPack()
    {
        system = CultureInfo.GetCultureInfo("en-GB");
        Assert.Null(Create().Next());

        system = CultureInfo.GetCultureInfo("fr-FR");
        Assert.Null(Create().Next());
    }

    [Fact]
    public void SuggestsNothingWhileALanguageIsChosenThePackIsInstalledOrTheHubCannotInstall()
    {
        settings.Values[SettingKeys.Language] = "en";
        Assert.Null(Create().Next());

        settings.Values[SettingKeys.Language] = LocalizationOptions.SystemLanguage;
        installed.Add("runesmith.dutch");
        Assert.Null(Create().Next());

        installed.Clear();
        canInstall = false;
        Assert.Null(Create().Next());

        canInstall = true;
        Assert.Equal("runesmith.dutch", Create().Next()?.PluginId);
    }

    [Fact]
    public void InstallOpensThePacksPageAndDeclineKeepsItQuietInLaterSessions()
    {
        var suggestions = Create();
        var pack = suggestions.Next()!;

        suggestions.Install(pack);
        suggestions.StopSuggesting(pack);

        Assert.Equal(["runesmith.dutch"], shownPages);
        Assert.Equal("runesmith.dutch", settings.Values[HubSettings.DeclinedSuggestions]);
        Assert.Null(Create().Next());
    }

    private LanguagePackSuggestions Create() =>
        new(settings, () => system, () => canInstall, () => Official, installed.Contains, shownPages.Add);
}
