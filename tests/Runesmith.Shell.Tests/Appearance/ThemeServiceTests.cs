using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using HammerUI.Theming;
using Runesmith.Languages;
using Runesmith.Languages.Highlighting;
using Runesmith.Sdk.Appearance;
using Runesmith.Sdk.Messaging;
using Runesmith.Sdk.Settings;
using Runesmith.Shell.Appearance;
using Runesmith.Shell.Services;

namespace Runesmith.Shell.Tests.Appearance;

public sealed class ThemeServiceTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "runesmith-theme-tests", Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> created = [];

    public void Dispose()
    {
        foreach (var disposable in created)
            disposable.Dispose();
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public Task AppliesAPluginThemeAndItsSchemeAtOnceAndKeepsTheChoice() => Run(() =>
    {
        var plugin = Plugin();
        var settings = TestSettings.Create(SettingsPath);
        var (service, syntax, _) = Create(settings, FakeAppearance.Catalog(plugin));
        var changes = 0;
        service.Changed += (_, _) => changes++;

        settings.Set(SettingKeys.Theme, "ember.dusk");

        Assert.Equal(1, changes);
        Assert.True(service.IsDark);
        Assert.Equal("ember.dusk", service.Current.Theme.Id);
        Assert.Equal(Color.Parse("#211912"), Theme.DarkPalette.Surface);
        Assert.Equal("ember.dusk", syntax.Current.Id);
        Assert.Contains("ember.dusk", File.ReadAllText(SettingsPath), StringComparison.Ordinal);

        var (restarted, _, _) = Create(TestSettings.Create(SettingsPath), FakeAppearance.Catalog(plugin));
        Assert.Equal("ember.dusk", restarted.Current.Theme.Id);
    });

    [Fact]
    public Task FallsBackToRunesmithsThemeWhenThePluginIsGoneAndKeepsTheSetting() => Run(() =>
    {
        TestSettings.Create(SettingsPath).Set(SettingKeys.Theme, "ember.dusk");

        var settings = TestSettings.Create(SettingsPath);
        var (service, syntax, output) = Create(settings, FakeAppearance.Catalog());

        Assert.Equal(BuiltInAppearance.DarkTheme, service.Current.Theme.Id);
        Assert.Equal(ThemePalette.Dark.Surface, Theme.DarkPalette.Surface);
        Assert.Equal(BuiltInColorSchemes.DarkId, syntax.Current.Id);
        Assert.Equal("ember.dusk", settings.Get<string>(SettingKeys.Theme));
        Assert.Contains(output.Lines, l => l.Contains("ember.dusk is not installed", StringComparison.Ordinal));
    });

    [Fact]
    public Task SwitchesBetweenRunesmithsLightAndDarkThemesAndFollowsTheSystem() => Run(() =>
    {
        var settings = TestSettings.Create(SettingsPath);
        var (service, syntax, _) = Create(settings, FakeAppearance.Catalog(Plugin()));
        settings.Set(SettingKeys.Theme, "ember.dusk");

        service.Toggle();

        Assert.False(service.IsDark);
        Assert.Equal(BuiltInAppearance.LightTheme, settings.Get<string>(SettingKeys.Theme));
        Assert.Equal(BuiltInColorSchemes.LightId, syntax.Current.Id);
        Assert.Equal(ThemePalette.Dark.Surface, Theme.DarkPalette.Surface);

        settings.Set(SettingKeys.Theme, AppearanceCatalog.System);

        Assert.Equal(ThemeVariant.Default, Application.Current!.RequestedThemeVariant);
        Assert.Equal(service.IsDark ? BuiltInAppearance.DarkTheme : BuiltInAppearance.LightTheme, service.Current.Theme.Id);
    });

    [Fact]
    public Task AppliesTheLowContrastThemesWithTheirSchemesAndTogglesBetweenThem() => Run(() =>
    {
        var settings = TestSettings.Create(SettingsPath);
        var (service, syntax, output) = Create(settings, FakeAppearance.Catalog());

        settings.Set(SettingKeys.Theme, BuiltInAppearance.LowContrastDarkTheme);

        Assert.True(service.IsDark);
        Assert.Equal(BuiltInAppearance.LowContrastDarkTheme, service.Current.Theme.Id);
        Assert.Equal(Color.Parse("#2A2C32"), Theme.DarkPalette.Surface);
        Assert.Equal(BuiltInColorSchemes.LowContrastDarkId, syntax.Current.Id);

        service.Toggle();

        Assert.False(service.IsDark);
        Assert.Equal(BuiltInAppearance.LowContrastLightTheme, settings.Get<string>(SettingKeys.Theme));
        Assert.Equal(Color.Parse("#EEEFF1"), Theme.LightPalette.Surface);
        Assert.Equal(BuiltInColorSchemes.LowContrastLightId, syntax.Current.Id);
        Assert.Empty(output.Lines);
    });

    [Fact]
    public Task AppliesTheAccentOverAnyThemeAndGoesBackToTheThemesOwn() => Run(() =>
    {
        var settings = TestSettings.Create(SettingsPath);
        var (service, _, _) = Create(settings, FakeAppearance.Catalog(Plugin()));
        settings.Set(SettingKeys.Theme, "ember.dusk");

        settings.Set(SettingKeys.Accent, "#22C55E");

        Assert.Equal(Color.Parse("#22C55E"), Theme.DarkPalette.Accent);
        Assert.Equal(Color.Parse("#22C55E"), Theme.LightPalette.Accent);

        settings.Set(SettingKeys.Accent, "");

        Assert.Equal(Color.Parse("#E4A23C"), Theme.DarkPalette.Accent);
        Assert.Equal(ThemePalette.Light.Accent, Theme.LightPalette.Accent);
        Assert.Equal("ember.dusk", service.Current.Theme.Id);
    });

    [Fact]
    public Task UsesTheChosenSchemeOverTheThemesAndFallsBackWhenItCannotBeRead() => Run(() =>
    {
        var broken = Path.Combine(folder, "broken.json");
        Directory.CreateDirectory(folder);
        File.WriteAllText(broken, "{ not json");
        var plugin = new FakeAppearance(
            [FakeAppearance.Dusk],
            [new ColorScheme("ember.dusk", "Ember Dusk", FakeAppearance.WriteScheme(folder)), new ColorScheme("ember.broken", "Broken", broken)]);
        var settings = TestSettings.Create(SettingsPath);
        var (service, syntax, output) = Create(settings, FakeAppearance.Catalog(plugin));

        settings.Set(SettingKeys.ColorScheme, "ember.dusk");
        Assert.Equal("ember.dusk", syntax.Current.Id);
        Assert.Equal("ember.dusk", service.Scheme.Id);

        settings.Set(SettingKeys.ColorScheme, "ember.broken");
        Assert.Equal(BuiltInColorSchemes.DarkId, syntax.Current.Id);
        Assert.Contains(output.Lines, l => l.Contains("Broken (ember.broken) cannot be used", StringComparison.Ordinal));

        settings.Set(SettingKeys.Theme, BuiltInAppearance.LightTheme);
        settings.Set(SettingKeys.ColorScheme, "");
        Assert.Equal(BuiltInColorSchemes.LightId, syntax.Current.Id);
    });

    private string SettingsPath => Path.Combine(folder, "settings.json");

    private static ToolkitTheme Theme => Application.Current!.Styles.OfType<ToolkitTheme>().First();

    private FakeAppearance Plugin() =>
        new([FakeAppearance.Dusk], [new ColorScheme("ember.dusk", "Ember Dusk", FakeAppearance.WriteScheme(folder))]);

    private (ThemeService Service, SyntaxColors Syntax, FakeOutput Output) Create(ISettingsService settings, AppearanceCatalog catalog)
    {
        var syntax = new SyntaxColors(new TextMateGrammars([]));
        var output = new FakeOutput();
        var service = new ThemeService(settings, new MessageBus(), catalog, syntax, new Lazy<Sdk.Shell.IOutputService>(() => output));
        created.Add(service);
        if (settings is IDisposable disposable)
            created.Add(disposable);
        return (service, syntax, output);
    }

    // The application's palettes and theme variant are shared by every test, so each test puts them back.
    private Task Run(Action test) => HeadlessSession.Value.Dispatch(() =>
    {
        var theme = Theme;
        var (dark, light, variant) = (theme.DarkPalette, theme.LightPalette, Application.Current!.RequestedThemeVariant);
        try
        {
            test();
        }
        finally
        {
            foreach (var disposable in created)
                disposable.Dispose();
            created.Clear();
            theme.DarkPalette = dark;
            theme.LightPalette = light;
            Application.Current.RequestedThemeVariant = variant;
        }
    }, CancellationToken.None);
}
