namespace Runesmith.Sdk.Languages;

/// <summary>Describes a language: which files belong to it, how it is highlighted and how its comments and brackets look.</summary>
/// <remarks>Export one with <c>[Export(typeof(LanguageDefinition))]</c> on a property of an exported class.</remarks>
/// <param name="Id">A unique id, such as <c>csharp</c>; it is also the language id language servers receive.</param>
/// <param name="Name">The name shown to the user, such as "C#".</param>
public sealed record LanguageDefinition(string Id, string Name)
{
    /// <summary>Gets the file extensions, with the dot, such as <c>.cs</c>.</summary>
    public IReadOnlyList<string> Extensions { get; init; } = [];

    /// <summary>Gets whole file names that belong to the language whatever their extension, such as <c>Dockerfile</c>.</summary>
    public IReadOnlyList<string> FileNames { get; init; } = [];

    /// <summary>Gets the TextMate scope of the language's grammar, such as <c>source.cs</c>, or null for no highlighting.</summary>
    public string? ScopeName { get; init; }

    /// <summary>Gets the token that starts a line comment, such as <c>//</c>.</summary>
    public string? LineComment { get; init; }

    /// <summary>Gets the tokens that open and close a block comment, such as <c>/*</c> and <c>*/</c>.</summary>
    public (string Open, string Close)? BlockComment { get; init; }

    /// <summary>Gets the bracket pairs the editor matches and closes as you type.</summary>
    public IReadOnlyList<(char Open, char Close)> Brackets { get; init; } = [('(', ')'), ('[', ']'), ('{', '}')];

    /// <summary>Gets the pairs of quotes the editor closes as you type.</summary>
    public IReadOnlyList<char> Quotes { get; init; } = ['"', '\''];

    /// <summary>Gets the name of the icon of the language's files, such as <c>file-code</c>.</summary>
    public string Icon { get; init; } = "file";
}
