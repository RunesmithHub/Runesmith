using Runesmith.Sdk.Documents;
using Runesmith.Text;

namespace Runesmith.Sdk.Languages;

/// <summary>Why completion was asked for.</summary>
public enum CompletionTrigger
{
    /// <summary>The user asked, such as with Ctrl+Space.</summary>
    Invoked,

    /// <summary>The user typed a trigger character, such as a dot.</summary>
    Character,

    /// <summary>The user kept typing while an incomplete list was shown.</summary>
    Incomplete,
}

/// <summary>What kind of thing a completion item inserts; it picks the item's icon.</summary>
public enum CompletionItemKind
{
    Text, Method, Function, Constructor, Field, Variable, Class, Interface, Module, Property, Unit, Value, Enum, Keyword, Snippet, Color, File,
    Reference, Folder, EnumMember, Constant, Struct, Event, Operator, TypeParameter,
}

/// <summary>A request for completion at an offset of a document's text.</summary>
/// <param name="Snapshot">The text the offset refers to.</param>
/// <param name="TriggerCharacter">The character typed when <paramref name="Trigger"/> is <see cref="CompletionTrigger.Character"/>.</param>
public sealed record CompletionRequest(IDocument Document, TextSnapshot Snapshot, int Offset, CompletionTrigger Trigger, char? TriggerCharacter = null);

/// <summary>One suggestion.</summary>
/// <param name="Label">The text shown in the list and, unless <see cref="InsertText"/> says otherwise, inserted.</param>
public sealed record CompletionItem(string Label, CompletionItemKind Kind)
{
    /// <summary>Gets a short description shown beside the label, such as a method's signature.</summary>
    public string? Detail { get; init; }

    /// <summary>Gets the item's documentation as Markdown.</summary>
    public string? Documentation { get; init; }

    /// <summary>Gets the text inserted, when it differs from the label.</summary>
    public string? InsertText { get; init; }

    /// <summary>Gets whether the inserted text is a snippet, with tab stops, placeholders and variables as <see cref="SnippetString"/>
    /// describes; the editor then moves between its tab stops after accepting the item.</summary>
    /// <remarks>Added in plugin API 0.1.2.</remarks>
    public bool IsSnippet { get; init; }

    /// <summary>Gets the span of the request's snapshot that the inserted text replaces; null replaces the word before the caret.</summary>
    public TextSpan? ReplaceSpan { get; init; }

    /// <summary>Gets the text the typed word is matched against, when it differs from the label.</summary>
    public string? FilterText { get; init; }

    /// <summary>Gets the text items are sorted by, when it differs from the label.</summary>
    public string? SortText { get; init; }

    /// <summary>Gets characters that accept the item when typed and are then inserted too, such as <c>(</c>.</summary>
    public IReadOnlyList<char> CommitCharacters { get; init; } = [];

    /// <summary>Gets other edits made when the item is accepted, such as a using directive, in the request's snapshot.</summary>
    public IReadOnlyList<TextChange> AdditionalChanges { get; init; } = [];

    /// <summary>Gets whether the list selects this item first.</summary>
    public bool IsPreselected { get; init; }

    /// <summary>Gets data the provider that made the item needs to resolve it.</summary>
    public object? Data { get; init; }

    /// <summary>Gets the provider that made the item, which resolves it; set by Runesmith.</summary>
    public ICompletionProvider? Provider { get; init; }
}

/// <summary>The suggestions for a request.</summary>
/// <param name="IsIncomplete">Whether typing more should ask again rather than only filter this list.</param>
public sealed record CompletionList(IReadOnlyList<CompletionItem> Items, bool IsIncomplete = false)
{
    public static CompletionList Empty { get; } = new([]);
}

/// <summary>Suggests completions. Export it with <c>[Export(typeof(ICompletionProvider))]</c> and <see cref="LanguagesAttribute"/>.</summary>
/// <remarks>Several providers can serve a language; their lists are merged. A provider that is slow does not hold up the editor.</remarks>
public interface ICompletionProvider
{
    /// <summary>Whether typing <paramref name="character"/> in the document should ask this provider for completions.</summary>
    bool IsTriggerCharacter(IDocument document, char character);

    Task<CompletionList> GetCompletionsAsync(CompletionRequest request, CancellationToken cancellationToken);

    /// <summary>Fills in details of an item that were left out for speed, such as its documentation, when the item is selected.</summary>
    Task<CompletionItem> ResolveAsync(CompletionItem item, CancellationToken cancellationToken) => Task.FromResult(item);
}
