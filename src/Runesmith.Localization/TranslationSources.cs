using Runesmith.Sdk.Localization;

namespace Runesmith.Localization;

/// <summary>A Fluent file embedded in an assembly: its resource name, such as <c>locales/nl/acme.todo.ftl</c>, and its text.</summary>
public sealed record LocaleResource(string Name, string Text);

/// <summary>An installed language pack.</summary>
/// <param name="Id">The pack's plugin id.</param>
/// <param name="Language">The one language it carries, as plugin.json's <c>languagePack.language</c> says.</param>
/// <param name="Name">The language's name in itself, such as Nederlands.</param>
/// <param name="EnglishName">The language's name in English, such as Dutch.</param>
public sealed record LanguagePack(string Id, string Language, string Name, string EnglishName)
{
    /// <summary>Gets whether the pack is official; an official pack wins over the others of its language.</summary>
    public bool IsOfficial { get; init; }

    /// <summary>Gets when the pack was installed or updated; among packs that are not official, the newest wins.</summary>
    public DateTimeOffset Updated { get; init; }
}

/// <summary>What a language pack translates of one target.</summary>
/// <param name="Target"><c>runesmith</c>, or a plugin's id.</param>
/// <param name="Messages">How many messages of the target the pack has.</param>
/// <param name="Coverage">The share of the target's English messages the pack translates, from 0 to 1, or null when the target's English
/// source is not here, such as a plugin that is not installed.</param>
public sealed record LanguagePackTarget(string Target, int Messages, double? Coverage);

/// <summary>Everything Runesmith shows text from: the English source and translations of each target, Runesmith or a plugin, and the language
/// packs.</summary>
/// <remarks>A target's own resources are named <c>locales/&lt;language&gt;/&lt;file&gt;.ftl</c>; a language pack's are named
/// <c>locales/&lt;language&gt;/&lt;target&gt;/&lt;file&gt;.ftl</c>. Resources that are named otherwise are left out, and logged.</remarks>
public sealed class TranslationSources
{
    private readonly Dictionary<string, Dictionary<string, List<LocaleResource>>> own = new(StringComparer.Ordinal);
    private readonly List<(LanguagePack Pack, Dictionary<string, List<LocaleResource>> Targets)> packs = [];
    private readonly List<string> problems = [];

    /// <summary>Gets what was left out, for the log.</summary>
    public IReadOnlyList<string> Problems => problems;

    /// <summary>Adds a target's own English source and translations.</summary>
    /// <param name="target"><c>runesmith</c>, or the plugin's id.</param>
    public void AddOwn(string target, IEnumerable<LocaleResource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        foreach (var resource in resources)
        {
            var parts = resource.Name.Split('/');
            if (parts is not ["locales", var folder, var file] || !file.EndsWith(".ftl", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"{target}: {resource.Name} is not named locales/<language>/<file>.ftl, so it is left out.");
                continue;
            }

            if (LanguageTag.Canonicalize(folder) is not { } language)
            {
                problems.Add($"{target}: {folder} in {resource.Name} is not a language tag, so it is left out.");
                continue;
            }

            Add(own, target, language, resource);
        }
    }

    /// <summary>Adds a language pack and its translations.</summary>
    public void AddPack(LanguagePack pack, IEnumerable<LocaleResource> resources)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(resources);
        if (LanguageTag.Canonicalize(pack.Language) is not { } language)
        {
            problems.Add($"{pack.Id}: {pack.Language} is not a language tag, so the language pack is left out.");
            return;
        }

        var targets = new Dictionary<string, List<LocaleResource>>(StringComparer.Ordinal);
        foreach (var resource in resources)
        {
            var parts = resource.Name.Split('/');
            if (parts is not ["locales", var folder, var target, var file] || !file.EndsWith(".ftl", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"{pack.Id}: {resource.Name} is not named locales/<language>/<target>/<file>.ftl, so it is left out.");
                continue;
            }

            if (!LanguageTag.AreSame(LanguageTag.Canonicalize(folder), language))
            {
                problems.Add($"{pack.Id}: {resource.Name} is not in {language}, the one language of the pack, so it is left out.");
                continue;
            }

            if (!targets.TryGetValue(target, out var files))
                targets[target] = files = [];
            files.Add(resource);
        }

        packs.Add((pack with { Language = language }, targets));
    }

    /// <summary>Gets the targets that have resources of their own.</summary>
    internal IEnumerable<string> Targets => own.Keys;

    /// <summary>Gets a target's own resources by language.</summary>
    internal IReadOnlyDictionary<string, List<LocaleResource>> Own(string target) =>
        own.TryGetValue(target, out var languages) ? languages : new Dictionary<string, List<LocaleResource>>();

    /// <summary>Gets the language packs of a language, the one that wins first: official, then the most recently updated.</summary>
    internal IEnumerable<(LanguagePack Pack, Dictionary<string, List<LocaleResource>> Targets)> Packs(string language) =>
        packs.Where(p => p.Pack.Language == language).OrderByDescending(p => p.Pack.IsOfficial).ThenByDescending(p => p.Pack.Updated).ThenBy(p => p.Pack.Id, StringComparer.Ordinal);

    /// <summary>Gets every language pack.</summary>
    internal IEnumerable<LanguagePack> AllPacks => packs.Select(p => p.Pack);

    /// <summary>Gets every resource, of targets and of language packs.</summary>
    internal IEnumerable<LocaleResource> All =>
        own.Values.SelectMany(languages => languages.Values).Concat(packs.SelectMany(p => p.Targets.Values)).SelectMany(files => files);

    private static void Add(Dictionary<string, Dictionary<string, List<LocaleResource>>> into, string target, string language, LocaleResource resource)
    {
        if (!into.TryGetValue(target, out var languages))
            into[target] = languages = new Dictionary<string, List<LocaleResource>>(StringComparer.Ordinal);
        if (!languages.TryGetValue(language, out var files))
            languages[language] = files = [];
        files.Add(resource);
    }
}
