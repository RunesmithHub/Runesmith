using System.Composition;
using Runesmith.Sdk.Languages;

namespace Runesmith.Java;

/// <summary>The Java language: its files, highlighting, comments and brackets.</summary>
public sealed class JavaLanguage
{
    /// <summary>The language id analyzers and settings use.</summary>
    public const string Id = "java";

    [Export(typeof(LanguageDefinition))]
    public LanguageDefinition Definition { get; } = new(Id, "Java")
    {
        Extensions = [".java"],
        ScopeName = "source.java",
        LineComment = "//",
        BlockComment = ("/*", "*/"),
        Brackets = [('(', ')'), ('[', ']'), ('{', '}')],
        Quotes = ['"', '\''],
        Icon = "file-code",
    };
}
