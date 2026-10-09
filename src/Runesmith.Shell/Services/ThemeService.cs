using System.Composition;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using HammerUI.Theming;
using Runesmith.Languages.Highlighting;
using Runesmith.Sdk.Appearance;
using Runesmith.Sdk.Messaging;
using Runesmith.Sdk.Settings;
using Runesmith.Sdk.Shell;
using Runesmith.Shell.Appearance;

namespace Runesmith.Shell.Services;

/// <summary>Keeps the color theme, the accent color and the syntax color scheme in step with the settings.</summary>
/// <remarks><c>appearance.theme</c> holds a theme's id, or <see cref="AppearanceCatalog.System"/> to follow the system between Runesmith's
/// dark and light themes. A theme or scheme that is no longer there, such as one of a removed plugin, falls back to Runesmith's own.</remarks>
[Export(typeof(IThemeService))]
[Export]
[Shared]
public sealed class ThemeService : IThemeService, IDisposable
{
    private readonly ISettingsService settings;
    private readonly IMessageBus messages;
    private readonly AppearanceCatalog catalog;
    private readonly SyntaxColors syntax;
    private readonly Lazy<IOutputService> output;
    private readonly Application application;
    private readonly ThemeManager manager;
    private readonly ToolkitTheme theme;
    private readonly HashSet<string> reported = new(StringComparer.Ordinal);
    private (string Theme, bool IsDark, ThemePalette Palette, string Scheme)? applied;
    private bool applying;

    [ImportingConstructor]
    public ThemeService(ISettingsService settings, IMessageBus messages, AppearanceCatalog catalog, SyntaxColors syntax, Lazy<IOutputService> output)
    {
        this.settings = settings;
        this.messages = messages;
        this.catalog = catalog;
        this.syntax = syntax;
        this.output = output;
        application = Application.Current ?? throw new InvalidOperationException("The theme service needs a running application.");
        manager = new ThemeManager(application);
        theme = application.Styles.OfType<ToolkitTheme>().First();
        Current = catalog.Dark;
        Scheme = syntax.Current;
        settings.Changed += (_, e) =>
        {
            if (e.Key is SettingKeys.Theme or SettingKeys.Accent or SettingKeys.ColorScheme)
                Apply();
            else if (e.Key == ShellSettings.UiFontSize)
                ApplyFontSize();
        };
        manager.Changed += (_, _) =>
        {
            if (!applying)
                Apply();
        };
        foreach (var problem in catalog.Problems)
            Report(problem);
        Apply();
        ApplyFontSize();
    }

    public bool IsDark => application.ActualThemeVariant == ThemeVariant.Dark;

    /// <summary>Gets every theme, scheme and icon theme there is to pick from.</summary>
    public AppearanceCatalog Catalog => catalog;

    /// <summary>Gets the color theme that shows.</summary>
    public ThemeEntry Current { get; private set; }

    /// <summary>Gets the syntax color scheme the editor colors code with.</summary>
    public ColorScheme Scheme { get; private set; }

    public event EventHandler? Changed;

    public void Dispose() => manager.Dispose();

    public void Toggle() => settings.Set(SettingKeys.Theme, IsDark ? BuiltInAppearance.LightTheme : BuiltInAppearance.DarkTheme);

    private void ApplyFontSize()
    {
        var size = settings.Get<double>(ShellSettings.UiFontSize);
        application.Resources["FontSizeBody"] = size;
        application.Resources["ControlContentThemeFontSize"] = size;
        application.Resources["FontSizeCaption"] = size - 1;
        application.Resources["ToolTipContentThemeFontSize"] = size - 1;
    }

    private void Apply()
    {
        applying = true;
        try
        {
            var id = settings.Get<string>(SettingKeys.Theme);
            var followsSystem = string.Equals(id, AppearanceCatalog.System, StringComparison.OrdinalIgnoreCase);
            var chosen = followsSystem ? null : catalog.FindTheme(id);
            if (!followsSystem && chosen is null)
                Report($"The color theme {id} is not installed, so Runesmith's dark theme shows. Choose another in Appearance.");

            var dark = chosen is { Theme.IsDark: true } ? chosen : catalog.Dark;
            var light = chosen is { Theme.IsDark: false } ? chosen : catalog.Light;
            var accent = Color.TryParse(settings.Get<string>(SettingKeys.Accent), out var color) ? color : (Color?)null;
            SetPalette(ThemeVariant.Dark, WithAccent(dark.Palette, accent));
            SetPalette(ThemeVariant.Light, WithAccent(light.Palette, accent));
            manager.Mode = followsSystem ? ThemeMode.System : (chosen ?? dark).Theme.IsDark ? ThemeMode.Dark : ThemeMode.Light;

            Current = IsDark ? dark : light;
            Scheme = ApplyScheme(Current);
            var state = (Current.Theme.Id, IsDark, IsDark ? theme.DarkPalette : theme.LightPalette, Scheme.Id);
            if (applied == state)
                return;

            applied = state;
            Changed?.Invoke(this, EventArgs.Empty);
            messages.Publish(new ThemeChangedMessage(IsDark));
        }
        finally
        {
            applying = false;
        }
    }

    private ColorScheme ApplyScheme(ThemeEntry shown)
    {
        var builtIn = shown.Theme.IsDark ? BuiltInColorSchemes.Dark : BuiltInColorSchemes.Light;
        var id = settings.Get<string>(SettingKeys.ColorScheme);
        var scheme = string.IsNullOrWhiteSpace(id) ? null : catalog.FindScheme(id)?.Scheme;
        if (!string.IsNullOrWhiteSpace(id) && scheme is null)
            Report($"The syntax color scheme {id} is not installed, so the color theme's own shows.");

        scheme ??= catalog.FindScheme(shown.Theme.ColorScheme)?.Scheme ?? builtIn;
        if (syntax.Use(scheme) is { } problem)
        {
            Report($"The syntax color scheme {scheme.Name} ({scheme.Id}) cannot be used: {problem}");
            scheme = builtIn;
            syntax.Use(scheme);
        }

        return scheme;
    }

    private void SetPalette(ThemeVariant variant, ThemePalette palette)
    {
        if ((variant == ThemeVariant.Dark ? theme.DarkPalette : theme.LightPalette) != palette)
            theme.SetPalette(variant, palette);
    }

    private static ThemePalette WithAccent(ThemePalette palette, Color? accent) =>
        accent is { } color ? palette with { Accent = color, AccentForeground = ThemePalettes.ForegroundOn(color) } : palette;

    private void Report(string problem)
    {
        if (reported.Add(problem))
            output.Value.GetChannel(PluginAccess.ChannelName).AppendLine(problem);
    }
}
