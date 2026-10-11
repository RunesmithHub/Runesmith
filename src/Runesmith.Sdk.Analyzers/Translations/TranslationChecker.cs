using Runesmith.Sdk.Analyzers.Fluent;
using Runesmith.Sdk.Localization;

namespace Runesmith.Sdk.Analyzers.Translations;

/// <summary>How serious a problem is.</summary>
internal enum ProblemSeverity
{
    Error,
    Warning,
    Message,
}

/// <summary>Something the checks found: what, how serious, and where.</summary>
internal sealed class TranslationProblem
{
    public TranslationProblem(ProblemSeverity severity, string code, string message, string path, int line = 0, int column = 0, int endLine = 0, int endColumn = 0)
    {
        Severity = severity;
        Code = code;
        Message = message;
        Path = path;
        Line = line;
        Column = column;
        EndLine = endLine;
        EndColumn = endColumn;
    }

    public ProblemSeverity Severity { get; }

    public string Code { get; }

    public string Message { get; }

    public string Path { get; }

    /// <summary>Gets the line, counted from 1, or 0 for the whole file.</summary>
    public int Line { get; }

    public int Column { get; }

    public int EndLine { get; }

    public int EndColumn { get; }

    public override string ToString() => $"{Path}({Line},{Column}): {Severity.ToString().ToLowerInvariant()} {Code}: {Message}";
}

/// <summary>A Fluent file of the project, with its path relative to the <c>locales</c> folder split into folders and file name.</summary>
internal sealed class LocaleFile
{
    public LocaleFile(string path, IReadOnlyList<string> relative, string text)
    {
        Path = path;
        Relative = relative;
        Text = text;
    }

    public string Path { get; }

    /// <summary>Gets the parts of the path under <c>locales</c>, such as <c>nl</c> and <c>acme.todo.ftl</c>.</summary>
    public IReadOnlyList<string> Relative { get; }

    public string Text { get; }
}

/// <summary>An English Fluent file of another project, to check a language pack's translations of it against.</summary>
internal sealed class EnglishSource
{
    public EnglishSource(string target, string path, string text)
    {
        Target = target;
        Path = path;
        Text = text;
    }

    /// <summary>Gets what the file belongs to: <c>runesmith</c> or a plugin id.</summary>
    public string Target { get; }

    public string Path { get; }

    public string Text { get; }
}

/// <summary>What the project's <c>plugin.json</c> says about languages, or nothing for a project that is not a plugin.</summary>
internal sealed class ManifestFacts
{
    public static readonly ManifestFacts None = new(null, null, null, null, null, false, false);

    public ManifestFacts(string? path, string? pluginId, IReadOnlyList<string>? languages, string? packLanguage, string? packName, bool hasCapabilities,
        bool hasDependencies)
    {
        Path = path;
        PluginId = pluginId;
        Languages = languages;
        PackLanguage = packLanguage;
        PackName = packName;
        HasCapabilities = hasCapabilities;
        HasDependencies = hasDependencies;
    }

    public string? Path { get; }

    public string? PluginId { get; }

    /// <summary>Gets <c>languages</c>, or null when the manifest has none.</summary>
    public IReadOnlyList<string>? Languages { get; }

    /// <summary>Gets <c>languagePack.language</c>, or null when the plugin is not a language pack.</summary>
    public string? PackLanguage { get; }

    public string? PackName { get; }

    public bool HasCapabilities { get; }

    public bool HasDependencies { get; }

    public bool IsLanguagePack => PackLanguage is not null || PackName is not null;
}

/// <summary>Checks a project's Fluent files: syntax everywhere, the English source on its own, each translation against its English source,
/// and the files against what <c>plugin.json</c> declares.</summary>
/// <remarks>Syntax errors and problems in the English source are errors; gaps in translations are warnings, since what is missing shows in
/// English.</remarks>
internal static class TranslationChecker
{
    public const string SyntaxError = "RSL1001";
    public const string Duplicate = "RSL1002";
    public const string Missing = "RSL1003";
    public const string Unused = "RSL1004";
    public const string Variables = "RSL1005";
    public const string UnknownReference = "RSL1006";
    public const string NoEnglish = "RSL1007";
    public const string BadLanguage = "RSL1008";
    public const string Misplaced = "RSL1009";
    public const string Undeclared = "RSL1010";
    public const string LanguagePack = "RSL1011";
    public const string NoSource = "RSL1012";

