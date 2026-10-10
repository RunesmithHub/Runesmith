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

    public IEnumerable<SettingDefinition> Settings { get; } =
    [
        new(Shell, "Shell", Category, "")
        {
            Description = "The program new terminals start, such as /bin/zsh or pwsh.exe. Leave it empty for your default shell: $SHELL on Linux and macOS, PowerShell or cmd on Windows.",
        },
        new(FontFamily, "Font family", Category, "") { Description = "The terminal's font. Leave it empty to use the editor's font." },
        new(FontSize, "Font size", Category, 0) { Description = "The terminal's font size in pixels, or 0 to use the editor's.", Minimum = 0, Maximum = 40 },
        new(Scrollback, "Scrollback", Category, 10_000) { Description = "How many lines a terminal keeps after they scroll off its top.", Minimum = 0, Maximum = 1_000_000 },
        new(ShellIntegration, "Shell integration", Category, true)
        {
            Description = "Let bash and zsh tell Runesmith their folder, so links to files open from it and new terminals start in it. Takes effect in new terminals.",
        },
        new(CursorBlink, "Blinking cursor", Category, false) { Description = "Make the terminal's cursor blink." },
    ];
}
