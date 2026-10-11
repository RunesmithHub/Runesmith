namespace Runesmith.Sdk.Settings;

/// <summary>The keys of Runesmith's own settings.</summary>
public static class SettingKeys
{
    public const string Theme = "appearance.theme";
    public const string Accent = "appearance.accent";
    public const string FileIconTheme = "appearance.fileIconTheme";

    /// <summary>The language Runesmith shows: <c>system</c> to follow the system's language when an installed language pack covers it, or a
    /// BCP 47 tag such as <c>en</c> or <c>nl</c>.</summary>
    /// <remarks>Added in plugin API 0.1.2.</remarks>
    public const string Language = "appearance.language";
    public const string ColorScheme = "editor.colorScheme";

    public const string FontFamily = "editor.fontFamily";
    public const string FontSize = "editor.fontSize";
    public const string Ligatures = "editor.ligatures";
    public const string TabSize = "editor.tabSize";
    public const string InsertSpaces = "editor.insertSpaces";
    public const string LineNumbers = "editor.lineNumbers";
    public const string IndentGuides = "editor.indentGuides";
    public const string HighlightCurrentLine = "editor.highlightCurrentLine";
    public const string AutoCloseBrackets = "editor.autoCloseBrackets";
    public const string ShowWhitespace = "editor.showWhitespace";
    public const string CompletionOnType = "editor.completionOnType";
    public const string FormatOnSave = "editor.formatOnSave";
    public const string InlayHints = "editor.inlayHints";
    public const string CodeLens = "editor.codeLens";
    public const string DisabledKeyHooks = "editor.disabledKeyHooks";

    /// <summary>Whether the gutter shows fold markers and blocks fold; off unfolds everything.</summary>
    /// <remarks>Added in plugin API 0.1.2.</remarks>
    public const string Folding = "editor.folding";

    /// <summary>Whether the occurrences of the symbol under the caret are highlighted.</summary>
    /// <remarks>Added in plugin API 0.1.2.</remarks>
    public const string OccurrenceHighlight = "editor.occurrenceHighlight";

    /// <summary>Whether semantic tokens color the text over its TextMate highlighting; off leaves the TextMate colors alone.</summary>
    /// <remarks>Added in plugin API 0.1.2.</remarks>
    public const string SemanticHighlighting = "editor.semanticHighlighting";

    public const string AutoSave = "files.autoSave";
    public const string TrimTrailingWhitespace = "files.trimTrailingWhitespace";
    public const string InsertFinalNewline = "files.insertFinalNewline";
    public const string DefaultLineEnding = "files.defaultLineEnding";
    public const string Exclude = "files.exclude";

    public const string RestoreSession = "window.restoreSession";

    /// <summary>How a plugin shows whose translation is incomplete: <c>mix</c> shows each translated message translated and the rest in
    /// English; <c>english</c> shows a plugin below <see cref="MinimumCoverage"/> entirely in English.</summary>
    /// <remarks>Added in plugin API 0.1.2.</remarks>
    public const string UntranslatedPlugins = "localization.untranslatedPlugins";

    /// <summary>The percentage of a plugin's messages its translation must cover to show translated while
    /// <see cref="UntranslatedPlugins"/> is <c>english</c>.</summary>
    /// <remarks>Added in plugin API 0.1.2.</remarks>
    public const string MinimumCoverage = "localization.minimumCoverage";

    /// <summary>Whether every message shows pseudo-localized: accented, longer and in brackets, so text that is not localized and layouts
    /// that cut text off stand out.</summary>
    /// <remarks>Added in plugin API 0.1.2.</remarks>
    public const string PseudoLocalization = "localization.pseudo";
}
