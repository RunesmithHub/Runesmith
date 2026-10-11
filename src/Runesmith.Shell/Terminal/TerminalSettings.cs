using System.Composition;
using Runesmith.Sdk.Settings;

namespace Runesmith.Shell.Terminal;

/// <summary>The settings of the Terminal panel.</summary>
[Export(typeof(ISettingContributor))]
public sealed class TerminalSettings : ISettingContributor
{
    public const string Category = "Terminal";

    /// <summary>The shell new terminals start, or empty for the user's default shell.</summary>
    public const string Shell = "terminal.shell";

    /// <summary>The font family, or empty for the editor's.</summary>
    public const string FontFamily = "terminal.fontFamily";

    /// <summary>The font size in pixels, or 0 for the editor's.</summary>
    public const string FontSize = "terminal.fontSize";

    /// <summary>How many lines a terminal keeps after they scroll off the top.</summary>
    public const string Scrollback = "terminal.scrollback";

    /// <summary>Whether bash and zsh report their folder and their commands' exit codes to Runesmith.</summary>
    public const string ShellIntegration = "terminal.shellIntegration";

    /// <summary>Whether the cursor blinks.</summary>
    public const string CursorBlink = "terminal.cursorBlink";

    public IEnumerable<SettingDefinition> Settings =>
    [
        new(Shell, Strings.SettingsShell, Category, "")
        {
            Description = Strings.SettingsShellDescription,
        },
        new(FontFamily, Strings.SettingsFontFamily, Category, "") { Description = Strings.SettingsFontFamilyDescription },
        new(FontSize, Strings.SettingsFontSize, Category, 0) { Description = Strings.SettingsFontSizeDescription, Minimum = 0, Maximum = 40 },
        new(Scrollback, Strings.SettingsScrollback, Category, 10_000) { Description = Strings.SettingsScrollbackDescription, Minimum = 0, Maximum = 1_000_000 },
        new(ShellIntegration, Strings.SettingsShellIntegration, Category, true)
        {
            Description = Strings.SettingsShellIntegrationDescription,
        },
        new(CursorBlink, Strings.SettingsBlinkingCursor, Category, false) { Description = Strings.SettingsBlinkingCursorDescription },
    ];
}
