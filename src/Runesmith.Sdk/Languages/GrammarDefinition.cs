namespace Runesmith.Sdk.Languages;

/// <summary>A TextMate grammar a plugin brings. Runesmith has none of its own: a language is highlighted by the grammar a plugin exports for
/// its <see cref="LanguageDefinition.ScopeName"/>.</summary>
/// <remarks>Export one with <c>[Export(typeof(GrammarDefinition))]</c>. A grammar may include the grammars of other plugins by their scope,
/// as Markdown does for the code in its fences; where no plugin brings the scope, that part stays unhighlighted. When two plugins bring the
/// same scope, the first one loaded keeps it.</remarks>
/// <param name="ScopeName">The grammar's scope, such as <c>source.toml</c>; a <see cref="LanguageDefinition.ScopeName"/> refers to it.</param>
/// <param name="FilePath">The full path of the grammar file, in JSON or plist format.</param>
public sealed record GrammarDefinition(string ScopeName, string FilePath);
