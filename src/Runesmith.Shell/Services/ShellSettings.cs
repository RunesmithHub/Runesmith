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

    public IEnumerable<SettingDefinition> Settings { get; } =
    [
        new(DisabledPlugins, "Turned off plugins", "Plugins", "") { Description = "The ids of the plugins Runesmith does not load, separated by semicolons." },
        new(MenuBar, "Menus", "Window", "toolbar")
        {
            Description = "Where the menus are: behind the menu button at the left of the toolbar, or in a menu bar of their own under it.",
            Choices = ["toolbar", "separate"],
        },
        new(TitleBar, "Title bar", "Window", "custom")
        {
            Description = "Whether the toolbar is the window's title bar, with its own window buttons, or the system's title bar stays above it. Takes effect when Runesmith starts again. macOS always keeps its own.",
            Choices = ["custom", "system"],
        },
        new(UiFontSize, "Interface font size", "Appearance", 13) { Description = "The size of the text of menus, panels and the status bar, in pixels.", Minimum = 11, Maximum = 18 },
        new(Breadcrumbs, "Breadcrumbs", "Editor", true) { Description = "Show the file's path under the tabs. When this is off, the status bar shows it." },
    ];

    /// <summary>Reads the <see cref="TitleBar"/> setting; anything unknown means a custom title bar.</summary>
    public static TitleBarMode ParseTitleBar(string? value) =>
        string.Equals(value, "system", StringComparison.OrdinalIgnoreCase) ? TitleBarMode.System : TitleBarMode.Custom;

    /// <summary>Reads the <see cref="MenuBar"/> setting; anything unknown means the toolbar.</summary>
    public static MenuBarMode ParseMenuBar(string? value) =>
        string.Equals(value, "separate", StringComparison.OrdinalIgnoreCase) ? MenuBarMode.Separate : MenuBarMode.Toolbar;
}