    private const int ListedIds = 8;

    public static IReadOnlyList<TranslationProblem> Check(IReadOnlyList<LocaleFile> files, IReadOnlyList<EnglishSource> sources, ManifestFacts manifest)
    {
        var problems = new List<TranslationProblem>();
        var groups = new Dictionary<(string Target, string Language), List<Parsed>>();
        foreach (var file in files)
        {
            var parsed = Place(file, manifest, problems);
            if (parsed is null)
                continue;

            if (!groups.TryGetValue((parsed.Target, parsed.Language), out var group))
                groups[(parsed.Target, parsed.Language)] = group = [];
            group.Add(parsed);
        }

        var english = new Dictionary<string, Bundle>(StringComparer.Ordinal);
        foreach (var source in sources.GroupBy(s => s.Target, StringComparer.Ordinal))
            english[source.Key] = new Bundle(source.Key, LanguageTag.English, [.. source.Select(s => new Parsed(s.Path, s.Target, LanguageTag.English, FluentParser.Parse(s.Text)))], isReference: true);

        foreach (var group in groups.Where(g => g.Key.Language == LanguageTag.English))
            english[group.Key.Target] = new Bundle(group.Key.Target, LanguageTag.English, group.Value, isReference: false);

        foreach (var group in groups.OrderBy(g => g.Key.Target, StringComparer.Ordinal).ThenBy(g => g.Key.Language, StringComparer.Ordinal))
        {
            var isEnglish = group.Key.Language == LanguageTag.English;
            var bundle = isEnglish ? english[group.Key.Target] : new Bundle(group.Key.Target, group.Key.Language, group.Value, isReference: false);
            foreach (var file in bundle.Files)
                ReportSyntax(file, problems);
            CheckBundle(bundle, isEnglish, problems);
            if (isEnglish)
                continue;

            if (english.TryGetValue(group.Key.Target, out var source))
                Compare(bundle, source, problems);
            else if (manifest.IsLanguagePack)
                problems.Add(new TranslationProblem(ProblemSeverity.Message, NoSource,
                    $"Only the syntax of the {group.Key.Language} translation of {group.Key.Target} is checked: add its English files as RunesmithEnglishSource items with TranslationTarget=\"{group.Key.Target}\" to check it against them.",
                    bundle.Files[0].Path));
            else
                problems.Add(new TranslationProblem(ProblemSeverity.Error, NoEnglish,
                    $"The project translates into {group.Key.Language} but has no English source; English (locales/en) is the source every translation follows.",
                    bundle.Files[0].Path));
        }

        CheckManifest(groups.Keys.Select(k => k.Language).Distinct(StringComparer.Ordinal).ToList(), files, manifest, problems);
        return problems;
    }

    private static Parsed? Place(LocaleFile file, ManifestFacts manifest, List<TranslationProblem> problems)
    {
        var parts = file.Relative;
        var expected = manifest.IsLanguagePack ? 3 : 2;
        if (parts.Count != expected)
        {
            var layout = manifest.IsLanguagePack ? "locales/<language>/<target>/<file>.ftl, where the target is runesmith or a plugin id" : "locales/<language>/<file>.ftl";
            problems.Add(new TranslationProblem(ProblemSeverity.Error, Misplaced, $"A Fluent file of this project belongs in {layout}.", file.Path));
            return null;
        }

        var language = LanguageTag.Canonicalize(parts[0]);
        if (language is null)
        {
            problems.Add(new TranslationProblem(ProblemSeverity.Error, BadLanguage,
                $"The folder {parts[0]} is not a BCP 47 language tag, such as en, nl or pt-BR.", file.Path));
            return null;
        }

        if (!string.Equals(language, parts[0], StringComparison.Ordinal))
            problems.Add(new TranslationProblem(ProblemSeverity.Warning, BadLanguage, $"Name the folder {parts[0]} {language}, the language tag's canonical case.", file.Path));

        var target = manifest.IsLanguagePack ? parts[1] : manifest.PluginId ?? "";
        if (manifest.IsLanguagePack)
        {
            if (manifest.PackLanguage is { } pack && !LanguageTag.AreSame(pack, language))
            {
                problems.Add(new TranslationProblem(ProblemSeverity.Error, LanguagePack,
                    $"The language pack carries {pack} only, as plugin.json's languagePack says, and this file is in {language}.", file.Path));
                return null;
            }

            if (target != "runesmith" && !IsPluginId(target))
            {
                problems.Add(new TranslationProblem(ProblemSeverity.Error, Misplaced,
                    $"{target} is neither runesmith nor a plugin id; a language pack's files sit in locales/{language}/<target>/.", file.Path));
                return null;
            }
        }

        return new Parsed(file.Path, target, language, FluentParser.Parse(file.Text));
    }

