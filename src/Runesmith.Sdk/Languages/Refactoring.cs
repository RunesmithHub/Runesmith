using Runesmith.Sdk.Documents;
using Runesmith.Text;

namespace Runesmith.Sdk.Languages;

/// <summary>The symbol a rename starts from.</summary>
/// <param name="Span">The span of the name in the request's snapshot, which the rename box covers.</param>
/// <param name="Name">The name the rename box starts with.</param>
public sealed record RenameTarget(TextSpan Span, string Name);

/// <summary>Renames symbols everywhere they are used. Export it with <c>[Export(typeof(IRenameProvider))]</c> and
/// <see cref="LanguagesAttribute"/>.</summary>
/// <remarks>The first provider that answers is used. Throw <see cref="LanguageFeatureException"/> to tell the user why a symbol cannot be
/// renamed, such as one defined in a library.</remarks>
public interface IRenameProvider
{
    /// <summary>Finds the symbol at an offset; null when this provider cannot rename there, so the next provider is asked.</summary>
    Task<RenameTarget?> PrepareRenameAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken);

    /// <summary>Computes the edit that renames the symbol at an offset; null when this provider cannot rename there.</summary>
    Task<WorkspaceEdit?> RenameAsync(IDocument document, TextSnapshot snapshot, int offset, string newName, CancellationToken cancellationToken);
}

/// <summary>How formatted code is indented, from the editor's settings.</summary>
/// <param name="TabSize">The width of a tab and of one indentation level, in spaces.</param>
/// <param name="InsertSpaces">Whether indentation is spaces rather than tabs.</param>
public sealed record FormattingOptions(int TabSize, bool InsertSpaces);

/// <summary>Formats whole documents, for Format Document and format on save. Export it with
/// <c>[Export(typeof(IDocumentFormattingProvider))]</c> and <see cref="LanguagesAttribute"/>.</summary>
/// <remarks>The first provider that answers is used. A provider that starts a formatter program needs the <c>process</c> capability.</remarks>
public interface IDocumentFormattingProvider
{
    /// <summary>Gets the changes that format the snapshot, in its coordinates; empty when it is formatted already, null when this provider does
    /// not format the document, so the next provider is asked.</summary>
    Task<IReadOnlyList<TextChange>?> FormatDocumentAsync(IDocument document, TextSnapshot snapshot, FormattingOptions options, CancellationToken cancellationToken);
}

/// <summary>Formats a span of a document, for Format Selection. Export it with <c>[Export(typeof(IRangeFormattingProvider))]</c> and
/// <see cref="LanguagesAttribute"/>.</summary>
public interface IRangeFormattingProvider
{
    /// <summary>Gets the changes that format the span; empty when it is formatted already, null when this provider does not format the
    /// document.</summary>
    Task<IReadOnlyList<TextChange>?> FormatRangeAsync(IDocument document, TextSnapshot snapshot, TextSpan span, FormattingOptions options, CancellationToken cancellationToken);
}
