using System.Composition;
using Runesmith.Sdk.Settings;

namespace Runesmith.Shell.Services;

/// <summary>Where the menus are: in the main menu button of the toolbar, or in a menu bar of their own.</summary>
public enum MenuBarMode
{
    Toolbar,
    Separate,
}

/// <summary>Who draws the window's title bar: Runesmith, as its toolbar, or the system, above the toolbar.</summary>
public enum TitleBarMode
{
    Custom,
    System,
}

/// <summary>The settings of the shell itself.</summary>
[Export(typeof(ISettingContributor))]
public sealed class ShellSettings : ISettingContributor
{
    /// <summary>The ids of the plugins the user turned off, separated by semicolons.</summary>
    public const string DisabledPlugins = "plugins.disabled";

    /// <summary>Where the menus are: <c>toolbar</c> or <c>separate</c>.</summary>
    public const string MenuBar = "window.menuBar";

    /// <summary>Who draws the title bar: <c>custom</c> or <c>system</c>; read when Runesmith starts.</summary>
    public const string TitleBar = "window.titleBar";

    /// <summary>The size of the interface's text, in pixels.</summary>
    public const string UiFontSize = "appearance.uiFontSize";

    /// <summary>Whether the file's path shows under the editor's tabs; when it does not, the status bar shows it.</summary>
    public const string Breadcrumbs = "editor.breadcrumbs";

    public IEnumerable<SettingDefinition> Settings =>
    [
        new(DisabledPlugins, Strings.SettingsTurnedOffPlugins, "Plugins", "") { Description = Strings.SettingsTurnedOffPluginsDescription },
        new(MenuBar, Strings.SettingsMenus, "Window", "toolbar")
        {
            Description = Strings.SettingsMenusDescription,
            Choices = ["toolbar", "separate"],
            ChoiceTitles = [Strings.SettingsChoiceToolbar, Strings.SettingsChoiceSeparate],
        },
        new(TitleBar, Strings.SettingsTitleBar, "Window", "custom")
        {
            Description = Strings.SettingsTitleBarDescription,
            Choices = ["custom", "system"],
            ChoiceTitles = [Strings.SettingsChoiceCustom, Strings.SettingsChoiceSystem],
        },
        new(UiFontSize, Strings.SettingsInterfaceFontSize, "Appearance", 13) { Description = Strings.SettingsInterfaceFontSizeDescription, Minimum = 11, Maximum = 18 },
        new(Breadcrumbs, Strings.SettingsBreadcrumbs, "Editor", true) { Description = Strings.SettingsBreadcrumbsDescription },
    ];

    /// <summary>Reads the <see cref="TitleBar"/> setting; anything unknown means a custom title bar.</summary>
    public static TitleBarMode ParseTitleBar(string? value) =>
        string.Equals(value, "system", StringComparison.OrdinalIgnoreCase) ? TitleBarMode.System : TitleBarMode.Custom;

    /// <summary>Reads the <see cref="MenuBar"/> setting; anything unknown means the toolbar.</summary>
    public static MenuBarMode ParseMenuBar(string? value) =>
        string.Equals(value, "separate", StringComparison.OrdinalIgnoreCase) ? MenuBarMode.Separate : MenuBarMode.Toolbar;
}
