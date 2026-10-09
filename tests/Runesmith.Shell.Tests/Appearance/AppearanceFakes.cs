using Runesmith.Composition;
using Runesmith.Sdk.Appearance;
using Runesmith.Sdk.Messaging;
using Runesmith.Sdk.Shell;
using Runesmith.Shell.Appearance;
using Runesmith.Shell.Services;
using Runesmith.Shell.Tests.Plugins;
using Runesmith.Workspace.Settings;

namespace Runesmith.Shell.Tests.Appearance;

/// <summary>A plugin's appearance contributions, for the tests.</summary>
internal sealed class FakeAppearance(IEnumerable<ColorTheme>? themes = null, IEnumerable<ColorScheme>? schemes = null, IEnumerable<FileIconTheme>? iconThemes = null)
    : IColorThemeContributor, IColorSchemeContributor, IFileIconThemeContributor
{
    public static PluginInfo Plugin { get; } = TestCallers.Plugin("ember.themes");

    public IEnumerable<ColorTheme> Themes => themes ?? [];

    public IEnumerable<ColorScheme> Schemes => schemes ?? [];

    public IEnumerable<FileIconTheme> IconThemes => iconThemes ?? [];

    /// <summary>A dark theme with readable text.</summary>
    public static ColorTheme Dusk { get; } = new("ember.dusk", "Ember Dusk", IsDark: true)
    {
        Colors = new ThemeColors { Background = "#1A1410", Surface = "#211912", SurfaceRaised = "#2A2018", SurfaceSunken = "#1C150F", TextPrimary = "#F3E6D8", Accent = "#E4A23C" },
        ColorScheme = "ember.dusk",
    };

    /// <summary>Builds a catalog of Runesmith's own contributions and <paramref name="plugin"/>'s, which the fake plugin owns.</summary>
    public static AppearanceCatalog Catalog(FakeAppearance? plugin = null)
    {
        var builtIn = new BuiltInAppearance();
        var schemes = new Runesmith.Languages.Highlighting.BuiltInColorSchemes();
        return plugin is null
            ? new AppearanceCatalog([builtIn], [schemes], [builtIn], _ => null)
            : new AppearanceCatalog([builtIn, plugin], [schemes, plugin], [builtIn, plugin], c => c == plugin ? Plugin : null);
    }

    /// <summary>Writes a TextMate theme that colors keywords, and gives its path.</summary>
    public static string WriteScheme(string folder, string keyword = "#FF8800")
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "dusk.json");
        File.WriteAllText(path, $$"""
            { "name": "Dusk", "type": "dark", "tokenColors": [
              { "settings": { "foreground": "#F3E6D8", "background": "#211912" } },
              { "scope": "keyword", "settings": { "foreground": "{{keyword}}" } } ] }
            """);
        return path;
    }
}

/// <summary>Output channels that keep their lines.</summary>
internal sealed class FakeOutput : IOutputService, IOutputChannel
{
    public List<string> Lines { get; } = [];

    public string Name => "Plugins";

    public IOutputChannel GetChannel(string name) => this;

    public void Append(string text) => Lines.Add(text);

    public void AppendLine(string line) => Lines.Add(line);

    public void Clear() => Lines.Clear();

    public void Show()
    {
    }
}

/// <summary>Settings in a file of a temporary folder, with Runesmith's own definitions.</summary>
internal static class TestSettings
{
    public static SettingsService Create(string path) => new([new CoreSettings(), new ShellSettings()], new MessageBus(), path, () => null);
}

/// <summary>Runesmith's own settings, kept in memory.</summary>
internal sealed class MemorySettings : Sdk.Settings.ISettingsService
{
    public Dictionary<string, object> Values { get; } = new(StringComparer.Ordinal);

    public IReadOnlyList<Sdk.Settings.SettingDefinition> Definitions { get; } = [.. new CoreSettings().Settings, .. new ShellSettings().Settings];

    public event EventHandler<Sdk.Settings.SettingChangedEventArgs>? Changed;

    public T Get<T>(string key) => (T)(Values.TryGetValue(key, out var value) ? value : Definitions.First(d => d.Key == key).DefaultValue);

    public object? GetValue(string key, Sdk.Settings.SettingScope scope) => Values.GetValueOrDefault(key);

    public Sdk.Settings.SettingScope GetEffectiveScope(string key) => Values.ContainsKey(key) ? Sdk.Settings.SettingScope.User : Sdk.Settings.SettingScope.Default;

    public void Set(string key, object value, Sdk.Settings.SettingScope scope = Sdk.Settings.SettingScope.User)
    {
        Values[key] = value;
        Changed?.Invoke(this, new Sdk.Settings.SettingChangedEventArgs(key));
    }

    public void Reset(string key, Sdk.Settings.SettingScope scope = Sdk.Settings.SettingScope.User)
    {
        Values.Remove(key);
        Changed?.Invoke(this, new Sdk.Settings.SettingChangedEventArgs(key));
    }
}
