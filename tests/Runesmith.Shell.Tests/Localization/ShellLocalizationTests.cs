using System.Globalization;
using Avalonia.Media;
using Runesmith.Localization;
using Runesmith.Sdk.Settings;
using Runesmith.Shell.Localization;
using Runesmith.Shell.Services;
using Runesmith.Shell.Tests.Appearance;

namespace Runesmith.Shell.Tests.Localization;

public sealed class ShellLocalizationTests
{
    private const string Category = "localization-category";

    private static readonly InstalledLanguagePack Dutch = Pack("runesmith.dutch", "nl", "Nederlands", "Dutch");
    private static readonly InstalledLanguagePack Arabic = Pack("tests.arabic", "ar", "العربية", "Arabic");

    private readonly MemorySettings settings = new()
    {
        Values =
        {
            [SettingKeys.Language] = LocalizationOptions.SystemLanguage,
            [SettingKeys.UntranslatedPlugins] = LocalizationSettings.Mix,
            [SettingKeys.MinimumCoverage] = 80,
            [SettingKeys.PseudoLocalization] = false,
        },
    };

    private readonly List<InstalledLanguagePack> installed = [];

    [Fact]
    public Task TheFirstStartFollowsTheSystemLanguageWhenAPackCoversIt() => HeadlessSession.Value.Dispatch(() =>
    {
        installed.Add(Dutch);
        using var localization = Create(CultureInfo.GetCultureInfo("nl-BE"));

        Assert.Equal("Taal", localization.GetLocalizer().Get(Category));
        Assert.Equal("nl-BE", localization.CurrentLanguage.Name);
        Assert.Equal("Nederlands", LanguagePicker.SystemLanguage(localization));
    }, CancellationToken.None);

    [Fact]
    public Task WithoutAPackForTheSystemLanguageTheTextIsEnglish() => HeadlessSession.Value.Dispatch(() =>
    {
        installed.Add(Dutch);
        using var localization = Create(CultureInfo.GetCultureInfo("fr-FR"));

        Assert.Equal("Language", localization.GetLocalizer().Get(Category));
        Assert.Equal("English", LanguagePicker.SystemLanguage(localization));
    }, CancellationToken.None);

    [Fact]
    public Task ChoosingALanguageSwitchesTheTextAtOnce() => HeadlessSession.Value.Dispatch(() =>
    {
        installed.Add(Dutch);
        using var localization = Create(CultureInfo.GetCultureInfo("en-US"));
        var changed = 0;
        localization.Changed += (_, _) => changed++;

        settings.Set(SettingKeys.Language, "nl");

        Assert.Equal("Taal", localization.GetLocalizer().Get(Category));
        Assert.Equal(1, changed);

        settings.Set(SettingKeys.Language, "en");

        Assert.Equal("Language", localization.GetLocalizer().Get(Category));
        Assert.Equal(2, changed);
    }, CancellationToken.None);

    [Fact]
    public Task ALanguagePackThatIsTurnedOnLaterShowsWithoutARestart() => HeadlessSession.Value.Dispatch(() =>
    {
        settings.Values[SettingKeys.Language] = "nl";
        using var localization = Create(CultureInfo.GetCultureInfo("en-US"));
        Assert.Equal("Language", localization.GetLocalizer().Get(Category));

        installed.Add(Dutch);
        settings.Set(ShellSettings.DisabledPlugins, "");

        Assert.Equal("Taal", localization.GetLocalizer().Get(Category));
        Assert.Contains(localization.AvailableLanguages, language => language.Tag == "nl");
    }, CancellationToken.None);

    [Fact]
    public Task ALanguageWrittenFromRightToLeftTurnsTheLayoutAround() => HeadlessSession.Value.Dispatch(() =>
    {
        installed.Add(Arabic);
        using var localization = Create(CultureInfo.GetCultureInfo("en-US"));
        Assert.Equal(FlowDirection.LeftToRight, localization.FlowDirection);

        settings.Set(SettingKeys.Language, "ar");

        Assert.Equal(FlowDirection.RightToLeft, localization.FlowDirection);
        Assert.Equal("اللغة", localization.GetLocalizer().Get(Category));
    }, CancellationToken.None);

    [Fact]
    public Task PseudoLocalizationMarksEveryTranslatableText() => HeadlessSession.Value.Dispatch(() =>
    {
        using var localization = Create(CultureInfo.GetCultureInfo("en-US"));

        settings.Set(SettingKeys.PseudoLocalization, true);

        var text = localization.GetLocalizer().Get(Category);
        Assert.NotEqual("Language", text);
        Assert.StartsWith("[", text, StringComparison.Ordinal);
    }, CancellationToken.None);

    private ShellLocalization Create(CultureInfo system) =>
        new(settings, new LocalizationEnvironment([typeof(ShellLocalization).Assembly], [], _ => [.. installed], system)
        {
            Read = path => path == Dutch.AssemblyPath ? Resource("nl", "Taal") : path == Arabic.AssemblyPath ? Resource("ar", "اللغة") : [],
        });

    private static InstalledLanguagePack Pack(string id, string language, string name, string englishName) =>
        new(new LanguagePack(id, language, name, englishName), "1.0.0", $"/packs/{id}/{id}.dll");

    private static IReadOnlyList<LocaleResource> Resource(string language, string category) =>
        [new LocaleResource($"locales/{language}/runesmith/runesmith-localization.ftl", $"{Category} = {category}")];
}
