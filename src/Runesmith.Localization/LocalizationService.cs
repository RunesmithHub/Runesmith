using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using Linguini.Bundle;
using Linguini.Bundle.Builder;
using Linguini.Bundle.Errors;
using Linguini.Syntax.Ast;
using Linguini.Syntax.Parser;
using Runesmith.Sdk.Localization;

namespace Runesmith.Localization;

/// <summary>Resolves the messages of Runesmith and of each plugin in the user's language, and switches language live.</summary>
/// <remarks>
/// <para>A message is looked up in each language of the language chain, such as <c>nl-BE</c> then <c>nl</c>: for each language in the
/// target's own translations first, then in the language packs, the official pack first. When no language has it, the target's English
/// source does; when not even English has it, a readable form of its id shows and the log says so. A message never shows as its id.</para>
/// <para>The state of one language is immutable and replaced as a whole when the settings change, so every member can be called from any
/// thread. <see cref="Apply"/> and <see cref="Reload"/> are called from one thread. <see cref="Changed"/> is raised through the function given
/// for it, on the UI thread in Runesmith.</para>
/// </remarks>
public sealed class LocalizationService : ILocalizationService, ILocalizationHost, IDisposable
{
    /// <summary>The target of Runesmith's own messages.</summary>
    public const string RunesmithTarget = "runesmith";

    private const string AssemblyTargetPrefix = "assembly:";

    private volatile TranslationSources sources;
    private readonly Func<Assembly, string?> targetOf;
    private readonly Action<string> log;
    private readonly Action<Action> raise;
    private readonly ConcurrentDictionary<LocaleResource, Resource> parsed = new();
    private readonly ConcurrentDictionary<string, ILocalizer> localizers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Assembly, ILocalizer> assemblies = new();
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, List<LocaleResource>>> assemblyTargets = new(StringComparer.Ordinal);
    private volatile Snapshot snapshot;
    private bool isAttached;

    /// <param name="targetOf">Gets the target an assembly's messages belong to, <see cref="RunesmithTarget"/> or a plugin id; for an assembly
    /// it does not know, it returns null and the assembly's own resources are used.</param>
    /// <param name="log">Writes a line to the log, such as a message no language has.</param>
    /// <param name="raise">Runs the code that raises <see cref="Changed"/>; by default at once.</param>
    public LocalizationService(TranslationSources sources, LocalizationOptions? options = null, Func<Assembly, string?>? targetOf = null, Action<string>? log = null,
        Action<Action>? raise = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        this.sources = sources;
        this.targetOf = targetOf ?? (_ => null);
        this.log = log ?? (_ => { });
        this.raise = raise ?? (action => action());
        foreach (var problem in sources.Problems)
            this.log(problem);
        snapshot = new Snapshot(this, sources, options ?? new LocalizationOptions());
    }

    public event EventHandler? Changed;

    /// <summary>Gets the settings in effect.</summary>
    public LocalizationOptions Options => snapshot.Options;

    public CultureInfo CurrentLanguage => snapshot.Language;

    /// <summary>Gets the culture numbers and dates are written in: the chosen language's, or the system's when the language follows the
    /// system.</summary>
    public CultureInfo FormatCulture => snapshot.Values.Culture;

    /// <summary>Gets whether the text shows in English, because English was chosen or no language pack covers the language.</summary>
    public bool IsEnglish => snapshot.Chain is [LanguageTag.English];

    public IReadOnlyList<LanguageInfo> AvailableLanguages => snapshot.Languages.Value;

    /// <summary>Applies new settings; raises <see cref="Changed"/> when they differ from the ones in effect.</summary>
    public void Apply(LocalizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var previous = Interlocked.Exchange(ref snapshot, new Snapshot(this, sources, options));
        if (previous.Options != options)
            RaiseChanged();
    }

    /// <summary>Replaces what text is shown from, such as after a language pack was installed or removed, and raises <see cref="Changed"/>.</summary>
    public void Reload(TranslationSources replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        foreach (var problem in replacement.Problems)
            log(problem);
        sources = replacement;
        var kept = replacement.All.ToHashSet();
        foreach (var stale in parsed.Keys.Where(resource => !kept.Contains(resource)))
            parsed.TryRemove(stale, out _);
        snapshot = new Snapshot(this, replacement, snapshot.Options);
        RaiseChanged();
    }

    /// <summary>Gets the languages a message is looked up in for a language, most specific first: <c>nl-BE</c>, then <c>nl</c>.</summary>
    public static IReadOnlyList<string> Chain(string language) => LanguageTag.Canonicalize(language) is { } tag ? LanguageChain.For(tag) : [];

