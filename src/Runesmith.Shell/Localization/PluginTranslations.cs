using Runesmith.Composition;
using Runesmith.Shell.Hub;
using RunesmithHub.Protocol.Index;

namespace Runesmith.Shell.Localization;

/// <summary>What the plugin manager shows about translations, for the plugins and language packs of its model.</summary>
internal sealed class PluginTranslations(ShellLocalization localization, PluginManagerModel model)
{
    /// <summary>Gets what to say about an installed plugin's translation into the language that shows, or null in English and for a
    /// language pack.</summary>
    public string? Coverage(InstalledRow row) =>
        row.LanguagePack is null ? TranslationLabels.Plugin(localization.Core, row.Id, row.IsRunning, row.Languages) : null;

    /// <summary>Gets what to say about a plugin on the hub, by the version given, or null in English and for a language pack.</summary>
    public string? Coverage(PluginRecord plugin, VersionRecord? version) =>
        version?.LanguagePack is null ? TranslationLabels.Plugin(localization.Core, plugin.Id, model.IsRunning(plugin.Id), version?.Languages) : null;

    /// <summary>Gets the language of a language pack, such as "Nederlands (Dutch)".</summary>
    public static string Language(PluginLanguagePack pack) => TranslationLabels.Language(pack.Name, pack.EnglishName);

    /// <inheritdoc cref="Language(PluginLanguagePack)"/>
    public static string Language(LanguagePackSummary pack) => TranslationLabels.Language(pack.Name, pack.EnglishName);

    /// <summary>Gets what a language pack translates: with how much of each when the pack is installed, and otherwise what the hub lists.</summary>
    public IReadOnlyList<TranslatedTarget> Targets(string packId, IEnumerable<string>? listed) =>
        localization.Core.LanguagePacks.Any(pack => pack.Id == packId)
            ? TranslationLabels.Targets(localization.Core, packId, model.NameOf)
            : TranslationLabels.Targets(listed ?? [], model.NameOf);

    /// <summary>Gets an installed language pack on one line, such as "Nederlands (Dutch): Runesmith 94%, Git 100%".</summary>
    public string Summary(InstalledRow row) =>
        row.LanguagePack is { } pack ? TranslationLabels.Summary(Language(pack), Targets(row.Id, null)) : "";
}
