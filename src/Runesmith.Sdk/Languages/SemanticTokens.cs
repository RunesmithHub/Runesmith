using Runesmith.Sdk.Documents;
using Runesmith.Text;

namespace Runesmith.Sdk.Languages;

/// <summary>The standard semantic token types, the same as the language server protocol's. The editor colors each through the syntax color
/// scheme's TextMate scopes; a type it does not know keeps the TextMate color.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
public static class SemanticTokenTypes
{
    public const string Namespace = "namespace";

    /// <summary>A type that is none of the more specific kinds below, such as a type alias.</summary>
    public const string Type = "type";

    public const string Class = "class";
    public const string Enum = "enum";
    public const string Interface = "interface";
    public const string Struct = "struct";
    public const string TypeParameter = "typeParameter";
    public const string Parameter = "parameter";
    public const string Variable = "variable";
    public const string Property = "property";
    public const string EnumMember = "enumMember";
    public const string Event = "event";
    public const string Function = "function";
    public const string Method = "method";
    public const string Macro = "macro";
    public const string Keyword = "keyword";
    public const string Modifier = "modifier";
    public const string Comment = "comment";
    public const string String = "string";
    public const string Number = "number";
    public const string Regexp = "regexp";
    public const string Operator = "operator";
    public const string Decorator = "decorator";
    public const string Label = "label";

    /// <summary>Gets every standard type.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Namespace, Type, Class, Enum, Interface, Struct, TypeParameter, Parameter, Variable, Property, EnumMember, Event, Function, Method, Macro,
        Keyword, Modifier, Comment, String, Number, Regexp, Operator, Decorator, Label,
    ];
}

/// <summary>The standard semantic token modifiers, the same as the language server protocol's.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
[Flags]
public enum SemanticTokenModifiers
{
    None = 0,

    /// <summary>The token declares the symbol.</summary>
    Declaration = 1 << 0,

    /// <summary>The token defines the symbol, such as a function with its body.</summary>
    Definition = 1 << 1,

    /// <summary>The symbol cannot be assigned, such as a constant or a read-only field.</summary>
    Readonly = 1 << 2,

    Static = 1 << 3,

    /// <summary>The symbol is deprecated; the editor strikes it through.</summary>
    Deprecated = 1 << 4,

    Abstract = 1 << 5,
    Async = 1 << 6,

    /// <summary>The token assigns the symbol a new value.</summary>
    Modification = 1 << 7,

    /// <summary>The token is part of documentation, such as a parameter name in a doc comment.</summary>
    Documentation = 1 << 8,

    /// <summary>The symbol comes from the language's standard library.</summary>
    DefaultLibrary = 1 << 9,
}

/// <summary>A token whose meaning the language knows, such as a name that refers to a parameter, drawn over the TextMate highlighting.</summary>
/// <param name="Span">The token's span in the request's snapshot, within one line.</param>
/// <param name="Type">One of <see cref="SemanticTokenTypes"/>.</param>
/// <remarks>Added in plugin API 0.1.2.</remarks>
public readonly record struct SemanticToken(TextSpan Span, string Type, SemanticTokenModifiers Modifiers = SemanticTokenModifiers.None);

/// <summary>Classifies the tokens of a document by meaning, which the editor colors over the TextMate highlighting. Export it with
/// <c>[Export(typeof(ISemanticTokensProvider))]</c> and <see cref="LanguagesAttribute"/>.</summary>
/// <remarks>The first provider that answers is used. Runesmith asks when a document opens and a moment after typing stops; until the answer
/// comes, the previous tokens follow the edits, so the colors do not flicker. Added in plugin API 0.1.2.</remarks>
public interface ISemanticTokensProvider
{
    /// <summary>Gets the tokens of the whole document, sorted by where they start; null when this provider does not know the document, so the
    /// next provider is asked.</summary>
    Task<IReadOnlyList<SemanticToken>?> GetSemanticTokensAsync(IDocument document, TextSnapshot snapshot, CancellationToken cancellationToken);

    /// <summary>Gets the tokens that touch a span, such as the lines on screen of a long document, which Runesmith asks for first; by default
    /// the tokens of the whole document that touch it.</summary>
    async Task<IReadOnlyList<SemanticToken>?> GetSemanticTokensAsync(IDocument document, TextSnapshot snapshot, TextSpan span, CancellationToken cancellationToken)
    {
        var tokens = await GetSemanticTokensAsync(document, snapshot, cancellationToken).ConfigureAwait(false);
        return tokens?.Where(token => token.Span.IntersectsWith(span)).ToList();
    }

    /// <summary>Raised, on any thread, when the provider's tokens change for a reason other than an edit, such as a project finishing loading,
    /// so open editors ask again.</summary>
    event EventHandler<LanguageFeatureChangedEventArgs>? Changed
    {
        add { }
        remove { }
    }
}
