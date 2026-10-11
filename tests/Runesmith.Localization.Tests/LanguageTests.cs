using System.Globalization;
using Runesmith.Sdk.Localization;

namespace Runesmith.Localization.Tests;

public sealed class LanguageTests
{
    private const string English = "a = A\nb = B\nc = C\nd = D\ne = E";

    [Fact]
    public void CoverageIsTheShareOfEnglishMessagesALanguageTranslates()
    {
        var sources = Sources();
        using var service = new LocalizationService(sources, Options("nl-BE"));

        Assert.Equal(0.6, service.Coverage("acme.todo"));
        Assert.Equal(0.4, service.Coverage("acme.todo", "nl"));
        Assert.Equal(0.2, service.Coverage("acme.todo", "de"));
        Assert.Equal(0.4, service.Coverage("runesmith", "nl"));
        Assert.Equal(1, service.Coverage("acme.todo", "en"));
        Assert.Null(service.Coverage("acme.unlocalized"));
    }

    [Fact]
    public void WithEnglishForUntranslatedPluginsAPluginBelowTheMinimumShowsEntirelyInEnglish()
    {
        using var service = new LocalizationService(Sources(), Options("nl") with { UntranslatedPlugins = UntranslatedPlugins.English, MinimumCoverage = 50 });

        Assert.Equal("A", service.GetLocalizer("acme.todo").Get("a"));
        Assert.Equal("Een", service.GetLocalizer("runesmith").Get("a"));
        service.Apply(service.Options with { MinimumCoverage = 40 });
        Assert.Equal("Een", service.GetLocalizer("acme.todo").Get("a"));
        service.Apply(service.Options with { UntranslatedPlugins = UntranslatedPlugins.Mix, MinimumCoverage = 100 });
        Assert.Equal("Een", service.GetLocalizer("acme.todo").Get("a"));
        Assert.Equal("C", service.GetLocalizer("acme.todo").Get("c"));
    }

    [Fact]
    public void TheSystemLanguageShowsWhenAnInstalledPackCoversIt()
    {
        using var dutch = new LocalizationService(Sources(), Options("system", "nl-BE"));
        using var french = new LocalizationService(Sources(), Options("system", "fr-FR"));
        using var british = new LocalizationService(Sources(), Options("system", "en-GB"));

        Assert.Equal("nl-BE", dutch.CurrentLanguage.Name);
        Assert.Equal("Een", dutch.GetLocalizer().Get("a"));
        Assert.Equal("en", french.CurrentLanguage.Name);
        Assert.Equal("fr-FR", french.FormatCulture.Name);
        Assert.True(french.IsEnglish);
        Assert.Equal("A", french.GetLocalizer().Get("a"));
        Assert.Equal("en-GB", british.CurrentLanguage.Name);
    }

    [Fact]
    public void TheLanguagesAreEnglishAndTheLanguagePacksWithTheirNamesAndCoverageOfRunesmith()
    {
        using var service = new LocalizationService(Sources(), Options("en"));

        Assert.Equal(
            [
                new LanguageInfo("en", "English", "English", 1), new LanguageInfo("de", "Deutsch", "German", 0), new LanguageInfo("nl", "Nederlands", "Dutch", 0.4),
                new LanguageInfo("nl-BE", "Nederlands (België)", "Dutch (Belgium)", 0.4),
            ],
            service.AvailableLanguages);
    }

    [Fact]
    public void PseudoLocalizationAccentsLengthensAndBracketsEveryMessageButNotItsValues()
    {
        var sources = new TranslationSources();
        sources.AddOwn("runesmith", [new LocaleResource("locales/en/a.ftl", "opened = Opened { $file } in the editor")]);
        using var service = new LocalizationService(sources, Options("en") with { IsPseudo = true });

        var text = service.GetLocalizer().Get("opened", new LocalizationArgs { { "file", "notes.txt" } });
        var plain = "Opened notes.txt in the editor";

        Assert.StartsWith("[", text, StringComparison.Ordinal);
        Assert.EndsWith("]", text, StringComparison.Ordinal);
        Assert.Contains(" notes.txt ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Opened", text, StringComparison.Ordinal);
        Assert.InRange(text.Length, plain.Length * 1.25, plain.Length * 1.6);
        Assert.Equal("[Šééţţîîñĝš]", service.GetLocalizer().Get("settings"));
    }

    [Fact]
    public void ChangingTheLanguageRaisesChangedAndTheNextLookupUsesTheNewLanguage()
    {
        using var service = new LocalizationService(Sources(), Options("en"));
        var localizer = service.GetLocalizer("acme.todo");
        var changes = 0;
        service.Changed += (_, _) => changes++;

        Assert.Equal("A", localizer.Get("a"));
        service.Apply(service.Options with { Language = "nl" });
        Assert.Equal("Een", localizer.Get("a"));
        service.Apply(service.Options with { Language = "nl" });
        service.Apply(service.Options with { Language = "de" });
        Assert.Equal("Eins", localizer.Get("a"));
        Assert.Equal(2, changes);
    }

    [Fact]
    public void LookupsFromManyThreadsWhileTheLanguageChangesGiveAnAnswerOfOneLanguage()
    {
        using var service = new LocalizationService(Sources(), Options("en"));
        var localizer = service.GetLocalizer("acme.todo");
        var valid = new HashSet<string> { "A", "Een", "Eins" };
        string[] languages = ["en", "nl", "de"];

        var readers = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 2000; i++)
                Assert.Contains(localizer.Get("a", i % 3 == 0 ? new LocalizationArgs { { "n", i } } : null), valid);
        }, TestContext.Current.CancellationToken)).ToList();
        for (var i = 0; i < 300; i++)
            service.Apply(service.Options with { Language = languages[i % 3] });

        Assert.All(readers, reader => reader.Wait(TestContext.Current.CancellationToken));
    }

    private static LocalizationOptions Options(string language, string system = "en-US") =>
        new() { Language = language, SystemCulture = CultureInfo.GetCultureInfo(system) };

    private static TranslationSources Sources()
    {
        var sources = new TranslationSources();
        sources.AddOwn("runesmith", [new LocaleResource("locales/en/shell.ftl", English)]);
        sources.AddOwn("acme.todo", [new LocaleResource("locales/en/acme.todo.ftl", English), new LocaleResource("locales/nl/acme.todo.ftl", "a = Een\nb = Bee")]);
        sources.AddPack(new LanguagePack("acme.dutch", "nl", "Nederlands", "Dutch") { IsOfficial = true },
        [
            new LocaleResource("locales/nl/runesmith/shell.ftl", "a = Een\nb = Bee"),
            new LocaleResource("locales/nl-BE/acme.todo/x.ftl", "c = Cee"),
        ]);
        sources.AddPack(new LanguagePack("acme.belgian", "nl-BE", "Nederlands (België)", "Dutch (Belgium)"), [new LocaleResource("locales/nl-BE/acme.todo/x.ftl", "c = Cee")]);
        sources.AddPack(new LanguagePack("acme.german", "de", "Deutsch", "German"), [new LocaleResource("locales/de/acme.todo/x.ftl", "a = Eins")]);
        return sources;
    }
}
