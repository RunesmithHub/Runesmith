namespace Runesmith.Sdk.Localization;

/// <summary>A language Runesmith can show.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
/// <param name="Tag">The BCP 47 language tag in its canonical case, such as <c>nl</c> or <c>pt-BR</c>.</param>
/// <param name="Name">The language's name in itself, such as Nederlands.</param>
/// <param name="EnglishName">The language's name in English, such as Dutch.</param>
/// <param name="Coverage">The share of Runesmith's messages the language translates, from 0 to 1; 1 for English.</param>
public sealed record LanguageInfo(string Tag, string Name, string EnglishName, double Coverage);
