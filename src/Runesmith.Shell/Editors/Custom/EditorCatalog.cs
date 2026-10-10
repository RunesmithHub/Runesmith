using Runesmith.Sdk.Editors;

namespace Runesmith.Shell.Editors.Custom;

/// <summary>A custom editor provider and whether it ships with Runesmith.</summary>
internal sealed record EditorEntry(EditorProviderDefinition Definition, bool IsBuiltIn, IEditorProvider Provider);

/// <summary>Works out which editors can open a file and which one opens it by default.</summary>
internal sealed class EditorCatalog(IReadOnlyList<EditorEntry> providers, EditorAssociations associations)
{
    public const string TextEditorName = "Text Editor";

    public IReadOnlyList<EditorEntry> Providers { get; } = providers;

    public EditorAssociations Associations { get; } = associations;

    public EditorEntry? Find(string editorId) => Providers.FirstOrDefault(p => p.Definition.Id == editorId);

    /// <summary>Gets the custom editors whose patterns match the file, plugins' first, each once.</summary>
    public IReadOnlyList<EditorEntry> Matching(string filePath)
    {
        var name = Path.GetFileName(filePath);
        return [.. Providers.Where(p => p.Definition.FilePatterns.Any(pattern => FilePatterns.Matches(pattern, name)))
            .DistinctBy(p => p.Definition.Id)
            .OrderBy(p => p.IsBuiltIn)
            .ThenBy(p => p.Definition.Id, StringComparer.Ordinal)];
    }

    /// <summary>Gets the id of the editor a file opens in: the user's choice for its pattern, else the first custom editor that is the
    /// default for it, else the text editor.</summary>
    public string DefaultFor(string filePath)
    {
        var matching = Matching(filePath);
        if (Associations.Get(FilePatterns.KeyOf(filePath)) is { } chosen
            && (chosen == EditorProviderDefinition.TextEditorId || matching.Any(p => p.Definition.Id == chosen)))
        {
            return chosen;
        }

        return matching.FirstOrDefault(p => p.Definition.Priority == EditorPriority.Default)?.Definition.Id ?? EditorProviderDefinition.TextEditorId;
    }

    public IReadOnlyList<EditorChoice> GetChoices(string filePath)
    {
        var chosen = DefaultFor(filePath);
        return
        [
            new EditorChoice(EditorProviderDefinition.TextEditorId, TextEditorName, chosen == EditorProviderDefinition.TextEditorId),
            .. Matching(filePath).Select(p => new EditorChoice(p.Definition.Id, p.Definition.Name, chosen == p.Definition.Id)),
        ];
    }
}
