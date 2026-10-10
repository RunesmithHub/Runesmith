using Runesmith.Sdk.Documents;
using Runesmith.Text;

namespace Runesmith.Sdk.Languages;

/// <summary>Finds every place a symbol is used, for Find References. Export it with <c>[Export(typeof(IReferenceProvider))]</c> and
/// <see cref="LanguagesAttribute"/>.</summary>
/// <remarks>Several providers can serve a language; their answers are merged. Requests come from a background thread and are cancelled when
/// the user asks again. Added in plugin API 0.1.2.</remarks>
public interface IReferenceProvider
{
    /// <summary>Gets the places the symbol at an offset is used, each the span of the name.</summary>
    /// <param name="includeDeclaration">Whether the symbol's own declaration belongs in the answer too.</param>
    Task<IReadOnlyList<DocumentLocation>> GetReferencesAsync(IDocument document, TextSnapshot snapshot, int offset, bool includeDeclaration,
        CancellationToken cancellationToken);
}

/// <summary>Finds the implementations of an interface or abstract member, or the overrides of a virtual one, for Go to Implementation.
/// Export it with <c>[Export(typeof(IImplementationProvider))]</c> and <see cref="LanguagesAttribute"/>.</summary>
/// <remarks>Several providers can serve a language; their answers are merged. Added in plugin API 0.1.2.</remarks>
public interface IImplementationProvider
{
    Task<IReadOnlyList<DocumentLocation>> GetImplementationsAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken);
}

/// <summary>How a highlighted occurrence uses its symbol, which picks its color.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
public enum DocumentHighlightKind
{
    /// <summary>A textual match, or a use that is neither a read nor a write.</summary>
    Text,

    /// <summary>The symbol's value is read.</summary>
    Read,

    /// <summary>The symbol is assigned or declared.</summary>
    Write,
}

/// <summary>An occurrence of the symbol under the caret in the same document.</summary>
/// <param name="Span">The span of the occurrence in the request's snapshot.</param>
/// <remarks>Added in plugin API 0.1.2.</remarks>
public sealed record DocumentHighlight(TextSpan Span, DocumentHighlightKind Kind = DocumentHighlightKind.Text);

/// <summary>Finds the other occurrences of the symbol under the caret, which the editor highlights. Export it with
/// <c>[Export(typeof(IDocumentHighlightProvider))]</c> and <see cref="LanguagesAttribute"/>.</summary>
/// <remarks>The first provider that answers is used. The editor asks a moment after the caret stops, and cancels the request when it moves
/// on. Added in plugin API 0.1.2.</remarks>
public interface IDocumentHighlightProvider
{
    /// <summary>Gets the occurrences; null when this provider does not know the document, so the next provider is asked.</summary>
    Task<IReadOnlyList<DocumentHighlight>?> GetDocumentHighlightsAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken);
}
