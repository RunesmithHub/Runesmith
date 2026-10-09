using Runesmith.Text;

namespace Runesmith.LanguageServices;

public sealed partial class LanguageServiceHost
{
    /// <summary>Gets a version of an open document that is still kept, or null.</summary>
    public SourceDocument? GetDocument(string path, int version) => documents.TryGetValue(path, out var history) ? history.Find(version) : null;

    /// <summary>Gets the quick fixes, and with <paramref name="includeRefactorings"/> the refactorings, for a span of a document.</summary>
    public Task<IReadOnlyList<CodeActionEntry>> CodeActionsAsync(string path, TextSnapshot snapshot, TextSpan span, bool includeRefactorings,
        CancellationToken cancellationToken) =>
        Request<IReadOnlyList<CodeActionEntry>>(path, snapshot, "codeActions", RequestLane.Navigation, [], async (analyzer, document) =>
        {
            if (analyzer is not ICodeActionAnalyzer actions || !ReferenceEquals(document.Snapshot, snapshot))
                return [];

            var entries = await actions.CodeActionsAsync(document, span, includeRefactorings, cancellationToken).ConfigureAwait(false);
            foreach (var entry in entries)
                entry.Analyzer = analyzer;
            return entries;
        }, cancellationToken);

    /// <summary>Computes the edits of a code action.</summary>
    public async Task<IReadOnlyList<FileEdit>> ResolveCodeActionAsync(CodeActionEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Analyzer is not ICodeActionAnalyzer actions)
            return [];

        var analyzer = entry.Analyzer;
        return await RunAsync<IReadOnlyList<FileEdit>>(analyzer, analyzer.LanguageIds[0], "resolveCodeAction", RequestLane.Navigation, [],
            async () => await actions.ResolveCodeActionAsync(entry, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Finds the name to rename at an offset; null when the analyzer does not rename or finds none.</summary>
    public Task<RenameSite?> PrepareRenameAsync(string path, TextSnapshot snapshot, int offset, CancellationToken cancellationToken) =>
        Request<RenameSite?>(path, snapshot, "prepareRename", RequestLane.Navigation, null, async (analyzer, document) =>
            analyzer is IRenameAnalyzer rename && ReferenceEquals(document.Snapshot, snapshot)
                ? await rename.PrepareRenameAsync(new DocumentPosition(document, offset), cancellationToken).ConfigureAwait(false)
                : null, cancellationToken);

    /// <summary>Computes the edits that rename the symbol at an offset; null when the analyzer does not rename.</summary>
    public Task<IReadOnlyList<FileEdit>?> RenameAsync(string path, TextSnapshot snapshot, int offset, string newName, CancellationToken cancellationToken) =>
        Request<IReadOnlyList<FileEdit>?>(path, snapshot, "rename", RequestLane.Navigation, null, async (analyzer, document) =>
            analyzer is IRenameAnalyzer rename && ReferenceEquals(document.Snapshot, snapshot)
                ? await rename.RenameAsync(new DocumentPosition(document, offset), newName, cancellationToken).ConfigureAwait(false)
                : null, cancellationToken);

    /// <summary>Gets the changes that format a document, or a span of it, in the snapshot's coordinates; null when the analyzer does not
    /// format.</summary>
    public Task<IReadOnlyList<TextChange>?> FormatAsync(string path, TextSnapshot snapshot, TextSpan? span, int tabSize, bool insertSpaces,
        CancellationToken cancellationToken) =>
        Request<IReadOnlyList<TextChange>?>(path, snapshot, "format", RequestLane.Navigation, null, async (analyzer, document) =>
            analyzer is IFormattingAnalyzer formatting && ReferenceEquals(document.Snapshot, snapshot)
                ? await formatting.FormatAsync(document, span, tabSize, insertSpaces, cancellationToken).ConfigureAwait(false)
                : null, cancellationToken);

    /// <summary>Gets the inlay hints of a document, in the background lane; empty when the analyzer shows none.</summary>
    public Task<IReadOnlyList<InlayHintEntry>> InlayHintsAsync(string path, TextSnapshot snapshot, CancellationToken cancellationToken) =>
        Request<IReadOnlyList<InlayHintEntry>>(path, snapshot, "inlayHints", RequestLane.Background, [], async (analyzer, document) =>
            analyzer is IInlayHintAnalyzer hints && ReferenceEquals(document.Snapshot, snapshot)
                ? await hints.InlayHintsAsync(document, cancellationToken).ConfigureAwait(false)
                : [], cancellationToken);
}
