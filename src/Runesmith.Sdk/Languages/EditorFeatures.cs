using Runesmith.Sdk.Documents;
using Runesmith.Text;

namespace Runesmith.Sdk.Languages;

/// <summary>The code actions, rename, formatting and decoration providers that serve a document, merged; the editor uses this rather than the
/// providers.</summary>
/// <remarks>A provider that throws is logged to the "Language features" output channel and left out. <see cref="LanguageFeatureException"/>
/// and cancellation pass through to the caller.</remarks>
public interface IEditorFeatures
{
    /// <summary>Gets the actions of every provider, preferred ones first.</summary>
    Task<IReadOnlyList<CodeAction>> GetCodeActionsAsync(CodeActionRequest request, CancellationToken cancellationToken);

    /// <summary>Asks the action's provider to fill in what it left out.</summary>
    Task<CodeAction> ResolveCodeActionAsync(CodeAction action, CancellationToken cancellationToken);

    /// <summary>Finds the symbol to rename at an offset, from the first provider that knows one; null when none does.</summary>
    Task<RenameTarget?> PrepareRenameAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken);

    /// <summary>Computes a rename with the first provider that can; null when none can.</summary>
    Task<WorkspaceEdit?> RenameAsync(IDocument document, TextSnapshot snapshot, int offset, string newName, CancellationToken cancellationToken);

    /// <summary>Formats a document with the first provider that does; null when none does.</summary>
    Task<IReadOnlyList<TextChange>?> FormatDocumentAsync(IDocument document, TextSnapshot snapshot, FormattingOptions options, CancellationToken cancellationToken);

    /// <summary>Formats a span with the first provider that does; null when none does.</summary>
    Task<IReadOnlyList<TextChange>?> FormatRangeAsync(IDocument document, TextSnapshot snapshot, TextSpan span, FormattingOptions options, CancellationToken cancellationToken);

    /// <summary>Gets the decoration providers that serve a document, each guarded so a failure is logged and answers nothing.</summary>
    IReadOnlyList<IDecorationProvider> GetDecorationProviders(IDocument document);
}
