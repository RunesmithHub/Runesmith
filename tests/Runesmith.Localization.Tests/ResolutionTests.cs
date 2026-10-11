using System.Globalization;
using Runesmith.Sdk.Localization;

namespace Runesmith.Localization.Tests;

public sealed class ResolutionTests
{
    private const string Shell = "locales/en/Runesmith.Shell.ftl";

    private readonly List<string> log = [];

    [Fact]
    public void AMessageComesFromTheMostSpecificLanguageOfTheChain()
    {
        var sources = Sources(
            ("runesmith", "locales/en/a.ftl", "both = Both\nonly-base = Only base\nonly-english = Only English"),
            ("runesmith", "locales/nl/a.ftl", "both = Beide\nonly-base = Alleen basis"),
            ("runesmith", "locales/nl-BE/a.ftl", "both = Allebei"));
        using var service = Service(sources, "nl-BE");
        var localizer = service.GetLocalizer();

        Assert.Equal("Allebei", localizer.Get("both"));
        Assert.Equal("Alleen basis", localizer.Get("only-base"));
        Assert.Equal("Only English", localizer.Get("only-english"));
        Assert.Equal(CultureInfo.GetCultureInfo("nl-BE"), service.CurrentLanguage);
    }

    [Fact]
    public void APluginsOwnTranslationComesBeforeALanguagePacksAndTheOfficialPackBeforeTheOthers()
    {
        var sources = Sources(
            ("acme.todo", "locales/en/acme.todo.ftl", "own = Own\npacked = Packed\nofficial = Official\nnewest = Newest"),
            ("acme.todo", "locales/nl/acme.todo.ftl", "own = Eigen"));
        Pack(sources, new LanguagePack("old.dutch", "nl", "Nederlands", "Dutch") { Updated = DateTimeOffset.UnixEpoch },
            ("locales/nl/acme.todo/x.ftl", "own = Pakket\npacked = Verpakt\nnewest = Oud"));
        Pack(sources, new LanguagePack("new.dutch", "nl", "Nederlands", "Dutch") { Updated = DateTimeOffset.UnixEpoch.AddDays(1) },
            ("locales/nl/acme.todo/x.ftl", "newest = Nieuwst"));
        Pack(sources, new LanguagePack("runesmith.dutch", "nl", "Nederlands", "Dutch") { IsOfficial = true, Updated = DateTimeOffset.UnixEpoch },
            ("locales/nl/acme.todo/x.ftl", "official = Officieel"));
        using var service = Service(sources, "nl");
        var localizer = service.GetLocalizer("acme.todo");

        Assert.Equal("Eigen", localizer.Get("own"));
        Assert.Equal("Verpakt", localizer.Get("packed"));
        Assert.Equal("Officieel", localizer.Get("official"));
        Assert.Equal("Nieuwst", localizer.Get("newest"));
    }

    [Fact]
    public void AMessageNoLanguageHasShowsAsAReadableIdAndIsLogged()
    {
        using var service = Service(Sources(("runesmith", Shell, "known = Known")), "en");

        Assert.Equal("Settings title", service.GetLocalizer().Get("settings-title"));
        Assert.Equal("Save button tooltip", service.GetLocalizer().Get("save-button.tooltip"));
        Assert.False(service.GetLocalizer().TryGet("settings-title", null, out var missing));
        Assert.Null(missing);
        Assert.Contains(log, line => line.Contains("settings-title", StringComparison.Ordinal));
        Assert.Single(log, line => line.Contains("settings-title", StringComparison.Ordinal));
    }

    [Fact]
    public void AttributesResolveLikeMessages()
    {
        var sources = Sources(
            ("runesmith", "locales/en/a.ftl", "save = Save\n    .tooltip = Save the file\n    .key = S"),
            ("runesmith", "locales/nl/a.ftl", "save = Bewaar\n    .tooltip = Bewaar het bestand"));
        using var service = Service(sources, "nl");
        var localizer = service.GetLocalizer();

        Assert.Equal("Bewaar het bestand", localizer.Get("save.tooltip"));
        Assert.Equal("S", localizer.Get("save.key"));
        Assert.True(localizer.TryGet("save.key", null, out var key));
        Assert.Equal("S", key);
    }

    [Fact]
    public void ATranslationThatFailsToFormatFallsBackToEnglish()
    {
        var sources = Sources(
            ("runesmith", "locales/en/a.ftl", "greet = Hello { $name }"),
            ("runesmith", "locales/nl/a.ftl", "greet = Hallo { $naam }"));
        using var service = Service(sources, "nl");

        Assert.Equal("Hello Ada", service.GetLocalizer().Get("greet", new LocalizationArgs { { "name", "Ada" } }));
        Assert.Contains(log, line => line.Contains("greet", StringComparison.Ordinal) && line.Contains("nl", StringComparison.Ordinal));
    }

    [Fact]
    public void WithoutItsArgumentAMessageStillShowsItsText()
    {
        using var service = Service(Sources(("runesmith", Shell, "greet = Hello { $name }")), "en");

        Assert.Equal("Hello {$name}", service.GetLocalizer().Get("greet"));
    }

