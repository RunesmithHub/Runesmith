using Avalonia.Media;
using HammerUI.Theming;
using Runesmith.Languages.Highlighting;
using Runesmith.Sdk.Appearance;
using Runesmith.Shell.Appearance;

namespace Runesmith.Shell.Tests.Appearance;

public sealed class AppearanceCatalogTests
{
    [Fact]
    public void ListsRunesmithsOwnFirstThenThePluginsByNameWithThePluginTheyCameFrom()
    {
        var plugin = new FakeAppearance(
            [FakeAppearance.Dusk, new ColorTheme("ember.ash", "Ember Ash", IsDark: true)],
            iconThemes: [new FileIconTheme("ember.icons", "Ember Icons")]);

        var catalog = FakeAppearance.Catalog(plugin);

        Assert.Equal(["dark", "light", "runesmith-low-contrast-dark", "runesmith-low-contrast-light", "ember.ash", "ember.dusk"], catalog.Themes.Select(e => e.Theme.Id));
        Assert.Null(catalog.Themes[0].Plugin);
        Assert.Same(FakeAppearance.Plugin, catalog.FindTheme("Ember.Dusk")?.Plugin);
        Assert.Equal(["runesmith", "ember.icons"], catalog.IconThemes.Select(e => e.Theme.Id));
        Assert.Equal(
            [BuiltInColorSchemes.DarkId, BuiltInColorSchemes.LightId, BuiltInColorSchemes.LowContrastDarkId, BuiltInColorSchemes.LowContrastLightId],
            catalog.Schemes.Select(e => e.Scheme.Id));
        Assert.Empty(catalog.Problems);
    }

    [Fact]
    public void FillsTheColorsAThemeLeavesOutFromRunesmithsOwnAndGivesItsAccentReadableText()
    {
        var theme = new ColorTheme("pale", "Pale", IsDark: false) { Colors = new ThemeColors { Accent = "#FDE68A" } };

        var entry = Assert.Single(FakeAppearance.Catalog(new FakeAppearance([theme])).Themes, e => e.Theme.Id == "pale");

        Assert.Equal(ThemePalette.Light.Surface, entry.Palette.Surface);
        Assert.Equal(Color.Parse("#FDE68A"), entry.Palette.Accent);
        Assert.True(ThemePalettes.Contrast(entry.Palette.AccentForeground, entry.Palette.Accent) >= 4.5);
    }

    [Theory]
    [InlineData("#20232A", "#2A2D35", "too faint to read")]
    [InlineData("#F3E6D8", "not-a-color", "not colors")]
    public void LeavesOutAThemeWhoseTextCannotBeRead(string textPrimary, string surface, string reason)
    {
        var theme = new ColorTheme("bad", "Bad", IsDark: true) { Colors = new ThemeColors { Background = "#1A1A1A", Surface = surface, TextPrimary = textPrimary } };

        var catalog = FakeAppearance.Catalog(new FakeAppearance([theme]));

        Assert.Null(catalog.FindTheme("bad"));
        Assert.Contains(catalog.Problems, p => p.Contains("Bad (bad) is left out", StringComparison.Ordinal) && p.Contains(reason, StringComparison.Ordinal));
    }

    [Fact]
    public void LeavesOutADarkThemeWithALightBackground()
    {
        var theme = new ColorTheme("odd", "Odd", IsDark: true) { Colors = new ThemeColors { Background = "#FFFFFF", Surface = "#FAFAFA", TextPrimary = "#111111" } };

        var catalog = FakeAppearance.Catalog(new FakeAppearance([theme]));

        Assert.Null(catalog.FindTheme("odd"));
        Assert.Contains(catalog.Problems, p => p.Contains("its background is light", StringComparison.Ordinal));
    }

    [Fact]
    public void KeepsAThemeWithFaintSecondaryTextButSaysItMayBeHardToRead()
    {
        var theme = FakeAppearance.Dusk with { Colors = FakeAppearance.Dusk.Colors with { TextSecondary = "#3A3028" } };

        var catalog = FakeAppearance.Catalog(new FakeAppearance([theme]));

        Assert.NotNull(catalog.FindTheme(theme.Id));
        Assert.Contains(catalog.Problems, p => p.Contains("may be hard to read", StringComparison.Ordinal) && p.Contains("secondary text", StringComparison.Ordinal));
    }

    [Fact]
    public void KeepsTheFirstOfTwoThemesWithOneIdAndReportsAContributorThatFails()
    {
        var copy = new ColorTheme("DARK", "Not Runesmith Dark", IsDark: true);
        var catalog = FakeAppearance.Catalog(new FakeAppearance([copy, FakeAppearance.Dusk]));
        var failing = new AppearanceCatalog([new BuiltInAppearance(), new ThrowingThemes()], [], [new BuiltInAppearance()], _ => null);

        Assert.Equal("Runesmith Dark", catalog.FindTheme("dark")?.Theme.Name);
        Assert.Contains(catalog.Problems, p => p.Contains("another has that id", StringComparison.Ordinal));
        Assert.Equal(["dark", "light", "runesmith-low-contrast-dark", "runesmith-low-contrast-light"], failing.Themes.Select(e => e.Theme.Id));
        Assert.Contains(failing.Problems, p => p.Contains("could not list its color themes", StringComparison.Ordinal));
    }

