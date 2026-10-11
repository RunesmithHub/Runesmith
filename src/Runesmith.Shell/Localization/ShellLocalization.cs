using System.Composition;
using System.Globalization;
using System.Reflection;
using Avalonia.Media;
using Runesmith.Composition;
using Runesmith.Hub;
using Runesmith.Localization;
using Runesmith.Sdk.Localization;
using Runesmith.Sdk.Settings;
using Runesmith.Sdk.Shell;
using Runesmith.Shell.Hub;
using Runesmith.Shell.Services;
using Runesmith.Workspace.Settings;

namespace Runesmith.Shell.Localization;

/// <summary>What localization is made of in one run of Runesmith.</summary>
/// <param name="Runesmith">Runesmith's own assemblies, whose messages form the target <c>runesmith</c>.</param>
/// <param name="Plugins">The plugins found at start; those that run give their own translations.</param>
/// <param name="Packs">Finds the language packs: first those found at start, then, when asked again, those there now.</param>
/// <param name="SystemCulture">The system's culture, which the language <c>system</c> follows.</param>
internal sealed record LocalizationEnvironment(
    IReadOnlyList<Assembly> Runesmith, IReadOnlyList<PluginInfo> Plugins, Func<bool, IReadOnlyList<InstalledLanguagePack>> Packs, CultureInfo SystemCulture)
{
    /// <summary>Gets the plugin that calls the service, or null when Runesmith does.</summary>
    public Func<PluginInfo?> Caller { get; init; } = () => null;

    /// <summary>Gets the plugin an assembly belongs to, or null.</summary>
    public Func<Assembly, PluginInfo?> PluginOf { get; init; } = _ => null;

    /// <summary>Reads the Fluent files an assembly file embeds.</summary>
    public Func<string, IReadOnlyList<LocaleResource>> Read { get; init; } = LocaleResources.Read;

    public Action<string> Log { get; init; } = _ => { };

    /// <summary>Makes numbers and dates written without the localizer follow the language too; its arguments are the language and the
    /// culture of numbers and dates.</summary>
    public Action<CultureInfo, CultureInfo>? ApplyCultures { get; init; }

    /// <summary>Gets whether the generated <c>Strings</c> classes and XAML find their messages through this localization.</summary>
    public bool Attach { get; init; }
}

/// <summary>Runesmith's localization inside the window: shows Runesmith's own messages, each running plugin's and the installed language
/// packs' in the language the settings choose, and follows the settings and the language packs live.</summary>
/// <remarks>A plugin that calls <see cref="GetLocalizer"/> gets its own messages, and Runesmith its own. The language packs are looked for
/// again when the hub installs or removes a plugin and when a plugin is turned on or off; a language pack runs no code, so the change shows
/// at once.</remarks>
[Export(typeof(ILocalizationService))]
[Export]
[Shared]
public sealed class ShellLocalization : ILocalizationService, IDisposable
{
    private static readonly string[] OwnPrefixes = ["Runesmith.", "runesmith"];

    private readonly ISettingsService settings;
    private readonly LocalizationEnvironment environment;
    private readonly HashSet<Assembly> ownAssemblies;
    private readonly List<(string Target, IReadOnlyList<LocaleResource> Resources)> ownResources = [];
    private string packsKey;

    [ImportingConstructor]
    public ShellLocalization(ISettingsService settings, RuntimeInfo runtime, Lazy<HubService> hub, Lazy<IOutputService> output)
        : this(settings, Environment(settings, runtime, hub, output))
    {
        hub.Value.Client.Changed += (_, _) => UiThread.Run(Reload);
        Changed += (_, _) => hub.Value.Model.Refresh();
    }

    internal ShellLocalization(ISettingsService settings, LocalizationEnvironment environment)
    {
        this.settings = settings;
        this.environment = environment;
        ownAssemblies = [.. environment.Runesmith];
        ownResources.Add((LocalizationService.RunesmithTarget, [.. environment.Runesmith.SelectMany(LocaleResources.Read)]));
        foreach (var plugin in environment.Plugins.Where(p => p.State == PluginState.Loaded && !p.Manifest.IsLanguagePack))
        {
            if (Read(plugin.AssemblyPath) is { Count: > 0 } resources)
                ownResources.Add((plugin.Manifest.Id, resources));
        }

        var packs = environment.Packs(false);
        packsKey = Key(packs);
        Core = new LocalizationService(Sources(packs), LocalizationSettings.Read(settings, environment.SystemCulture), TargetOf, environment.Log, Raise);
        Core.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        ApplyCultures();
        settings.Changed += OnSettingChanged;
        if (environment.Attach)
            Core.Attach();
    }

    public event EventHandler? Changed;

    public CultureInfo CurrentLanguage => Core.CurrentLanguage;

    public IReadOnlyList<LanguageInfo> AvailableLanguages => Core.AvailableLanguages;

    /// <summary>Gets the localization behind the service: every target's messages, coverage and the language packs.</summary>
    internal LocalizationService Core { get; }

    /// <summary>Gets the system's culture, which the language <c>system</c> follows.</summary>
    internal CultureInfo SystemCulture => environment.SystemCulture;

