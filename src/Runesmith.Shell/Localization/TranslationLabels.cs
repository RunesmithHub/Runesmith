using Runesmith.Localization;

namespace Runesmith.Shell.Localization;

/// <summary>One thing a language pack translates, as the plugin manager lists it.</summary>
/// <param name="Name">Runesmith, or the plugin's name.</param>
/// <param name="Detail">How much of it: a percentage when its English text is here to compare with, a number of messages when only that is
/// known, and nothing for a pack that is not installed.</param>
internal sealed record TranslatedTarget(string Name, string? Detail);

/// <summary>What the plugin manager says about translations: how much of a plugin the language that shows translates, and what a language
/// pack translates.</summary>
internal static class TranslationLabels
{
    /// <summary>Gets a share as the whole percentage shown for it: 100 percent only when nothing is missing, and more than nothing as at
    /// least 1 percent.</summary>
    public static double Share(double coverage) => coverage >= 1 ? 1 : coverage <= 0 ? 0 : Math.Clamp(Math.Floor(coverage * 100), 1, 99) / 100;

    /// <summary>Gets what to say about a plugin's translation into the language that shows, such as "Nederlands, 62%" or "English only";
    /// null while Runesmith shows English.</summary>
    /// <param name="isRunning">Whether the plugin runs, so its own messages are here to count.</param>
    /// <param name="languages">The languages the plugin says it carries, in its manifest or on the hub, or null.</param>
    public static string? Plugin(LocalizationService localization, string pluginId, bool isRunning, IReadOnlyList<string>? languages)
    {
        if (localization.IsEnglish)
            return null;

        var language = localization.Names(localization.DisplayLanguage).Name;
        if (isRunning)
        {
            return localization.Coverage(pluginId) is > 0 and var coverage
                ? Strings.LocalizationCoverage(language, Share(coverage))
                : Strings.LocalizationCoverageEnglishOnly;
        }

        var chain = LocalizationService.Chain(localization.DisplayLanguage).Where(tag => tag != "en").ToList();
        var own = languages?.Any(tag => chain.Contains(tag, StringComparer.OrdinalIgnoreCase)) == true;
        var packed = localization.LanguagePacks.Any(pack => chain.Contains(pack.Language) && localization.Targets(pack.Id).Any(target => target.Target == pluginId));
        return own || packed ? Strings.LocalizationCoverageAvailable(language) : Strings.LocalizationCoverageEnglishOnly;
    }

    /// <summary>Gets a language's name as a language pack shows it, such as "Nederlands (Dutch)".</summary>
    public static string Language(string name, string englishName) =>
        string.IsNullOrWhiteSpace(englishName) || string.Equals(name, englishName, StringComparison.OrdinalIgnoreCase) ? name : Strings.LocalizationPackLanguage(name, englishName);

    /// <summary>Gets what an installed language pack translates, Runesmith first, with how much of each.</summary>
    /// <param name="nameOf">Gets a plugin's name from its id.</param>
    public static IReadOnlyList<TranslatedTarget> Targets(LocalizationService localization, string packId, Func<string, string> nameOf) =>
    [
        .. localization.Targets(packId).Select(target => new TranslatedTarget(
            Name(target.Target, nameOf),
            target.Coverage is { } coverage ? Strings.LocalizationPackShare(Share(coverage)) : Strings.LocalizationPackMessages(target.Messages))),
    ];

    /// <summary>Gets what a language pack on the hub says it translates, without how much.</summary>
    public static IReadOnlyList<TranslatedTarget> Targets(IEnumerable<string> targets, Func<string, string> nameOf) =>
        [.. targets.Select(target => new TranslatedTarget(Name(target, nameOf), null))];

    /// <summary>Gets a language pack on one line, such as "Nederlands (Dutch): Runesmith 94%, Git 100%".</summary>
    public static string Summary(string language, IReadOnlyList<TranslatedTarget> targets) =>
        targets.Count == 0
            ? language
            : Strings.LocalizationPackSummary(language, string.Join(Strings.LocalizationPackSeparator, targets.Select(target =>
                target.Detail is { } detail ? Strings.LocalizationPackTarget(target.Name, detail) : target.Name)));

    private static string Name(string target, Func<string, string> nameOf) =>
        target == LocalizationService.RunesmithTarget ? Strings.LocalizationPackRunesmith : nameOf(target);
}
