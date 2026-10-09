using System.Composition;

namespace Runesmith.Sdk.Languages;

/// <summary>Names the languages a language feature serves, such as a completion provider for <c>csharp</c>; <c>*</c> means every language.</summary>
/// <remarks>Runesmith reads it without creating the provider, so a provider loads only when a document of one of its languages needs it.</remarks>
[MetadataAttribute]
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class LanguagesAttribute(params string[] languageIds) : Attribute
{
    /// <summary>The language that stands for every language.</summary>
    public const string Any = "*";

    public string[] LanguageIds { get; } = languageIds;
}

/// <summary>The metadata of an export marked with <see cref="LanguagesAttribute"/>.</summary>
public sealed class LanguageMetadata
{
    public string[] LanguageIds { get; set; } = [];

    /// <summary>Whether the feature serves the language.</summary>
    public bool Serves(string languageId) =>
        LanguageIds.Any(id => id == LanguagesAttribute.Any || string.Equals(id, languageId, StringComparison.OrdinalIgnoreCase));
}
