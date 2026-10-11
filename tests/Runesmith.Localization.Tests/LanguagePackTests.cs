using System.Globalization;
using Runesmith.Sdk.Localization;

namespace Runesmith.Localization.Tests;

public sealed class LanguagePackTests
{
    private static readonly LanguagePack Dutch = new("runesmith.dutch", "nl", "Nederlands", "Dutch") { IsOfficial = true };

    [Fact]
    public void ReloadingShowsTextFromAPackInstalledSinceAndRaisesChanged()
    {
        using var service = Service(English(), "nl");
        var changed = 0;
        service.Changed += (_, _) => changed++;
        Assert.Equal("Save", service.GetLocalizer().Get("save"));

        service.Reload(WithDutch());

        Assert.Equal("Opslaan", service.GetLocalizer().Get("save"));
        Assert.Equal(1, changed);
        Assert.Contains(service.AvailableLanguages, language => language.Tag == "nl" && language.Name == "Nederlands");
    }

    [Fact]
    public void ReloadingWithoutThePackGoesBackToEnglish()
    {
        using var service = Service(WithDutch(), "nl");
        Assert.Equal("Opslaan", service.GetLocalizer().Get("save"));

        service.Reload(English());

        Assert.Equal("Save", service.GetLocalizer().Get("save"));
        Assert.Empty(service.LanguagePacks);
        Assert.Equal(["en"], service.AvailableLanguages.Select(language => language.Tag));
    }

    [Fact]
    public void APacksTargetsComeRunesmithFirstWithTheShareOfEachThatIsHere()
    {
        var sources = English();
        sources.AddOwn("acme.todo", [new LocaleResource("locales/en/acme.todo.ftl", "add = Add\nremove = Remove")]);
        sources.AddPack(Dutch,
        [
            new LocaleResource("locales/nl/runesmith/runesmith-shell.ftl", "save = Opslaan"),
            new LocaleResource("locales/nl/acme.todo/acme.todo.ftl", "add = Toevoegen"),
            new LocaleResource("locales/nl/acme.other/acme.other.ftl", "one = Een\ntwo = Twee"),
        ]);
        using var service = Service(sources, "nl");

        var targets = service.Targets("runesmith.dutch");

        Assert.Equal(
        [
            new LanguagePackTarget("runesmith", 1, 0.5),
            new LanguagePackTarget("acme.other", 2, null),
            new LanguagePackTarget("acme.todo", 1, 0.5),
        ], targets);
        Assert.Empty(service.Targets("unknown.pack"));
    }

    [Fact]
    public void ARegionalLanguageIsNamedByThePackOfItsBaseLanguage()
    {
        using var service = Service(WithDutch(), "nl-BE");

        Assert.Equal(("Nederlands", "Dutch"), service.Names("nl-BE"));
        Assert.Equal("nl-BE", service.DisplayLanguage);
        Assert.Equal(["nl-BE", "nl"], LocalizationService.Chain("nl-be"));
        Assert.Empty(LocalizationService.Chain("not a tag"));
    }

    private static TranslationSources English()
    {
        var sources = new TranslationSources();
        sources.AddOwn("runesmith", [new LocaleResource("locales/en/runesmith-shell.ftl", "save = Save\nclose = Close")]);
        return sources;
    }

    private static TranslationSources WithDutch()
    {
        var sources = English();
        sources.AddPack(Dutch, [new LocaleResource("locales/nl/runesmith/runesmith-shell.ftl", "save = Opslaan")]);
        return sources;
    }

    private static LocalizationService Service(TranslationSources sources, string language) =>
        new(sources, new LocalizationOptions { Language = language, SystemCulture = CultureInfo.GetCultureInfo("en-US") });
}