    private static void ReportSyntax(Parsed file, List<TranslationProblem> problems)
    {
        foreach (var error in file.Resource.Errors)
            problems.Add(At(file, error.Span, ProblemSeverity.Error, SyntaxError, error.Message));
    }

    private static void CheckBundle(Bundle bundle, bool isEnglish, List<TranslationProblem> problems)
    {
        var language = isEnglish ? "English" : bundle.Language;
        foreach (var (file, duplicate) in bundle.Duplicates)
        {
            var kind = duplicate is FluentTerm ? "term -" : "message ";
            problems.Add(At(file, duplicate.IdSpan, ProblemSeverity.Error, Duplicate,
                $"The {kind}{duplicate.Id} is defined again in the {language} files; only the first one counts."));
        }

        foreach (var (file, definition) in bundle.All)
        {
            foreach (var pattern in Patterns(definition))
            {
                foreach (var (id, attribute, span) in FluentAnalysis.References(pattern))
                {
                    if (bundle.Has(id, attribute))
                        continue;

                    var name = attribute is null ? id : id + "." + attribute;
                    problems.Add(At(file, span, isEnglish ? ProblemSeverity.Error : ProblemSeverity.Warning, UnknownReference,
                        $"{name} is not defined in the {language} files, so {Name(definition)} can't show."));
                }
            }
        }
    }

    private static void Compare(Bundle translation, Bundle english, List<TranslationProblem> problems)
    {
        var missing = new List<string>();
        foreach (var (_, message) in english.Messages)
        {
            if (!translation.TryGetMessage(message.Id, out var translated))
            {
                missing.Add(message.Id);
                continue;
            }

            if (message.Value is not null && translated.Message.Value is null)
                missing.Add(message.Id);
            foreach (var attribute in message.Attributes.Where(a => !translated.Message.Attributes.Any(t => t.Id == a.Id)))
                missing.Add(message.Id + "." + attribute.Id);
        }

        foreach (var (file, message) in translation.Messages)
        {
            if (!english.TryGetMessage(message.Id, out var source))
            {
                problems.Add(At(file, message.IdSpan, ProblemSeverity.Warning, Unused,
                    $"The message {message.Id} is not in the English source{Of(english)}, so nothing shows it; remove it or check its id."));
                continue;
            }

            if (message.Value is { } value && source.Message.Value is { } sourceValue)
                CompareVariables(file, message.Id, message.IdSpan, value, sourceValue, problems);
            foreach (var attribute in message.Attributes)
            {
                if (source.Message.Attributes.FirstOrDefault(a => a.Id == attribute.Id) is not { } sourceAttribute)
                {
                    problems.Add(At(file, attribute.IdSpan, ProblemSeverity.Warning, Unused,
                        $"The attribute {message.Id}.{attribute.Id} is not in the English source{Of(english)}, so nothing shows it."));
                    continue;
                }

                CompareVariables(file, message.Id + "." + attribute.Id, attribute.IdSpan, attribute.Value, sourceAttribute.Value, problems);
            }
        }

        if (missing.Count > 0)
        {
            var total = english.Messages.Count();
            var translated = english.Messages.Count(m => translation.TryGetMessage(m.Message.Id, out var t) && (m.Message.Value is null || t.Message.Value is not null));
            var listed = string.Join(", ", missing.Take(ListedIds)) + (missing.Count > ListedIds ? $" and {missing.Count - ListedIds} more" : "");
            problems.Add(new TranslationProblem(ProblemSeverity.Warning, Missing,
                $"The {translation.Language} translation{Of(english)} has {translated} of {total} messages; these show in English: {listed}.",
                translation.Files[0].Path));
        }
    }

