namespace Runesmith.Editor;

/// <summary>How an editor looks and behaves; the shell keeps it in step with the settings.</summary>
public sealed record EditorOptions
{
    /// <summary>The font family of the bundled JetBrains Mono.</summary>
    public const string BundledFontFamily = "avares://Runesmith.Editor/Assets/Fonts#JetBrains Mono";

    public static EditorOptions Default { get; } = new();

    /// <summary>Gets the font, by name; "JetBrains Mono" uses the bundled copy when the system has none.</summary>
    public string FontFamily { get; init; } = "JetBrains Mono";

    public double FontSize { get; init; } = 14;

    /// <summary>Gets whether character sequences such as <c>=&gt;</c> are drawn as one symbol where the font has one.</summary>
    public bool Ligatures { get; init; } = true;

    /// <summary>Gets the width of a tab and of one indentation level, in spaces.</summary>
    public int TabSize { get; init; } = 4;

    /// <summary>Gets whether Tab and automatic indentation insert spaces rather than tabs.</summary>
    public bool InsertSpaces { get; init; } = true;

    public bool ShowLineNumbers { get; init; } = true;

    public bool ShowIndentGuides { get; init; } = true;

    public bool HighlightCurrentLine { get; init; } = true;

    /// <summary>Gets whether typing an opening bracket or quote inserts the closing one too.</summary>
    public bool AutoCloseBrackets { get; init; } = true;

    /// <summary>Gets whether spaces and tabs are drawn as faint dots and arrows.</summary>
    public bool ShowWhitespace { get; init; }

    /// <summary>Gets whether completions show while typing, rather than only on Ctrl+Space.</summary>
    public bool CompletionOnType { get; init; } = true;

    /// <summary>Gets whether inlay hints, such as parameter names, are drawn inside lines.</summary>
    public bool ShowInlayHints { get; init; } = true;

    /// <summary>Gets whether code lenses are drawn above lines.</summary>
    public bool ShowCodeLens { get; init; } = true;

    /// <summary>Gets the text one indentation level inserts.</summary>
    public string IndentUnit => InsertSpaces ? new string(' ', TabSize) : "\t";
}
