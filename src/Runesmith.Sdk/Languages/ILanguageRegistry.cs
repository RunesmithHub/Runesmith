namespace Runesmith.Sdk.Languages;

/// <summary>Knows every language and which one a file belongs to.</summary>
public interface ILanguageRegistry
{
    /// <summary>The id of plain text, the language of files no other language claims.</summary>
    public const string PlainText = "plaintext";

    IReadOnlyList<LanguageDefinition> Languages { get; }

    /// <summary>Finds a language by id, or returns null.</summary>
    LanguageDefinition? Find(string languageId);

    /// <summary>Gets the language of a file by its name or extension, or plain text.</summary>
    LanguageDefinition GetLanguageForFile(string filePath);
}
