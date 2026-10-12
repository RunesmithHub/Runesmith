using System.Composition;
using Runesmith.Sdk.Languages;

namespace Runesmith.Languages;

/// <summary>Knows plain text and the languages plugins add. Runesmith brings no language of its own: every other one comes from a plugin.</summary>
[Export(typeof(ILanguageRegistry))]
[Shared]
public sealed class LanguageRegistry : ILanguageRegistry
{
    private readonly Dictionary<string, LanguageDefinition> byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LanguageDefinition> byFileName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LanguageDefinition> byExtension = new(StringComparer.OrdinalIgnoreCase);

    [ImportingConstructor]
    public LanguageRegistry([ImportMany] IEnumerable<LanguageDefinition> pluginLanguages)
    {
        ArgumentNullException.ThrowIfNull(pluginLanguages);
        PlainText = new LanguageDefinition(ILanguageRegistry.PlainText, "Plain Text") { Extensions = [".txt", ".text"], Icon = "file-text" };
        byId[PlainText.Id] = PlainText;
        foreach (var language in pluginLanguages)
            byId[language.Id] = language;

        Languages = [.. byId.Values.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)];
        foreach (var language in Languages)
        {
            foreach (var name in language.FileNames)
                byFileName[name] = language;
            foreach (var extension in language.Extensions)
                byExtension[extension] = language;
        }
    }

    public IReadOnlyList<LanguageDefinition> Languages { get; }

    private LanguageDefinition PlainText { get; }

    public LanguageDefinition? Find(string languageId) => byId.GetValueOrDefault(languageId);

    public LanguageDefinition GetLanguageForFile(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        var name = Path.GetFileName(filePath);
        if (byFileName.TryGetValue(name, out var language))
            return language;

        // The longest extension wins, so "App.csproj.user" is found before ".user"; a dotfile such as ".bashrc" is its own extension.
        for (var dot = name.IndexOf('.', StringComparison.Ordinal); dot >= 0; dot = name.IndexOf('.', dot + 1))
        {
            if (byExtension.TryGetValue(name[dot..], out language))
                return language;
        }

        return PlainText;
    }
}