    private static void CompareVariables(Parsed file, string name, FluentSpan span, FluentPattern translated, FluentPattern english, List<TranslationProblem> problems)
    {
        var wanted = FluentAnalysis.Variables(english).Select(v => v.Name).ToList();
        var used = FluentAnalysis.Variables(translated).Select(v => v.Name).ToList();
        foreach (var unknown in used.Except(wanted, StringComparer.Ordinal))
        {
            problems.Add(At(file, span, ProblemSeverity.Warning, Variables,
                $"{name} uses ${unknown}, which the English message does not have{(wanted.Count == 0 ? "" : "; it has " + string.Join(", ", wanted.Select(v => "$" + v)))}."));
        }

        foreach (var lost in wanted.Except(used, StringComparer.Ordinal))
            problems.Add(At(file, span, ProblemSeverity.Warning, Variables, $"{name} leaves out ${lost}, which the English message shows."));
    }

    private static void CheckManifest(List<string> languages, IReadOnlyList<LocaleFile> files, ManifestFacts manifest, List<TranslationProblem> problems)
    {
        if (manifest.Path is not { } path)
            return;

        if (manifest.IsLanguagePack)
        {
            if (manifest.PackLanguage is not { } pack || LanguageTag.Canonicalize(pack) is null)
                problems.Add(new TranslationProblem(ProblemSeverity.Error, LanguagePack, "languagePack.language in plugin.json must be a BCP 47 language tag, such as nl or pt-BR.", path));
            if (string.IsNullOrWhiteSpace(manifest.PackName))
                problems.Add(new TranslationProblem(ProblemSeverity.Error, LanguagePack, "languagePack.name in plugin.json must be the language's name in itself, such as Nederlands.", path));
            if (manifest.Languages is not null)
                problems.Add(new TranslationProblem(ProblemSeverity.Error, LanguagePack, "A language pack declares no languages in plugin.json; languagePack.language says what it carries.", path));
            if (manifest.HasCapabilities)
                problems.Add(new TranslationProblem(ProblemSeverity.Error, LanguagePack, "A language pack declares no capabilities in plugin.json.", path));
            if (manifest.HasDependencies)
                problems.Add(new TranslationProblem(ProblemSeverity.Error, LanguagePack, "A language pack declares no dependencies in plugin.json.", path));
            if (files.Count == 0)
                problems.Add(new TranslationProblem(ProblemSeverity.Warning, LanguagePack, "The language pack has no Fluent files in locales/<language>/<target>/ yet.", path));
            return;
        }

        if (manifest.Languages is not { } declared)
        {
            if (languages.Count > 0)
            {
                var list = string.Join(", ", languages.OrderBy(l => l == LanguageTag.English ? "" : l, StringComparer.Ordinal).Select(l => $"\"{l}\""));
                problems.Add(new TranslationProblem(ProblemSeverity.Warning, Undeclared,
                    $"Add \"languages\": [{list}] to plugin.json, so the plugin hub lists the languages the plugin speaks.", path));
            }

            return;
        }

        var canonical = new List<string>();
        foreach (var language in declared)
        {
            if (LanguageTag.Canonicalize(language) is { } tag)
                canonical.Add(tag);
            else
                problems.Add(new TranslationProblem(ProblemSeverity.Error, BadLanguage, $"{language} in languages in plugin.json is not a BCP 47 language tag, such as en, nl or pt-BR.", path));
        }

        if (!canonical.Contains(LanguageTag.English))
            problems.Add(new TranslationProblem(ProblemSeverity.Error, Undeclared, "languages in plugin.json lists English (en) too: it is the source of every translation.", path));
        foreach (var language in canonical.Where(l => !languages.Contains(l)))
            problems.Add(new TranslationProblem(ProblemSeverity.Error, Undeclared, $"plugin.json lists {language} in languages, and the project has no Fluent file in locales/{language}/.", path));
        foreach (var language in languages.Where(l => !canonical.Contains(l)))
            problems.Add(new TranslationProblem(ProblemSeverity.Warning, Undeclared, $"Add \"{language}\" to languages in plugin.json, so the plugin hub lists it.", path));
    }