    /// <summary>Gets Runesmith's own localizer; Runesmith's service gives plugins their own.</summary>
    public ILocalizer GetLocalizer() => GetLocalizer(RunesmithTarget);

    /// <summary>Gets the localizer of a target: <see cref="RunesmithTarget"/> or a plugin id.</summary>
    public ILocalizer GetLocalizer(string target)
    {
        ArgumentException.ThrowIfNullOrEmpty(target);
        return localizers.GetOrAdd(target, name => new TargetLocalizer(this, name));
    }

    /// <summary>Gets the localizer of the target an assembly belongs to.</summary>
    public ILocalizer For(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return assemblies.GetOrAdd(assembly, loaded => GetLocalizer(targetOf(loaded) ?? AddAssembly(loaded)));
    }

    /// <summary>Gets the share of a target's English messages that the current language translates, from 0 to 1, or null when the target has
    /// no English source.</summary>
    public double? Coverage(string target) => snapshot.Target(target).Coverage;

    /// <summary>Gets the share of a target's English messages that a language translates, or null when the target has no English source.</summary>
    public double? Coverage(string target, string language) =>
        LanguageTag.Canonicalize(language) is { } tag ? snapshot.Coverage(target, LanguageChain.For(tag)) : null;

    /// <summary>Gets a language's name in itself and in English: that of the language pack that covers it, such as the Dutch pack for
    /// <c>nl-BE</c>, or else .NET's.</summary>
    public (string Name, string EnglishName) Names(string language)
    {
        var languages = AvailableLanguages;
        foreach (var tag in Chain(language))
        {
            if (languages.FirstOrDefault(l => LanguageTag.AreSame(l.Tag, tag)) is { } known)
                return (known.Name, known.EnglishName);
        }

        return Snapshot.CultureNames(language);
    }

    /// <summary>Gets the tag of the language the text shows in, such as <c>nl-BE</c>, or <c>en</c>.</summary>
    public string DisplayLanguage => snapshot.Display;

    /// <summary>Gets the installed language packs, by language and, within one, the pack that wins first.</summary>
    public IReadOnlyList<LanguagePack> LanguagePacks => snapshot.Packs;

    /// <summary>Gets what a language pack translates, Runesmith first: how many messages of each target, and the share of the target's
    /// English messages they are when the target is here to compare with.</summary>
    public IReadOnlyList<LanguagePackTarget> Targets(string packId) => snapshot.PackTargets(packId);

    /// <summary>Lets <see cref="Localized"/>, and with it the generated <c>Strings</c> classes and XAML, find messages through this
    /// service.</summary>
    public void Attach()
    {
        Localized.Attach(this);
        Volatile.Write(ref isAttached, true);
        raise(Localized.RaiseChanged);
    }

    public void Dispose()
    {
        if (Volatile.Read(ref isAttached))
            Localized.Attach(null);
        isAttached = false;
    }

    internal string Resolve(string target, string id, LocalizationArgs? args, out bool found)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        var current = snapshot;
        var cacheable = args is null || args.Count == 0;
        if (cacheable && current.Cache.TryGetValue((target, id), out var cached))
        {
            found = cached.Found;
            return cached.Text;
        }

        var state = current.Target(target);
        var values = current.Values.Convert(args);
        var isAttribute = id.Contains('.', StringComparison.Ordinal);
        string? text = null;
        string? partial = null;
        foreach (var (language, bundle) in state.Bundles)
        {
            if (!(isAttribute ? bundle.HasAttrMessage(id) : bundle.HasMessage(id)))
                continue;

            IList<FluentError>? errors;
            string? message;
            var resolved = isAttribute ? bundle.TryGetAttrMessage(id, values, out errors, out message) : bundle.TryGetMessage(id, values, out errors, out message);
            if (resolved && message is not null)
            {
                text = message;
                break;
            }

            partial ??= message;
            current.LogOnce($"{target}|{id}|{language}", $"{target}: the {language} message {id} could not be formatted: {string.Join("; ", errors ?? [])}");
        }

        found = text is not null || partial is not null;
        if (text is null && partial is null)
            current.LogOnce($"{target}|{id}", $"{target}: no language has the message {id}, so it shows as \"{Localized.Readable(id)}\".");

