using System.Composition;
using Avalonia.Threading;
using Runesmith.Editor;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Shell;
using Runesmith.Text;

namespace Runesmith.Shell.Editors;

/// <summary>Applies workspace edits, such as renames and code actions: opens the files that are not open in tabs that stay in the background,
/// checks every file first, then makes each file's changes one undo step of that file.</summary>
[Export(typeof(IWorkspaceEditService))]
[Shared]
[method: ImportingConstructor]
public sealed class WorkspaceEditService(IDocumentService documents, Lazy<IEditorService> editors, INotificationService notifications) : IWorkspaceEditService
{
    public async Task<bool> ApplyAsync(WorkspaceEdit edit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (!Dispatcher.UIThread.CheckAccess())
            return await Dispatcher.UIThread.InvokeAsync(() => ApplyAsync(edit, cancellationToken));

        var files = new List<(IDocument Document, IReadOnlyList<TextChange> Changes)>();
        foreach (var group in edit.Documents.Where(d => d.Changes.Count > 0).GroupBy(d => Path.GetFullPath(d.FilePath), PathComparer))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(group.Key);
            var document = documents.Find(group.Key) ?? (await editors.Value.OpenAsync(group.Key, activate: false))?.Document;
            if (document is null)
                return false;

            var problem = Check(document, group.ToList(), out var changes);
            if (problem is not null)
            {
                notifications.Notify(NotificationKind.Warning, "The edit was not applied", $"{name} {problem}");
                return false;
            }

            files.Add((document, changes));
        }

        foreach (var (document, changes) in files)
            Apply(document, changes);
        return true;
    }

    // Returns what keeps the changes from applying to the document, or null when they apply.
    private static string? Check(IDocument document, IReadOnlyList<DocumentEdit> edits, out IReadOnlyList<TextChange> changes)
    {
        changes = [.. edits.SelectMany(e => e.Changes).OrderBy(c => c.Span.Start).Select(c => c with { NewText = LineEndings.Normalize(c.NewText) })];
        var snapshot = document.Buffer.Current;
        if (document.IsReadOnly)
            return "is read-only.";
        if (edits.Any(e => e.Snapshot is { } basis && !ReferenceEquals(basis, snapshot)))
            return "changed while the edit was computed. Try again.";
        for (var i = 0; i < changes.Count; i++)
        {
            if (changes[i].Span.End > snapshot.Length)
                return "is shorter than the edit expects. Try again.";
            if (i > 0 && changes[i].Span.Start < changes[i - 1].Span.End)
                return "has overlapping changes in the edit.";
        }

        return null;
    }

    private void Apply(IDocument document, IReadOnlyList<TextChange> changes)
    {
        if (editors.Value.Editors.OfType<TextEditor>().FirstOrDefault(e => ReferenceEquals(e.Document, document)) is { } editor)
        {
            var selection = editor.Area.Selection;
            editor.Area.ApplyChanges(changes, new EditorSelection(OffsetMapping.Map(changes, selection.Anchor), OffsetMapping.Map(changes, selection.Caret)));
            return;
        }

        var changeSet = document.Buffer.Apply(changes);
        document.History.Push(changeSet, stateBefore: null, stateAfter: null);
    }

    private static StringComparer PathComparer => OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
}
