namespace Runesmith.Sdk.Languages;

/// <summary>A TextMate grammar a plugin brings, for a language whose grammar Runesmith does not have.</summary>
/// <remarks>Export one with <c>[Export(typeof(GrammarDefinition))]</c>. Runesmith already ships the grammars of the common languages.</remarks>
/// <param name="ScopeName">The grammar's scope, such as <c>source.toml</c>; a <see cref="LanguageDefinition.ScopeName"/> refers to it.</param>
/// <param name="FilePath">The full path of the grammar file, in JSON or plist format.</param>
public sealed record GrammarDefinition(string ScopeName, string FilePath);
