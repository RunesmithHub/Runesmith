using Avalonia.Media;
using HammerUI.Controls;
using Runesmith.Shell.Appearance;

namespace Runesmith.Shell.Tests.Appearance;

public sealed class AppearancePickersTests
{
    [Fact]
    public void OffersFollowingTheSystemAndNamesThePluginOfEachTheme()
    {
        var choices = AppearancePickers.Themes(FakeAppearance.Catalog(new FakeAppearance([FakeAppearance.Dusk])));

        Assert.Equal([AppearanceCatalog.System, "dark", "light", "ember.dusk"], choices.Select(c => c.Value));
        Assert.Equal("Dark, ember.themes", choices[^1].Detail);
        Assert.Equal(3, choices[^1].Swatch.Count);
        Assert.Equal("The color theme's own", AppearancePickers.Schemes(FakeAppearance.Catalog())[0].Title);
    }

    [Fact]
    public Task ShowsAValueNoLongerOfferedAsNotInstalledAndSetsThePickedOne() => HeadlessSession.Value.Dispatch(() =>
    {
        var picked = new List<string>();
        var box = AppearancePickers.Picker(AppearancePickers.Themes(FakeAppearance.Catalog()), "ember.dusk", picked.Add);

        var shown = Assert.IsType<AppearanceChoice>(box.SelectedItem);
        Assert.Equal("Not installed", shown.Detail);
        Assert.Empty(picked);

        box.SelectedIndex = 2;

        Assert.Equal(["light"], picked);
    }, CancellationToken.None);

    [Fact]
    public Task SetsTheAccentFromASwatchAndGoesBackToTheThemesOwn() => HeadlessSession.Value.Dispatch(() =>
    {
        var picked = new List<string>();
        var row = AppearancePickers.Accent("#22C55E", Color.Parse("#7073F6"), picked.Add);
        var swatches = Assert.IsType<ColorSwatchPicker>(row.Children[0]);
        var themeDefault = Assert.IsType<Avalonia.Controls.Button>(row.Children[1]);

        swatches.SelectedColor = Color.Parse("#3B82F6");
        themeDefault.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

        Assert.Equal(["#3B82F6", ""], picked);
        Assert.False(Assert.IsType<Avalonia.Controls.Button>(AppearancePickers.Accent("", Color.Parse("#7073F6"), picked.Add).Children[1]).IsEnabled);
    }, CancellationToken.None);
}
