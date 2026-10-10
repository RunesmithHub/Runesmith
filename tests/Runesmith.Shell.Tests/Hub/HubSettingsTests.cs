using Runesmith.Shell.Hub;
using Runesmith.Shell.Pages;
using RunesmithHub.Protocol.Catalog;

namespace Runesmith.Shell.Tests.Hub;

public sealed class HubSettingsTests
{
    [Theory]
    [InlineData("all-with-warnings", AllowedTiers.All)]
    [InlineData("official-and-verified", AllowedTiers.OfficialAndVerified)]
    [InlineData("official-only", AllowedTiers.OfficialOnly)]
    [InlineData("something-else", AllowedTiers.All)]
    public void TheAllowedTiersSettingReadsAsATierRule(string value, AllowedTiers expected) => Assert.Equal(expected, HubSettings.ParseAllowedTiers(value));

    [Theory]
    [InlineData("all-with-warnings", "All with warnings")]
    [InlineData("official-only", "Official only")]
    [InlineData("afterDelay", "After delay")]
    public void ChoicesShowAsWords(string choice, string shown) => Assert.Equal(shown, SettingsPage.Display(choice));

    [Fact]
    public void EveryHubSettingIsOnThePluginsPage()
    {
        var settings = new HubSettings().Settings.ToList();

        Assert.Equal(
            [HubSettings.Enabled, HubSettings.AllowedTiers, HubSettings.AutoUpdate, HubSettings.CheckInterval, HubSettings.RegisterLinks, HubSettings.PreReleases,
                HubSettings.DeclinedSuggestions],
            settings.Select(s => s.Key));
        Assert.All(settings, s => Assert.Equal("Plugins", s.Category));
        var interval = settings.Single(s => s.Key == HubSettings.CheckInterval);
        Assert.Equal((6, 1.0, 24.0), ((int)interval.DefaultValue, interval.Minimum!.Value, interval.Maximum!.Value));
    }
}
