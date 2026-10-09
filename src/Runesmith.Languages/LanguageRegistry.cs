using System.Composition;
using Runesmith.Sdk.Languages;

namespace Runesmith.Languages;

/// <summary>Knows the built-in languages and the ones plugins add; a plugin's definition replaces a built-in one with the same id.</summary>
[Export(typeof(ILanguageRegistry))]
[Shared]
public sealed class LanguageRegistry : ILanguageRegistry
{
    private readonly Dictionary<string, LanguageDefinition> byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LanguageDefinition> byFileName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LanguageDefinition> byExtension = new(StringComparer.OrdinalIgnoreCase);

    [ImportingConstructor]
    public LanguageRegistry([ImportMany] IEnumerable<LanguageDefinition> pluginLanguages, TextMateGrammars grammars)
    {
        ArgumentNullException.ThrowIfNull(pluginLanguages);
        ArgumentNullException.ThrowIfNull(grammars);
        var plugins = pluginLanguages.ToList();
        foreach (var language in BuiltInLanguages.Create(grammars.BuiltIn).Concat(plugins))
            byId[language.Id] = language;

        Languages = [.. byId.Values.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)];
        PlainText = byId[ILanguageRegistry.PlainText];

        // Built-in languages are indexed first so that a plugin's language wins a shared extension or file name.
        foreach (var language in Languages.OrderBy(l => plugins.Contains(l) ? 1 : 0))
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