    [Fact]
    public void NumbersAndDatesFollowTheCulture()
    {
        var sources = Sources(
            ("runesmith", "locales/en/a.ftl", "size = { $size } bytes\npercent = { NUMBER($share, style: \"percent\") }\nexact = { NUMBER($n, minimumFractionDigits: 2) }\nwhen = { DATETIME($date, dateStyle: \"long\") }"),
            ("runesmith", "locales/nl/a.ftl", "size = { $size } bytes"));
        using var dutch = Service(sources, "nl");
        using var english = Service(sources, "en");
        var date = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal("1.234,5 bytes", dutch.GetLocalizer().Get("size", new LocalizationArgs { { "size", 1234.5 } }));
        Assert.Equal("1,234.5 bytes", english.GetLocalizer().Get("size", new LocalizationArgs { { "size", 1234.5 } }));
        Assert.Equal("62%", dutch.GetLocalizer().Get("percent", new LocalizationArgs { { "share", 0.62 } }));
        Assert.Equal("3,00", dutch.GetLocalizer().Get("exact", new LocalizationArgs { { "n", 3 } }));
        Assert.Equal("1,234,567,890,123", english.GetLocalizer().Get("size", new LocalizationArgs { { "size", 1234567890123L } }).Replace(" bytes", "", StringComparison.Ordinal));
        Assert.Equal("10 oktober 2026", dutch.GetLocalizer().Get("when", new LocalizationArgs { { "date", date } }));
        Assert.Equal("October 10, 2026", english.GetLocalizer().Get("when", new LocalizationArgs { { "date", date } }));
    }

    [Theory]
    [InlineData(0, "Geen bestanden")]
    [InlineData(1, "Eén bestand")]
    [InlineData(2, "2 bestanden")]
    [InlineData(1500, "1.500 bestanden")]
    public void ANumberPicksThePluralFormOfTheLanguageAndExactKeysFirst(long count, string expected)
    {
        var sources = Sources(
            ("runesmith", "locales/en/a.ftl", "files = { $count ->\n    [one] One file\n   *[other] { $count } files\n}"),
            ("runesmith", "locales/nl/a.ftl", "files = { $count ->\n    [0] Geen bestanden\n    [one] Eén bestand\n   *[other] { $count } bestanden\n}"));
        using var service = Service(sources, "nl");

        Assert.Equal(expected, service.GetLocalizer().Get("files", new LocalizationArgs { { "count", count } }));
    }

    [Fact]
    public void IsolationMarksAreOffByDefaultAndCanBeTurnedOn()
    {
        var sources = Sources(("runesmith", Shell, "opened = Opened { $file } now"));
        using var service = Service(sources, "en");
        var args = new LocalizationArgs { { "file", "a.txt" } };

        Assert.Equal("Opened a.txt now", service.GetLocalizer().Get("opened", args));
        service.Apply(service.Options with { IsolateArguments = true });
        Assert.Equal("Opened ⁨a.txt⁩ now", service.GetLocalizer().Get("opened", args));
    }

    [Fact]
    public void ALanguageThatDoesNotParseIsLeftOutAndLogged()
    {
        var sources = new TranslationSources();
        sources.AddOwn("runesmith", [new("locales/en/a.ftl", "a = A"), new("locales/not_a_tag/a.ftl", "a = B"), new("strings/a.ftl", "a = C")]);
        sources.AddPack(new LanguagePack("acme.dutch", "nl", "Nederlands", "Dutch"), [new("locales/de/runesmith/a.ftl", "a = D"), new("locales/nl/a.ftl", "a = E")]);

        using var service = new LocalizationService(sources, log: log.Add);

        Assert.Equal(4, log.Count);
        Assert.Contains(log, line => line.Contains("not_a_tag", StringComparison.Ordinal));
        Assert.Contains(log, line => line.Contains("locales/de/runesmith/a.ftl", StringComparison.Ordinal));
    }

    [Fact]
    public void ABrokenFileIsLoggedAndItsGoodMessagesStillShow()
    {
        var sources = Sources(
            ("runesmith", "locales/en/a.ftl", "good = Good\nalso = Also"),
            ("runesmith", "locales/nl/a.ftl", "good = Goed\nbroken = { $x\nalso = Ook"));
        using var service = Service(sources, "nl");

        Assert.Equal("Goed", service.GetLocalizer().Get("good"));
        Assert.Equal("Ook", service.GetLocalizer().Get("also"));
        Assert.Contains(log, line => line.Contains("locales/nl/a.ftl", StringComparison.Ordinal));
    }

    private static TranslationSources Sources(params (string Target, string Name, string Text)[] files)
    {
        var sources = new TranslationSources();
        foreach (var group in files.GroupBy(f => f.Target))
            sources.AddOwn(group.Key, group.Select(f => new LocaleResource(f.Name, f.Text)));
        return sources;
    }

    private static void Pack(TranslationSources sources, LanguagePack pack, params (string Name, string Text)[] files) =>
        sources.AddPack(pack, files.Select(f => new LocaleResource(f.Name, f.Text)));

    private LocalizationService Service(TranslationSources sources, string language) =>
        new(sources, new LocalizationOptions { Language = language, SystemCulture = CultureInfo.GetCultureInfo("en-US") }, log: log.Add);
}
