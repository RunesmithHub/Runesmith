using System.Globalization;
using Runesmith.Localization;
using Runesmith.Sdk.Localization;
using Runesmith.Shell.Localization;

namespace Runesmith.Shell.Tests.Localization;

public sealed class TranslationLabelsTests
{
    private static readonly LanguagePack Dutch = new("runesmith.dutch", "nl", "Nederlands", "Dutch") { IsOfficial = true };

    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(0.999, 0.99)]
    [InlineData(0.625, 0.62)]
    [InlineData(0.001, 0.01)]
    [InlineData(0.0, 0.0)]
    public void ASharePercentageIsWholeAndNeverRoundsUpToAllOrDownToNothing(double coverage, double shown) =>
        Assert.Equal(shown, TranslationLabels.Share(coverage), 6);

    [Fact]
    public void InEnglishNothingIsSaidAboutTranslations()
    {
        using var service = Service("en");

        Assert.Null(TranslationLabels.Plugin(service, "acme.todo", isRunning: true, ["en", "nl"]));
    }

    [Fact]
    public void ARunningPluginShowsHowMuchOfItTheLanguageTranslates()
    {
        using var service = Service("nl");

        Assert.Equal("Nederlands, 50%", TranslationLabels.Plugin(service, "acme.todo", isRunning: true, null));
        Assert.Equal("English only", TranslationLabels.Plugin(service, "acme.notes", isRunning: true, null));
    }

    [Fact]
    public void APluginThatDoesNotRunShowsWhetherATranslationIsThere()
    {
        using var service = Service("nl-BE");

        Assert.Equal("Nederlands", TranslationLabels.Plugin(service, "acme.later", isRunning: false, ["en", "nl"]));
        Assert.Equal("Nederlands", TranslationLabels.Plugin(service, "acme.packed", isRunning: false, null));
        Assert.Equal("English only", TranslationLabels.Plugin(service, "acme.other", isRunning: false, ["en", "de"]));
    }

    [Fact]
    public void ALanguagePackListsWhatItTranslatesRunesmithFirst()
    {
        using var service = Service("nl");

        var targets = TranslationLabels.Targets(service, "runesmith.dutch", id => id == "acme.todo" ? "Todo" : id);

        Assert.Equal([new TranslatedTarget("Runesmith", "100%"), new TranslatedTarget("acme.packed", "one message"), new TranslatedTarget("Todo", "50%")], targets);
        Assert.Equal("Nederlands (Dutch): Runesmith 100%, acme.packed one message, Todo 50%",
            TranslationLabels.Summary(TranslationLabels.Language("Nederlands", "Dutch"), targets));
        Assert.Equal("English", TranslationLabels.Language("English", "English"));
    }

    [Fact]
    public void ThePickerNamesALanguageWithHowMuchOfRunesmithItTranslates()
    {
        Assert.Equal("Nederlands (Dutch), 94%", LanguagePicker.Title(new LanguageInfo("nl", "Nederlands", "Dutch", 0.948)));
        Assert.Equal("Esperanto, 10%", LanguagePicker.Title(new LanguageInfo("eo", "Esperanto", "Esperanto", 0.1)));
        Assert.Equal("English", LanguagePicker.Title(new LanguageInfo("en", "English", "English", 1)));
    }

    private static LocalizationService Service(string language)
    {
        var sources = new TranslationSources();
        sources.AddOwn("runesmith", [new LocaleResource("locales/en/runesmith-shell.ftl", "save = Save")]);
        sources.AddOwn("acme.todo", [new LocaleResource("locales/en/acme.todo.ftl", "add = Add\nremove = Remove")]);
        sources.AddOwn("acme.notes", [new LocaleResource("locales/en/acme.notes.ftl", "note = Note")]);
        sources.AddPack(Dutch,
        [
            new LocaleResource("locales/nl/runesmith/runesmith-shell.ftl", "save = Opslaan"),
            new LocaleResource("locales/nl/acme.todo/acme.todo.ftl", "add = Toevoegen"),
            new LocaleResource("locales/nl/acme.packed/acme.packed.ftl", "pack = Pakket"),
        ]);
        return new LocalizationService(sources, new LocalizationOptions { Language = language, SystemCulture = CultureInfo.GetCultureInfo("en-US") });
    }
}
