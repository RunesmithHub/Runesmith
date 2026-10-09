using System.Composition;
using Runesmith.Sdk.Settings;

namespace Runesmith.Workspace.Settings;

/// <summary>Runesmith's own settings.</summary>
[Export(typeof(ISettingContributor))]
[Shared]
public sealed class CoreSettings : ISettingContributor
{
    private const string Appearance = "Appearance";
    private const string Editor = "Editor";
    private const string Files = "Files";
    private const string Window = "Window";

    /// <summary>The globs <see cref="SettingKeys.Exclude"/> starts with.</summary>
    public const string DefaultExclude = "**/.git;**/bin;**/obj;**/node_modules;**/.vs;**/.idea";

    public IEnumerable<SettingDefinition> Settings { get; } =
    [
        new(SettingKeys.Theme, "Color theme", Appearance, "dark")
        {
            Description = "The colors of the window and the editor: dark, light, system to follow the system between the two, or the id of a plugin's theme.",
        },
        new(SettingKeys.Accent, "Accent color", Appearance, "")
        {
            Description = "The color of selections, focus and highlights, as a hex color such as #7073F6. Leave it empty for the theme's own accent.",
        },
        new(SettingKeys.ColorScheme, "Syntax colors", Appearance, "")
        {
            Description = "The colors of code in the editor, by the id of a color scheme. Leave it empty for the color theme's own.",
        },
        new(SettingKeys.FileIconTheme, "File icons", Appearance, "runesmith") { Description = "The icons of files and folders, by the id of a file icon theme." },

        new(SettingKeys.FontFamily, "Font", Editor, "JetBrains Mono") { Description = "The editor's font; a list separated by commas tries each in turn." },
        new(SettingKeys.FontSize, "Font size", Editor, 14) { Description = "The editor's font size, in pixels.", Minimum = 6, Maximum = 72 },
        new(SettingKeys.Ligatures, "Font ligatures", Editor, true) { Description = "Draw character pairs such as => and != as one symbol when the font has ligatures." },
        new(SettingKeys.TabSize, "Tab size", Editor, 4) { Description = "How many spaces a tab is wide, and how many spaces the Tab key inserts.", Minimum = 1, Maximum = 16 },
        new(SettingKeys.InsertSpaces, "Insert spaces", Editor, true) { Description = "Insert spaces when Tab is pressed, rather than a tab character." },
        new(SettingKeys.LineNumbers, "Line numbers", Editor, true) { Description = "Show line numbers beside the text." },
        new(SettingKeys.IndentGuides, "Indent guides", Editor, true) { Description = "Draw thin lines that show the levels of indentation." },
        new(SettingKeys.HighlightCurrentLine, "Highlight the current line", Editor, true) { Description = "Shade the line the caret is on." },
        new(SettingKeys.AutoCloseBrackets, "Close brackets and quotes", Editor, true) { Description = "Type the closing bracket or quote when you type an opening one." },
        new(SettingKeys.ShowWhitespace, "Show whitespace", Editor, false) { Description = "Draw spaces and tabs as faint dots and arrows." },
        new(SettingKeys.CompletionOnType, "Suggest while typing", Editor, true) { Description = "Show completions as you type, not only when you press Ctrl+Space." },
        new(SettingKeys.FormatOnSave, "Format on save", Editor, false) { Description = "Format a file with the formatter of its language each time you save it." },
        new(SettingKeys.InlayHints, "Inlay hints", Editor, true) { Description = "Show hints inside lines, such as parameter names and inferred types, where the language offers them." },
        new(SettingKeys.CodeLens, "Code lens", Editor, true) { Description = "Show lines of information and actions above code, such as reference counts, where the language offers them." },
        new(SettingKeys.DisabledKeyHooks, "Plugins without keyboard hooks", Editor, "")
        {
            Description = "The ids of plugins, separated by commas, whose keyboard hooks are turned off, so they no longer see the keys you press in the editor.",
        },

        new(SettingKeys.AutoSave, "Auto save", Files, "off")
        {
            Description = "Save changed files by themselves: never, a second after you stop typing, or when the editor loses focus.",
            Choices = ["off", "afterDelay", "onFocusChange"],
        },
        new(SettingKeys.TrimTrailingWhitespace, "Trim trailing whitespace", Files, false) { Description = "Remove spaces and tabs at the ends of lines when saving." },
        new(SettingKeys.InsertFinalNewline, "Insert a final newline", Files, true) { Description = "End the file with a line break when saving." },
        new(SettingKeys.DefaultLineEnding, "Line ending of new files", Files, "auto")
        {
            Description = "The line ending of new files, and of files without line breaks: the system's own, LF or CRLF.",
            Choices = ["auto", "lf", "crlf"],
        },
        new(SettingKeys.Exclude, "Excluded files", Files, DefaultExclude)
        {
            Description = "Globs, separated by semicolons, of files and folders that the Explorer, Quick Open and search leave out.",
        },

        new(SettingKeys.RestoreSession, "Restore the session", Window, true) { Description = "Open the last folder and its files again when Runesmith starts." },
    ];
}
