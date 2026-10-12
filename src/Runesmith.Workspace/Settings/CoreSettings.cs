using System.Composition;
using Runesmith.Composition;
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
    private const string Plugins = "Plugins";

    /// <summary>Whether a local copy of a plugin the hub installed runs in its place; read from the user's settings alone
    /// when Runesmith starts.</summary>
    public const string AllowLocalOverrides = PluginDiscovery.AllowLocalOverridesSetting;

    /// <summary>Gets the settings that count only in the user's settings, never in an open folder's.</summary>
    public static IReadOnlySet<string> UserOnly { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        AllowLocalOverrides, SettingKeys.Language, SettingKeys.UntranslatedPlugins, SettingKeys.MinimumCoverage, SettingKeys.PseudoLocalization,
    };

    /// <summary>The globs <see cref="SettingKeys.Exclude"/> starts with: none. Plugins leave out the folders of their own tools.</summary>
    public const string DefaultExclude = "";

    public IEnumerable<SettingDefinition> Settings =>
    [
        new(SettingKeys.Theme, Strings.WorkspaceColorTheme, Appearance, "dark")
        {
            Description = Strings.WorkspaceColorThemeDescription,
        },
        new(SettingKeys.Accent, Strings.WorkspaceAccentColor, Appearance, "")
        {
            Description = Strings.WorkspaceAccentColorDescription,
        },
        new(SettingKeys.ColorScheme, Strings.WorkspaceSyntaxColors, Appearance, "")
        {
            Description = Strings.WorkspaceSyntaxColorsDescription,
        },
        new(SettingKeys.FileIconTheme, Strings.WorkspaceFileIcons, Appearance, "runesmith") { Description = Strings.WorkspaceFileIconsDescription },

        new(SettingKeys.FontFamily, Strings.WorkspaceFont, Editor, "JetBrains Mono") { Description = Strings.WorkspaceFontDescription },
        new(SettingKeys.FontSize, Strings.WorkspaceFontSize, Editor, 14) { Description = Strings.WorkspaceFontSizeDescription, Minimum = 6, Maximum = 72 },
        new(SettingKeys.Ligatures, Strings.WorkspaceFontLigatures, Editor, true) { Description = Strings.WorkspaceFontLigaturesDescription },
        new(SettingKeys.TabSize, Strings.WorkspaceTabSize, Editor, 4) { Description = Strings.WorkspaceTabSizeDescription, Minimum = 1, Maximum = 16 },
        new(SettingKeys.InsertSpaces, Strings.WorkspaceInsertSpaces, Editor, true) { Description = Strings.WorkspaceInsertSpacesDescription },
        new(SettingKeys.LineNumbers, Strings.WorkspaceLineNumbers, Editor, true) { Description = Strings.WorkspaceLineNumbersDescription },
        new(SettingKeys.IndentGuides, Strings.WorkspaceIndentGuides, Editor, true) { Description = Strings.WorkspaceIndentGuidesDescription },
        new(SettingKeys.HighlightCurrentLine, Strings.WorkspaceHighlightTheCurrentLine, Editor, true) { Description = Strings.WorkspaceHighlightTheCurrentLineDescription },
        new(SettingKeys.AutoCloseBrackets, Strings.WorkspaceCloseBracketsAndQuotes, Editor, true) { Description = Strings.WorkspaceCloseBracketsAndQuotesDescription },
        new(SettingKeys.ShowWhitespace, Strings.WorkspaceShowWhitespace, Editor, false) { Description = Strings.WorkspaceShowWhitespaceDescription },
        new(SettingKeys.CompletionOnType, Strings.WorkspaceSuggestWhileTyping, Editor, true) { Description = Strings.WorkspaceSuggestWhileTypingDescription },
        new(SettingKeys.FormatOnSave, Strings.WorkspaceFormatOnSave, Editor, false) { Description = Strings.WorkspaceFormatOnSaveDescription },
        new(SettingKeys.InlayHints, Strings.WorkspaceInlayHints, Editor, true) { Description = Strings.WorkspaceInlayHintsDescription },
        new(SettingKeys.CodeLens, Strings.WorkspaceCodeLens, Editor, true) { Description = Strings.WorkspaceCodeLensDescription },
        new(SettingKeys.Folding, Strings.WorkspaceFolding, Editor, true) { Description = Strings.WorkspaceFoldingDescription },
        new(SettingKeys.OccurrenceHighlight, Strings.WorkspaceHighlightOccurrences, Editor, true)
        {
            Description = Strings.WorkspaceHighlightOccurrencesDescription,
        },
        new(SettingKeys.SemanticHighlighting, Strings.WorkspaceSemanticHighlighting, Editor, true)
        {
            Description = Strings.WorkspaceSemanticHighlightingDescription,
        },
        new(SettingKeys.DisabledKeyHooks, Strings.WorkspacePluginsWithoutKeyboardHooks, Editor, "")
        {
            Description = Strings.WorkspacePluginsWithoutKeyboardHooksDescription,
        },

        new(SettingKeys.AutoSave, Strings.WorkspaceAutoSave, Files, "off")
        {
            Description = Strings.WorkspaceAutoSaveDescription,
            Choices = ["off", "afterDelay", "onFocusChange"],
            ChoiceTitles = [Strings.WorkspaceChoiceOff, Strings.WorkspaceChoiceAfterDelay, Strings.WorkspaceChoiceOnFocusChange],
        },
        new(SettingKeys.TrimTrailingWhitespace, Strings.WorkspaceTrimTrailingWhitespace, Files, false) { Description = Strings.WorkspaceTrimTrailingWhitespaceDescription },
        new(SettingKeys.InsertFinalNewline, Strings.WorkspaceInsertAFinalNewline, Files, true) { Description = Strings.WorkspaceInsertAFinalNewlineDescription },
        new(SettingKeys.DefaultLineEnding, Strings.WorkspaceLineEndingOfNewFiles, Files, "auto")
        {
            Description = Strings.WorkspaceLineEndingOfNewFilesDescription,
            Choices = ["auto", "lf", "crlf"],
            ChoiceTitles = [Strings.WorkspaceChoiceAuto, Strings.WorkspaceChoiceLf, Strings.WorkspaceChoiceCrlf],
        },
        new(SettingKeys.Exclude, Strings.WorkspaceExcludedFiles, Files, DefaultExclude)
        {
            Description = Strings.WorkspaceExcludedFilesDescription,
        },

        new(SettingKeys.RestoreSession, Strings.WorkspaceRestoreTheSession, Window, true) { Description = Strings.WorkspaceRestoreTheSessionDescription },

        new(AllowLocalOverrides, Strings.WorkspaceLocalCopiesReplacePlugins, Plugins, false)
        {
            Description = Strings.WorkspaceLocalCopiesReplacePluginsDescription,
        },
    ];
}