    /// <summary>Gets the direction text and layouts flow in the language that shows.</summary>
    public FlowDirection FlowDirection => CurrentLanguage.TextInfo.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public ILocalizer GetLocalizer() => Core.GetLocalizer(environment.Caller()?.Manifest.Id ?? LocalizationService.RunesmithTarget);

    /// <summary>Looks for the language packs again, and shows text from them when they changed.</summary>
    public void Reload()
    {
        var packs = environment.Packs(true);
        var key = Key(packs);
        if (key == packsKey)
            return;

        packsKey = key;
        Core.Reload(Sources(packs));
    }

    public void Dispose()
    {
        settings.Changed -= OnSettingChanged;
        Core.Dispose();
    }

    /// <summary>Gets Runesmith's own assemblies that are loaded or referenced from the application and the shell.</summary>
    internal static IReadOnlyList<Assembly> RunesmithAssemblies()
    {
        var found = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        var pending = new Queue<Assembly>([typeof(ShellLocalization).Assembly]);
        if (Assembly.GetEntryAssembly() is { } entry && IsOwn(entry.GetName()))
            pending.Enqueue(entry);

        while (pending.TryDequeue(out var assembly))
        {
            if (!found.TryAdd(assembly.GetName().Name ?? "", assembly))
                continue;

            foreach (var reference in assembly.GetReferencedAssemblies().Where(name => IsOwn(name) && !found.ContainsKey(name.Name ?? "")))
            {
                try
                {
                    pending.Enqueue(Assembly.Load(reference));
                }
                catch (Exception exception) when (exception is IOException or BadImageFormatException)
                {
                }
            }
        }

        return [.. found.Values];
    }

    private static bool IsOwn(AssemblyName name) => name.Name is { } text && OwnPrefixes.Any(prefix => text.StartsWith(prefix, StringComparison.Ordinal));

    private static LocalizationEnvironment Environment(ISettingsService settings, RuntimeInfo runtime, Lazy<HubService> hub, Lazy<IOutputService> output)
    {
        var finder = new InstalledLanguagePacks(
            runtime.Plugins,
            runtime.PluginFolders,
            new HubPaths(Sdk.RunesmithPaths.Hub),
            () => hub.Value.Client.State,
            () => hub.Value.Disabled(),
            runtime.HubStartup.Withheld,
            () => settings.GetValue(CoreSettings.AllowLocalOverrides, SettingScope.User) is true);
        return new LocalizationEnvironment(RunesmithAssemblies(), runtime.Plugins, again => again ? finder.Find() : finder.AtStart(), CultureInfo.CurrentUICulture)
        {
            Caller = PluginCallers.Current,
            PluginOf = PluginCallers.Of,
            Log = line => output.Value.GetChannel(PluginAccess.ChannelName).AppendLine(line),
            ApplyCultures = Cultures.Apply,
            Attach = true,
        };
    }

    private static string Key(IEnumerable<InstalledLanguagePack> packs) => string.Join('\n', packs.Select(pack => pack.Key).Order(StringComparer.Ordinal));

    private string? TargetOf(Assembly assembly) =>
        environment.PluginOf(assembly) is { } plugin ? plugin.Manifest.Id : ownAssemblies.Contains(assembly) ? LocalizationService.RunesmithTarget : null;

    private TranslationSources Sources(IEnumerable<InstalledLanguagePack> packs)
    {
        var sources = new TranslationSources();
        foreach (var (target, resources) in ownResources)
            sources.AddOwn(target, resources);
        foreach (var pack in packs)
            sources.AddPack(pack.Pack, Read(pack.AssemblyPath) ?? []);
        return sources;
    }

    private IReadOnlyList<LocaleResource>? Read(string assemblyPath)
    {
        try
        {
            return environment.Read(assemblyPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or BadImageFormatException)
        {
            environment.Log($"The translations in {assemblyPath} could not be read: {exception.Message}");
            return null;
        }
    }

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.Key is SettingKeys.Language or SettingKeys.UntranslatedPlugins or SettingKeys.MinimumCoverage or SettingKeys.PseudoLocalization)
            Core.Apply(LocalizationSettings.Read(settings, environment.SystemCulture));
        else if (e.Key == ShellSettings.DisabledPlugins)
            Reload();
    }

    // The cultures change before any text is fetched again, so text that formats a number or a date itself is right too.
    private void Raise(Action changed) => UiThread.Run(() =>
    {
        ApplyCultures();
        changed();
    });

    private void ApplyCultures() => environment.ApplyCultures?.Invoke(Core.CurrentLanguage, Core.FormatCulture);

    /// <summary>The cultures of the process, which code that formats numbers and dates itself follows.</summary>
    private static class Cultures
    {
        public static void Apply(CultureInfo language, CultureInfo format)
        {
            CultureInfo.DefaultThreadCurrentUICulture = language;
            CultureInfo.DefaultThreadCurrentCulture = format;
            CultureInfo.CurrentUICulture = language;
            CultureInfo.CurrentCulture = format;
        }
    }
}