    [Fact]
    public void LeavesOutSchemesWhoseFileIsMissingAndIconThemesWithBadColors()
    {
        var plugin = new FakeAppearance(
            schemes: [new ColorScheme("gone", "Gone", Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "gone.json"))],
            iconThemes: [new FileIconTheme("bad.icons", "Bad Icons") { File = new FileIcon("M4 4h16v16H4z", "orange-ish") }]);

        var catalog = FakeAppearance.Catalog(plugin);

        Assert.Null(catalog.FindScheme("gone"));
        Assert.Null(catalog.FindIconTheme("bad.icons"));
        Assert.Equal(2, catalog.Problems.Count);
    }

    [Theory]
    [InlineData(BuiltInAppearance.LowContrastDarkTheme, BuiltInColorSchemes.LowContrastDarkId, true)]
    [InlineData(BuiltInAppearance.LowContrastLightTheme, BuiltInColorSchemes.LowContrastLightId, false)]
    public void ListsTheLowContrastThemesWithTheirSchemesAndKeepsTheirTextReadable(string id, string schemeId, bool isDark)
    {
        var catalog = FakeAppearance.Catalog();

        var entry = Assert.IsType<ThemeEntry>(catalog.FindTheme(id));
        var palette = entry.Palette;
        Assert.Empty(catalog.Problems);
        Assert.Null(entry.Plugin);
        Assert.Equal(isDark, palette.IsDark);
        Assert.Equal(schemeId, entry.Theme.ColorScheme);
        Assert.NotNull(catalog.FindScheme(schemeId));
        Assert.All([palette.Background, palette.Surface, palette.SurfaceRaised, palette.SurfaceSunken],
            surface => Assert.InRange(ThemePalettes.Contrast(palette.TextPrimary, surface), ThemePalettes.ReadableContrast, 9));
        Assert.All([palette.Background, palette.Surface, palette.SurfaceRaised, palette.SurfaceSunken],
            surface => Assert.True(ThemePalettes.Contrast(palette.TextSecondary, surface) >= ThemePalettes.UnreadableContrast));
        Assert.True(ThemePalettes.Contrast(palette.TextMuted, palette.Surface) >= ThemePalettes.UnreadableContrast);
        Assert.True(ThemePalettes.Contrast(palette.AccentForeground, palette.Accent) >= ThemePalettes.ReadableContrast);
        Assert.True(ThemePalettes.Contrast(palette.Accent, palette.Surface) >= ThemePalettes.UnreadableContrast);
        Assert.True(ThemePalettes.Contrast(palette.TextPrimary, palette.Surface) < ThemePalettes.Contrast(Default(isDark).TextPrimary, Default(isDark).Surface));
    }

    [Theory]
    [InlineData(BuiltInColorSchemes.LowContrastDarkId, true)]
    [InlineData(BuiltInColorSchemes.LowContrastLightId, false)]
    public void ColorsCodeInTheLowContrastSchemesReadablyOnTheirThemesEditor(string schemeId, bool isDark)
    {
        var catalog = FakeAppearance.Catalog();
        var scheme = Assert.IsType<SchemeEntry>(catalog.FindScheme(schemeId)).Scheme;
        var surface = catalog.Themes.Single(e => e.Theme.ColorScheme == schemeId).Palette.Surface;
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(scheme.FilePath));

        Assert.Equal(isDark, scheme.IsDark);
        Assert.Equal(surface, Color.Parse(json.RootElement.GetProperty("colors").GetProperty("editor.background").GetString()!));
        foreach (var rule in json.RootElement.GetProperty("tokenColors").EnumerateArray())
        {
            if (!rule.GetProperty("settings").TryGetProperty("foreground", out var foreground))
                continue;
            var name = rule.TryGetProperty("name", out var n) ? n.GetString() : "default";
            var least = name is "comment" or "doc" or "punctuation" ? ThemePalettes.UnreadableContrast : ThemePalettes.ReadableContrast;
            Assert.True(ThemePalettes.Contrast(Color.Parse(foreground.GetString()!), surface) >= least, $"{name} in {schemeId}");
        }
    }

    [Theory]
    [InlineData("#FFFFFF", "#000000", 21)]
    [InlineData("#777777", "#777777", 1)]
    [InlineData("#00FFFFFF", "#000000", 1)]
    public void MeasuresContrastAfterBlendingTransparency(string foreground, string background, double expected) =>
        Assert.Equal(expected, ThemePalettes.Contrast(Color.Parse(foreground), Color.Parse(background)), 1);

    private static ThemePalette Default(bool isDark) => isDark ? ThemePalette.Dark : ThemePalette.Light;

    private sealed class ThrowingThemes : IColorThemeContributor
    {
        public IEnumerable<ColorTheme> Themes => throw new InvalidOperationException("Broken");
    }
}