        text ??= partial ?? (current.Options.IsPseudo ? PseudoLocalization.Transform(Localized.Readable(id)) : Localized.Readable(id));
        if (current.Options.IsPseudo)
            text = PseudoLocalization.Wrap(text);
        if (cacheable)
            current.Cache[(target, id)] = (text, found);
        return text;
    }

    // Text bound through Localized is fresh before anyone who listens here builds from it.
    private void RaiseChanged() => raise(() =>
    {
        if (Volatile.Read(ref isAttached))
            Localized.RaiseChanged();
        Changed?.Invoke(this, EventArgs.Empty);
    });

    private string AddAssembly(Assembly assembly)
    {
        var target = AssemblyTargetPrefix + assembly.GetName().Name;
        assemblyTargets.GetOrAdd(target, _ =>
        {
            var own = new TranslationSources();
            own.AddOwn(target, LocaleResources.Read(assembly));
            foreach (var problem in own.Problems)
                log(problem);
            return own.Own(target);
        });
        return target;
    }

    private Resource Parse(LocaleResource resource) => parsed.GetOrAdd(resource, file =>
    {
        var result = LinguiniParser.FromFragment(file.Text, file.Name).Parse();
        foreach (var error in result.Errors)
            log($"{file.Name}: {error}");
        return result;
    });

    private static IEnumerable<string> MessageIds(IEnumerable<Resource> resources) =>
        resources.SelectMany(r => r.Entries.OfType<AstMessage>()).Select(m => m.Id.ToString());

    /// <summary>A localizer for one target.</summary>
    private sealed class TargetLocalizer(LocalizationService service, string target) : ILocalizer
    {
        public string Get(string id, LocalizationArgs? args = null) => service.Resolve(target, id, args, out _);

        public bool TryGet(string id, LocalizationArgs? args, [NotNullWhen(true)] out string? text)
        {
            var resolved = service.Resolve(target, id, args, out var found);
            text = found ? resolved : null;
            return found;
        }
    }

    /// <summary>What one set of settings resolves to: the language, the bundles of each target in the order they are searched, and the
    /// messages found so far.</summary>
    private sealed class Snapshot
    {
        private readonly LocalizationService service;
        private readonly TranslationSources sources;
        private readonly ConcurrentDictionary<string, Lazy<TargetState>> targets = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, byte> logged = new(StringComparer.Ordinal);
        private readonly IDictionary<string, Linguini.Bundle.Types.ExternalFunction> functions;

        public Snapshot(LocalizationService service, TranslationSources sources, LocalizationOptions options)
        {
            this.service = service;
            this.sources = sources;
            Options = options;
            var system = options.SystemCulture;
            if (string.Equals(options.Language, LocalizationOptions.SystemLanguage, StringComparison.OrdinalIgnoreCase)
                || LanguageTag.Canonicalize(options.Language) is not { } chosen
                || LanguageChain.Culture(chosen) is not { } culture)
            {
                var systemTag = LanguageTag.Canonicalize(system.Name);
                var covered = systemTag is not null && LanguageChain.For(systemTag).Any(l => l != LanguageTag.English && Speaks(l));
                Display = covered ? systemTag! : LanguageTag.English;
                Language = covered || (systemTag is not null && LanguageChain.IsEnglish(systemTag)) ? system : CultureInfo.GetCultureInfo(LanguageTag.English);
                Values = new FluentValues(system.Name.Length > 0 ? system : CultureInfo.GetCultureInfo(LanguageTag.English));
            }
            else
            {
                Display = chosen;
                Language = culture;
                Values = new FluentValues(culture);
            }

            Chain = LanguageChain.For(Display);
            functions = FluentFunctions.For(Values);
            Languages = new Lazy<IReadOnlyList<LanguageInfo>>(() => ListLanguages());
        }

        public LocalizationOptions Options { get; }

        public CultureInfo Language { get; }

        public string Display { get; }

        public IReadOnlyList<string> Chain { get; }

        public FluentValues Values { get; }

        public Lazy<IReadOnlyList<LanguageInfo>> Languages { get; }

        public IReadOnlyList<LanguagePack> Packs => field ??= [.. sources.AllPacks.Select(p => p.Language).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .SelectMany(language => sources.Packs(language).Select(p => p.Pack))];

        public ConcurrentDictionary<(string Target, string Id), (string Text, bool Found)> Cache { get; } = new();

        public static (string Name, string EnglishName) CultureNames(string tag)
        {
            if (LanguageChain.Culture(tag) is { } culture && !string.Equals(culture.EnglishName, tag, StringComparison.OrdinalIgnoreCase))
                return (culture.TextInfo.ToTitleCase(culture.NativeName), culture.EnglishName);
            return (tag, tag);
        }

        public TargetState Target(string target) => targets.GetOrAdd(target, name => new Lazy<TargetState>(() => Build(name))).Value;

        public double? Coverage(string target, IReadOnlyList<string> chain)
        {
            var english = MessageIds(Resources(target, LanguageTag.English).Select(service.Parse)).ToHashSet(StringComparer.Ordinal);
            if (english.Count == 0)
                return null;
            if (chain is [LanguageTag.English])
                return 1;

            var translated = chain.Where(l => l != LanguageTag.English).SelectMany(language =>
                Resources(target, language).Concat(sources.Packs(language).SelectMany(p => p.Targets.TryGetValue(target, out var files) ? files : [])))
                .Select(service.Parse);
            return (double)MessageIds(translated).Distinct(StringComparer.Ordinal).Count(english.Contains) / english.Count;
        }

        public IReadOnlyList<LanguagePackTarget> PackTargets(string packId)
        {
            var targets = sources.AllPacks.FirstOrDefault(p => p.Id == packId) is { } pack
                ? sources.Packs(pack.Language).First(p => p.Pack.Id == packId).Targets
                : [];
            return [.. targets.OrderBy(t => t.Key != RunesmithTarget).ThenBy(t => t.Key, StringComparer.Ordinal).Select(t =>
            {
                var ids = MessageIds(t.Value.Select(service.Parse)).ToHashSet(StringComparer.Ordinal);
                var english = MessageIds(Resources(t.Key, LanguageTag.English).Select(service.Parse)).ToHashSet(StringComparer.Ordinal);
                return new LanguagePackTarget(t.Key, ids.Count, english.Count == 0 ? null : (double)ids.Count(english.Contains) / english.Count);
            })];
        }

        public void LogOnce(string key, string line)
        {
            if (logged.TryAdd(key, 0))
                service.log(line);
        }

        private bool Speaks(string language) =>
            sources.Packs(language).Any() || sources.Own(RunesmithTarget).ContainsKey(language);

        private List<LocaleResource> Resources(string target, string language) =>
            Own(target).TryGetValue(language, out var files) ? files : [];

        private IReadOnlyDictionary<string, List<LocaleResource>> Own(string target) =>
            service.assemblyTargets.TryGetValue(target, out var own) ? own : sources.Own(target);

        private TargetState Build(string target)
        {
            var bundles = new List<(string Language, IReadBundle Bundle)>();
            foreach (var language in Chain.Where(l => l != LanguageTag.English))
            {
                if (Resources(target, language) is { Count: > 0 } own)
                    bundles.Add((language, Bundle(target, language, own)));
                foreach (var (pack, packTargets) in sources.Packs(language))
                {
                    if (packTargets.TryGetValue(target, out var files))
                        bundles.Add((language, Bundle(pack.Id + ":" + target, language, files)));
                }
            }

            var coverage = Coverage(target, Chain);
            if (target != RunesmithTarget && Options.UntranslatedPlugins == UntranslatedPlugins.English && coverage * 100 < Options.MinimumCoverage)
                bundles.Clear();

            if (Resources(target, LanguageTag.English) is { Count: > 0 } source)
                bundles.Add((LanguageTag.English, Bundle(target, LanguageTag.English, source)));
            return new TargetState(bundles, coverage);
        }

        private FrozenBundle Bundle(string owner, string language, List<LocaleResource> files)
        {
            var bundle = FluentBundle.MakeUnchecked(new FluentBundleOption
            {
                Locales = [language],
                UseIsolating = Options.IsolateArguments,
                TransformFunc = Options.IsPseudo ? PseudoLocalization.Transform : null,
                Functions = functions,
            });
            foreach (var file in files)
            {
                if (!bundle.AddResource(service.Parse(file), out var errors))
                {
                    foreach (var error in errors.Where(e => e.ErrorKind() != ErrorType.Parser))
                        LogOnce($"{owner}|{file.Name}|{error}", $"{owner}: {file.Name}: {error}");
                }
            }

            return bundle.ToFrozenBundle();
        }

        private List<LanguageInfo> ListLanguages()
        {
            var languages = new List<LanguageInfo> { new(LanguageTag.English, "English", "English", 1) };
            var tags = sources.Own(RunesmithTarget).Keys.Concat(sources.AllPacks.Select(p => p.Language))
                .Where(t => t != LanguageTag.English).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
            foreach (var tag in tags)
            {
                var (name, englishName) = sources.Packs(tag).Select(p => p.Pack).FirstOrDefault() is { } pack
                    ? (pack.Name, string.IsNullOrWhiteSpace(pack.EnglishName) ? CultureNames(tag).EnglishName : pack.EnglishName)
                    : CultureNames(tag);
                languages.Add(new LanguageInfo(tag, name, englishName, Coverage(RunesmithTarget, LanguageChain.For(tag)) ?? 0));
            }

            return languages;
        }
    }

    /// <summary>The bundles of a target in the order they are searched, English last, and how much of it the language translates.</summary>
    private sealed record TargetState(IReadOnlyList<(string Language, IReadBundle Bundle)> Bundles, double? Coverage);
}