    private static IEnumerable<FluentPattern> Patterns(FluentDefinition definition)
    {
        if (definition.Value is { } value)
            yield return value;
        foreach (var attribute in definition.Attributes)
            yield return attribute.Value;
    }

    private static string Name(FluentDefinition definition) => definition is FluentTerm ? "-" + definition.Id : definition.Id;

    private static string Of(Bundle english) => english.Target.Length > 0 ? $" of {english.Target}" : "";

    private static bool IsPluginId(string text)
    {
        var dot = text.IndexOf('.');
        return dot > 0 && IsPart(text.Substring(0, dot)) && IsPart(text.Substring(dot + 1));

        static bool IsPart(string part) =>
            part.Length is >= 2 and <= 40 && part[0] is >= 'a' and <= 'z' && part.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')
            && !part.Contains("--") && !part.EndsWith("-", StringComparison.Ordinal);
    }

    private static TranslationProblem At(Parsed file, FluentSpan span, ProblemSeverity severity, string code, string message)
    {
        var (line, column) = file.Resource.Position(span.Start);
        var (endLine, endColumn) = file.Resource.Position(span.End);
        return new TranslationProblem(severity, code, message, file.Path, line + 1, column + 1, endLine + 1, endColumn + 1);
    }

    private sealed class Parsed(string path, string target, string language, FluentResource resource)
    {
        public string Path { get; } = path;

        public string Target { get; } = target;

        public string Language { get; } = language;

        public FluentResource Resource { get; } = resource;
    }

    // One language of one target: what the runtime puts in one bundle.
    private sealed class Bundle
    {
        private readonly Dictionary<string, (Parsed File, FluentMessage Message)> messages = new(StringComparer.Ordinal);
        private readonly Dictionary<string, FluentTerm> terms = new(StringComparer.Ordinal);

        public Bundle(string target, string language, IReadOnlyList<Parsed> files, bool isReference)
        {
            Target = target;
            Language = language;
            Files = files;
            IsReference = isReference;
            foreach (var file in files.OrderBy(f => f.Path, StringComparer.Ordinal))
            {
                foreach (var definition in file.Resource.Entries.OfType<FluentDefinition>())
                {
                    All.Add((file, definition));
                    var added = definition switch
                    {
                        FluentMessage message when !messages.ContainsKey(message.Id) => Add(message, file),
                        FluentTerm term when !terms.ContainsKey(term.Id) => Add(term),
                        _ => false,
                    };
                    if (!added)
                        Duplicates.Add((file, definition));
                }
            }
        }

        public string Target { get; }

        public string Language { get; }

        public IReadOnlyList<Parsed> Files { get; }

        public bool IsReference { get; }

        public List<(Parsed File, FluentDefinition Definition)> All { get; } = [];

        public List<(Parsed File, FluentDefinition Definition)> Duplicates { get; } = [];

        public IEnumerable<(Parsed File, FluentMessage Message)> Messages => messages.Values;

        public bool TryGetMessage(string id, out (Parsed File, FluentMessage Message) found) => messages.TryGetValue(id, out found);

        public bool Has(string id, string? attribute)
        {
            FluentDefinition? definition = id.StartsWith("-", StringComparison.Ordinal)
                ? terms.TryGetValue(id.Substring(1), out var term) ? term : null
                : messages.TryGetValue(id, out var message) ? message.Message : null;
            return definition is not null && (attribute is null ? definition.Value is not null : definition.Attributes.Any(a => a.Id == attribute));
        }

        private bool Add(FluentMessage message, Parsed file)
        {
            messages[message.Id] = (file, message);
            return true;
        }

        private bool Add(FluentTerm term)
        {
            terms[term.Id] = term;
            return true;
        }
    }
}
