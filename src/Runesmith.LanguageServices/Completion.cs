using Runesmith.Text;

namespace Runesmith.LanguageServices;

/// <summary>Why completion was asked for.</summary>
public enum CompletionTriggerKind
{
    /// <summary>The user asked, or started typing a word.</summary>
    Invoked,

    /// <summary>The user typed a trigger character, such as <c>.</c>.</summary>
    Character,

    /// <summary>The user kept typing a word whose list was cut.</summary>
    Incomplete,
}

/// <summary>What a completion suggestion inserts; it picks the icon, and analyzers sort by it.</summary>
public enum CompletionKind : byte
{
    Text, Method, Function, Constructor, Field, Variable, Class, Interface, Module, Property, Unit, Value, Enum, Keyword, Snippet, Color, File,
    Reference, Folder, EnumMember, Constant, Struct, Event, Operator, TypeParameter,
}

/// <summary>A request for completions at a position.</summary>
/// <param name="Character">The typed trigger character, when <paramref name="Trigger"/> is <see cref="CompletionTriggerKind.Character"/>.</param>
public sealed record CompletionQuery(SourceDocument Document, int Offset, CompletionTriggerKind Trigger, char? Character = null);

/// <summary>A suggestion as an analyzer produces it: small, and with its details left for <see cref="ILanguageAnalyzer.ResolveAsync"/>.</summary>
/// <param name="Label">What the list shows and, unless <see cref="InsertText"/> says otherwise, what is inserted.</param>
/// <param name="SortGroup">Lower groups sort first among equally good matches, such as locals before members before types before keywords.</param>
/// <param name="Data">What the analyzer needs to resolve the suggestion later, such as its symbol.</param>
public readonly record struct CompletionCandidate(
    string Label,
    CompletionKind Kind,
    byte SortGroup = 0,
    string? Detail = null,
    string? InsertText = null,
    string? FilterText = null,
    object? Data = null);

/// <summary>A suggestion in a completion result, ranked.</summary>
public sealed class CompletionEntry
{
    internal CompletionEntry(in CompletionCandidate candidate, int score)
    {
        Label = candidate.Label;
        Kind = candidate.Kind;
        SortGroup = candidate.SortGroup;
        Detail = candidate.Detail;
        InsertText = candidate.InsertText;
        FilterText = candidate.FilterText;
        Data = candidate.Data;
        Score = score;
    }

    public string Label { get; }

    public CompletionKind Kind { get; }

    public byte SortGroup { get; }

    /// <summary>Gets a short description shown beside the label, such as a type or a signature.</summary>
    public string? Detail { get; }

    public string? InsertText { get; }

    public string? FilterText { get; }

    public object? Data { get; }

    /// <summary>Gets how well the suggestion matches the typed word; higher is better.</summary>
    public int Score { get; }

    /// <summary>Gets the analyzer that made the suggestion, which resolves it.</summary>
    public ILanguageAnalyzer? Analyzer { get; internal set; }
}

/// <summary>The details of a suggestion, computed when it is selected.</summary>
/// <param name="AdditionalChanges">Other edits accepting the suggestion makes, such as adding an import, in the version it was computed for.</param>
public sealed record CompletionDetails(string? Detail, string? Documentation, IReadOnlyList<TextChange> AdditionalChanges);

/// <summary>A ranked completion list.</summary>
/// <param name="ReplaceSpan">The span of the word the suggestions replace, in the version they were computed for.</param>
/// <param name="IsIncomplete">Whether the list was cut, so typing more should ask again.</param>
/// <param name="Version">The document version the list was computed for.</param>
public sealed record CompletionResult(IReadOnlyList<CompletionEntry> Items, TextSpan ReplaceSpan, bool IsIncomplete, int Version)
{
    public static CompletionResult Empty(int version, int offset) => new([], new TextSpan(offset, 0), false, version);
}
