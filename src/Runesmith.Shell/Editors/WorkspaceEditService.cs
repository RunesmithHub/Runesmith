using System.Composition;
using Avalonia.Threading;
using Runesmith.Composition;
using Runesmith.Editor;
using Runesmith.Sdk;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Messaging;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.Workspace;
using Runesmith.Shell.Editors.FileOperations;
using Runesmith.Text;
using Runesmith.Workspace.Documents;
using Runesmith.Workspace.Files;
using RunesmithHub.Protocol;

namespace Runesmith.Shell.Editors;

/// <summary>Applies workspace edits, such as renames and code actions: opens the files that are not open in tabs that stay in the background,
/// checks every file first, then makes the file operations and each file's changes one undo step of that file, and keeps the edit as one
/// step of the workspace's own undo history.</summary>
[Export(typeof(IWorkspaceEditService))]
[Export]
[Shared]
public sealed class WorkspaceEditService : IWorkspaceEditService, IFileActionHost, IDisposable
{
    private const int HistoryLimit = 20;
    private const int SummaryLines = 12;

    private readonly IDocumentService documents;
    private readonly Lazy<IEditorService> editors;
    private readonly INotificationService notifications;
    private readonly IWorkspace? workspace;
    private readonly IMessageBus? messages;
    private readonly IReadOnlyList<Lazy<IFileOperationParticipant>> participants;
    private readonly IDocumentLocations? locations;
    private readonly Func<PluginInfo?> caller;
    private readonly Action<string> log;
    private readonly Lazy<FileStash> stash;
    private readonly List<Step> undo = [];
    private readonly Stack<Step> redo = new();
    private bool busy;

    public WorkspaceEditService(IDocumentService documents, Lazy<IEditorService> editors, INotificationService notifications)
        : this(documents, editors, notifications, null, null, [], null, () => null, _ => { }, new Lazy<FileStash>(DefaultStash))
    {
    }

    [ImportingConstructor]
    public WorkspaceEditService(
        IDocumentService documents,
        Lazy<IEditorService> editors,
        INotificationService notifications,
        IWorkspace workspace,
        IMessageBus messages,
        IOutputService output,
        [ImportMany] IEnumerable<Lazy<IFileOperationParticipant>> participants,
        [Import(AllowDefault = true)] IDocumentLocations? locations = null)
        : this(documents, editors, notifications, workspace, messages, participants, locations, PluginCallers.Current,
            line => output.GetChannel(PluginAccess.ChannelName).AppendLine(line), new Lazy<FileStash>(DefaultStash))
    {
    }

    internal WorkspaceEditService(
        IDocumentService documents,
        Lazy<IEditorService> editors,
        INotificationService notifications,
        IWorkspace? workspace,
        IMessageBus? messages,
        IEnumerable<Lazy<IFileOperationParticipant>> participants,
        IDocumentLocations? locations,
        Func<PluginInfo?> caller,
        Action<string> log,
        Lazy<FileStash> stash)
    {
        this.documents = documents;
        this.editors = editors;
        this.notifications = notifications;
        this.workspace = workspace;
        this.messages = messages;
        this.participants = [.. participants];
        this.locations = locations;
        this.caller = caller;
        this.log = log;
        this.stash = stash;
    }

    /// <summary>Gets how long participants get, together, before and after the files change.</summary>
    internal TimeSpan ParticipantBudget { get; init; } = IFileOperationParticipant.TimeBudget;

    public bool CanUndo => undo.Count > 0;

    public bool CanRedo => redo.Count > 0;

    /// <summary>Gets the label of the edit <see cref="UndoAsync"/> would undo, or null.</summary>
    internal string? UndoLabel => undo.Count > 0 ? undo[^1].Label : null;

    /// <summary>Raised on the UI thread when <see cref="CanUndo"/> or <see cref="CanRedo"/> may have changed.</summary>
    public event EventHandler? HistoryChanged;

    FileStash IFileActionHost.Stash => stash.Value;

    public Task<bool> ApplyAsync(WorkspaceEdit edit, CancellationToken cancellationToken = default) =>
        ApplyAsync(edit, new WorkspaceEditOptions { Confirmation = WorkspaceEditConfirmation.Never }, caller(), cancellationToken);

    /// <exception cref="UnauthorizedAccessException">An operation's path is outside the open folder and the calling plugin did not declare
    /// the filesystem capability.</exception>
    public Task<bool> ApplyAsync(WorkspaceEdit edit, WorkspaceEditOptions options, CancellationToken cancellationToken = default) =>
        ApplyAsync(edit, options, caller(), cancellationToken);

    public async Task<bool> UndoAsync(CancellationToken cancellationToken = default)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return await Dispatcher.UIThread.InvokeAsync(() => UndoAsync(cancellationToken));
        if (busy)
            return false;

        busy = true;
        try
        {
            return await UndoStepAsync(cancellationToken);
        }
        finally
        {
            busy = false;
        }
    }

    private async Task<bool> UndoStepAsync(CancellationToken cancellationToken)
    {
        if (undo.Count == 0)
            return false;

        var step = undo[^1];
        foreach (var text in step.Texts)
        {
            text.Live = await LiveDocumentAsync(text);
            var current = text.Live?.Buffer.Current;
            if (current is null || (!ReferenceEquals(current, text.After) && current.GetText() != text.After.GetText()))
                return Refuse("The edit was not undone", $"{text.Document.Name} changed since the edit.");
        }

        if (step.Actions.Count > 0 && step.Actions[^1].CannotUndo() is { } problem)
            return Refuse("The edit was not undone", problem);

        cancellationToken.ThrowIfCancellationRequested();
        foreach (var text in step.Texts.AsEnumerable().Reverse())
        {
            var document = text.Live!;
            if (ReferenceEquals(document.Buffer.Current, text.After) && document.History.CanUndo)
            {
                if (EditorOf(document) is { } editor)
                    editor.Undo();
                else
                    document.History.Undo(document.Buffer);
            }
            else
            {
                Apply(document, Inverse(text.Changes, text.Before));
            }

            text.Undone = document.Buffer.Current;
        }

        await CloseAsync(step.Actions.OfType<CreateFileAction>().SelectMany(a => a.Paths));
        if (!Run(step.Actions.AsEnumerable().Reverse(), action => Checked(action.CannotUndo, action.Undo), action => action.Do(), out var failure))
        {
            foreach (var text in step.Texts)
                Reapply(text);
            return Refuse("The edit was not undone", failure);
        }

        undo.RemoveAt(undo.Count - 1);
        redo.Push(step);
        Finish(step, [.. step.Actions.AsEnumerable().Reverse().Select(a => a.Reverse)]);
        return true;
    }

    public async Task<bool> RedoAsync(CancellationToken cancellationToken = default)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return await Dispatcher.UIThread.InvokeAsync(() => RedoAsync(cancellationToken));
        if (busy)
            return false;

        busy = true;
        try
        {
            return await RedoStepAsync(cancellationToken);
        }
        finally
        {
            busy = false;
        }
    }

    private async Task<bool> RedoStepAsync(CancellationToken cancellationToken)
    {
        if (redo.Count == 0)
            return false;

        var step = redo.Peek();
        if (step.Actions.Count > 0 && step.Actions[0].CannotRedo() is { } problem)
            return Refuse("The edit was not redone", problem);

        cancellationToken.ThrowIfCancellationRequested();
        await CloseAsync(step.Actions.OfType<CreateFileAction>().Where(a => ((CreateFileOperation)a.Operation).Overwrite).SelectMany(a => a.Paths));
        if (!Run(step.Actions, action => Checked(action.CannotRedo, action.Do), action => action.Undo(), out var failure))
            return Refuse("The edit was not redone", failure);

        foreach (var text in step.Texts)
        {
            text.Live = await LiveDocumentAsync(text);
            if (text.Live is { } document && !Reapply(text))
                notifications.Notify(NotificationKind.Warning, "Part of the edit was not redone", $"{document.Name} changed since the edit was undone.");
        }

        redo.Pop();
        undo.Add(step);
        Finish(step, [.. step.Actions.Select(a => a.Operation)]);
        return true;
    }

    /// <summary>Undoes the last workspace edit when it made file operations and is the last change of the document, so undoing in the
    /// editor takes back the whole edit; returns whether it did.</summary>
    internal bool TryUndoWith(IDocument document)
    {
        if (busy || undo.Count == 0 || undo[^1] is not { Actions.Count: > 0 } step
            || !step.Texts.Any(t => ReferenceEquals(t.Live ?? t.Document, document) && ReferenceEquals(document.Buffer.Current, t.After)))
            return false;

        _ = UndoAsync();
        return true;
    }

    /// <summary>Redoes the last undone workspace edit when it made file operations and undoing it was the last change of the document.</summary>
    internal bool TryRedoWith(IDocument document)
    {
        if (busy || redo.Count == 0 || redo.Peek() is not { Actions.Count: > 0 } step
            || !step.Texts.Any(t => ReferenceEquals(t.Live ?? t.Document, document) && ReferenceEquals(document.Buffer.Current, t.Undone)))
            return false;

        _ = RedoAsync();
        return true;
    }

    public void Dispose()
    {
        foreach (var step in undo.Concat(redo))
            Discard(step);
        undo.Clear();
        redo.Clear();
    }

    void IFileActionHost.MoveDocuments(string from, string to) => locations?.Move(from, to);

    private async Task<bool> ApplyAsync(WorkspaceEdit edit, WorkspaceEditOptions options, PluginInfo? plugin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(options);
        if (!Dispatcher.UIThread.CheckAccess())
            return await Dispatcher.UIThread.InvokeAsync(() => ApplyAsync(edit, options, plugin, cancellationToken));

        var (plan, planProblem) = FileOperationPlan.Create(edit.FileOperations);
        if (plan is null)
            return Refuse("The edit was not applied", planProblem);
        DemandAccess(plan, plugin);

        var operations = plan.Operations;
        var documentEdits = edit.Documents.Where(d => d.Changes.Count > 0).ToList();
        if (operations.Count > 0)
            documentEdits.AddRange(await AskParticipantsAsync(operations));

        var groups = Resolve(plan, documentEdits, out var resolveProblem);
        if (resolveProblem is not null)
            return Refuse("The edit was not applied", resolveProblem);

        var targets = new List<(EditTarget Target, IDocument? Document, IReadOnlyList<TextChange> Changes)>();
        foreach (var (target, group) in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IDocument? document = null;
            string? problem;
            IReadOnlyList<TextChange> changes;
            if (target.Created is { } created)
            {
                problem = Check(TextSnapshot.Create(LineEndings.Normalize(created.Content)), isReadOnly: false, group, out changes);
            }
            else
            {
                document = documents.Find(target.CurrentPath!) ?? (await editors.Value.OpenAsync(target.CurrentPath!, activate: false))?.Document;
                if (document is null)
                    return false;
                problem = Check(document.Buffer.Current, document.IsReadOnly, group, out changes);
            }

            if (problem is not null)
                return Refuse("The edit was not applied", $"{Path.GetFileName(target.FinalPath)} {problem}");
            targets.Add((target, document, changes));
        }

        var closing = ToClose(plan, out var unsaved);
        if (unsaved is not null)
            return Refuse("The edit was not applied", $"{unsaved.Name} has unsaved changes.");

        var label = options.Label ?? Describe(operations, targets.Count);
        if (NeedsConfirmation(options, plan, targets.Count)
            && !await notifications.ConfirmAsync(label, Summarize(operations, targets.Select(t => (t.Target.FinalPath, t.Changes.Count))), "Apply"))
            return false;

        cancellationToken.ThrowIfCancellationRequested();
        await CloseAsync(closing);
        var actions = operations.Select(ToAction).ToList();
        if (!Run(actions, action => action.Do(), action => action.Undo(), out var failure))
            return Refuse("The edit was not applied", failure);

        var texts = new List<TextRecord>();
        foreach (var (target, found, changes) in targets)
        {
            var document = found ?? (await editors.Value.OpenAsync(target.FinalPath, activate: false))?.Document;
            if (document is null || Check(document.Buffer.Current, document.IsReadOnly, [new DocumentEdit(target.FinalPath, changes)], out _) is not null)
            {
                foreach (var text in texts.AsEnumerable().Reverse())
                    Apply(text.Document, Inverse(text.Changes, text.Before));
                Run(actions.AsEnumerable().Reverse(), action => action.Undo(), action => action.Do(), out _);
                return Refuse("The edit was not applied", $"{Path.GetFileName(target.FinalPath)} could not be opened.");
            }

            var before = document.Buffer.Current;
            Apply(document, changes);
            texts.Add(new TextRecord(document, changes, before) { After = document.Buffer.Current });
        }

        if (actions.Count == 0 && texts.Count == 0)
            return true;

        var step = new Step(label, actions, texts);
        foreach (var undone in redo)
            Discard(undone);
        redo.Clear();
        undo.Add(step);
        if (undo.Count > HistoryLimit)
        {
            Discard(undo[0]);
            undo.RemoveAt(0);
        }

        Finish(step, [.. actions.Select(a => a.Operation)]);
        if (actions.Count > 0)
            notifications.Notify(NotificationKind.Info, label, null, "Undo", () => _ = UndoIfLastAsync(step));
        return true;
    }

    private Task<bool> UndoIfLastAsync(Step step) => undo.Count > 0 && undo[^1] == step ? UndoAsync() : Task.FromResult(false);

    // Groups the edits by the file they end up in. Without file operations every path names the file as it is, open or not.
    private static List<(EditTarget Target, List<DocumentEdit> Edits)> Resolve(FileOperationPlan plan, List<DocumentEdit> edits, out string? problem)
    {
        problem = null;
        var groups = new Dictionary<string, (EditTarget Target, List<DocumentEdit> Edits)>(PathComparison.Comparer);
        foreach (var edit in edits)
        {
            var full = Path.GetFullPath(edit.FilePath);
            EditTarget? target;
            if (plan.Operations.Count == 0)
            {
                target = new EditTarget(full, full, null);
            }
            else
            {
                (target, problem) = plan.Resolve(full);
                if (target is null)
                    return [];
            }

            if (groups.TryGetValue(target.FinalPath, out var group))
                group.Edits.Add(edit);
            else
                groups[target.FinalPath] = (target, [edit]);
        }

        return [.. groups.Values];
    }

    private void DemandAccess(FileOperationPlan plan, PluginInfo? plugin)
    {
        if (plugin is null || plugin.Manifest.Allows(Capabilities.FileSystem))
            return;

        var root = workspace?.RootPath is { } open ? PathComparison.Normalize(open) : null;
        if (plan.Paths.Any(path => root is null || !PathComparison.IsInside(path, root) || PathComparison.Comparer.Equals(path, root)))
            PluginAccess.Demand(plugin, Capabilities.FileSystem, "workspace edit service for files outside the open folder", log);
    }

    // The open documents that the operations delete or replace; unsaved changes in any of them stop the edit.
    private List<IDocument> ToClose(FileOperationPlan plan, out IDocument? unsaved)
    {
        var gone = plan.Operations.SelectMany(operation => operation switch
        {
            DeleteFileOperation delete => [delete.Path],
            CreateFileOperation { Overwrite: true } create => [create.FilePath],
            RenameFileOperation { Overwrite: true } rename when !PathComparison.Comparer.Equals(rename.OldPath, rename.NewPath) => new[] { rename.NewPath },
            _ => [],
        }).ToList();
        var closing = documents.Documents.Where(d => d.FilePath is { } path && gone.Any(g => PathComparison.IsInside(path, g))).ToList();
        unsaved = closing.FirstOrDefault(d => d.IsModified);
        return closing;
    }

    private async Task CloseAsync(IEnumerable<string> paths)
    {
        var list = paths.ToList();
        await CloseAsync(documents.Documents.Where(d => d.FilePath is { } path && list.Any(p => PathComparison.IsInside(path, p))).ToList());
    }

    private async Task CloseAsync(IReadOnlyList<IDocument> closing)
    {
        foreach (var document in closing)
        {
            if (editors.Value.Editors.FirstOrDefault(e => ReferenceEquals(e.Document, document)) is { } view)
                await editors.Value.CloseAsync(view);
            else if (documents.Documents.Contains(document))
                documents.Close(document);
        }
    }

    private FileAction ToAction(FileOperation operation) => operation switch
    {
        CreateFileOperation create => new CreateFileAction(this, create),
        RenameFileOperation rename => new RenameFileAction(this, rename),
        DeleteFileOperation delete => new DeleteFileAction(this, delete),
        _ => throw new ArgumentException("Unknown file operation.", nameof(operation)),
    };

    // Runs each action, and when one fails, takes back the ones that ran, newest first.
    private static bool Run(IEnumerable<FileAction> actions, Action<FileAction> run, Action<FileAction> takeBack, out string? failure)
    {
        var done = new List<FileAction>();
        foreach (var action in actions)
        {
            try
            {
                run(action);
                done.Add(action);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failure = exception.Message;
                foreach (var ran in done.AsEnumerable().Reverse())
                {
                    try
                    {
                        takeBack(ran);
                    }
                    catch (Exception again) when (again is IOException or UnauthorizedAccessException)
                    {
                        failure += " " + again.Message;
                    }
                }

                return false;
            }
        }

        failure = null;
        return true;
    }

    private async Task<IReadOnlyList<DocumentEdit>> AskParticipantsAsync(IReadOnlyList<FileOperation> operations)
    {
        if (participants.Count == 0)
            return [];

        using var budget = new CancellationTokenSource(ParticipantBudget);
        var asked = participants.Select(p => (Participant: p, Task: Task.Run(() => p.Value.WillApplyAsync(operations, budget.Token), CancellationToken.None))).ToList();
        await Task.WhenAny(Task.WhenAll(asked.Select(a => a.Task)), Task.Delay(ParticipantBudget, CancellationToken.None));
        await budget.CancelAsync();

        var edits = new List<DocumentEdit>();
        foreach (var (participant, task) in asked)
        {
            if (task.IsCompletedSuccessfully)
            {
                if (task.Result is { } edit)
                    edits.AddRange(edit.Documents.Where(d => d.Changes.Count > 0));
            }
            else
            {
                Report(participant, task, "before");
            }
        }

        return edits;
    }

    private void TellParticipants(IReadOnlyList<FileOperation> operations)
    {
        if (participants.Count == 0 || operations.Count == 0)
            return;

        _ = Task.Run(async () =>
        {
            using var budget = new CancellationTokenSource(ParticipantBudget);
            var told = participants.Select(p => (Participant: p, Task: Task.Run(() => p.Value.DidApplyAsync(operations, budget.Token), CancellationToken.None))).ToList();
            await Task.WhenAny(Task.WhenAll(told.Select(t => t.Task)), Task.Delay(ParticipantBudget, CancellationToken.None)).ConfigureAwait(false);
            await budget.CancelAsync().ConfigureAwait(false);
            foreach (var (participant, task) in told.Where(t => !t.Task.IsCompletedSuccessfully))
                Report(participant, task, "after");
        }, CancellationToken.None);
    }

    private void Report(Lazy<IFileOperationParticipant> participant, Task task, string when)
    {
        var owner = participant.IsValueCreated ? PluginCallers.Of(participant.Value.GetType().Assembly) : null;
        var who = owner is null ? "A file operation participant" : $"{owner.Manifest.Name} ({owner.Manifest.Id})";
        log(task.IsFaulted
            ? $"{who} failed {when} file operations: {task.Exception?.GetBaseException().Message}"
            : $"{who} took longer than {ParticipantBudget.TotalSeconds:0.#} seconds {when} file operations and was skipped.");
    }

    private void Finish(Step step, IReadOnlyList<FileOperation> operations)
    {
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        if (step.Actions.Count == 0)
            return;

        messages?.Publish(new FilesChangedMessage([.. step.Actions.SelectMany(a => a.Paths).Distinct(PathComparison.Comparer)]));
        TellParticipants(operations);
    }

    private static void Discard(Step step)
    {
        foreach (var action in step.Actions)
        {
            try
            {
                action.Discard();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The stash keeps what could not be let go of; it is cleaned up with abandoned sessions.
            }
        }
    }

    private async Task<IDocument?> LiveDocumentAsync(TextRecord text)
    {
        var document = text.Live ?? text.Document;
        if (documents.Documents.Contains(document))
            return document;
        if (document.FilePath is not { } path)
            return null;

        return documents.Find(path) ?? (File.Exists(path) ? (await editors.Value.OpenAsync(path, activate: false))?.Document : null);
    }

    // Makes a text's changes again, through the document's own redo when nothing changed since they were undone.
    private bool Reapply(TextRecord text)
    {
        if (text.Live is not { } document)
            return false;

        if (ReferenceEquals(document.Buffer.Current, text.Undone) && document.History.CanRedo)
        {
            if (EditorOf(document) is { } editor)
                editor.Redo();
            else
                document.History.Redo(document.Buffer);
        }
        else if (document.Buffer.Current.GetText() == text.Before.GetText())
        {
            Apply(document, text.Changes);
        }
        else
        {
            return false;
        }

        text.After = document.Buffer.Current;
        return true;
    }

    private static void Checked(Func<string?> problem, Action action)
    {
        if (problem() is { } reason)
            throw new IOException(reason);
        action();
    }

    private TextEditor? EditorOf(IDocument document) =>
        editors.Value.Editors.OfType<TextEditor>().FirstOrDefault(e => ReferenceEquals(e.Document, document));

    private static bool NeedsConfirmation(WorkspaceEditOptions options, FileOperationPlan plan, int textFiles)
    {
        if (options.Confirmation == WorkspaceEditConfirmation.Always)
            return true;
        if (options.Confirmation == WorkspaceEditConfirmation.Never)
            return false;

        var touched = textFiles;
        foreach (var operation in plan.Operations)
        {
            var folder = operation switch
            {
                RenameFileOperation rename => rename.OldPath,
                DeleteFileOperation delete => delete.Path,
                _ => null,
            };
            touched += folder is not null && Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Take(options.LargeEditThreshold + 1).Count()
                : 1;
            if (touched > options.LargeEditThreshold)
                return true;
        }

        return touched > options.LargeEditThreshold;
    }

    private static string Describe(IReadOnlyList<FileOperation> operations, int textFiles) => operations switch
    {
        [CreateFileOperation create] => $"Create {Path.GetFileName(create.FilePath)}",
        [RenameFileOperation rename] when PathComparison.Comparer.Equals(Path.GetDirectoryName(rename.OldPath), Path.GetDirectoryName(rename.NewPath)) =>
            $"Rename {Path.GetFileName(rename.OldPath)} to {Path.GetFileName(rename.NewPath)}",
        [RenameFileOperation rename] => $"Move {Path.GetFileName(rename.OldPath)} to {Path.GetFileName(Path.GetDirectoryName(rename.NewPath))}",
        [DeleteFileOperation delete] => $"Delete {Path.GetFileName(delete.Path)}",
        { Count: > 1 } => $"{operations.Count} file operations",
        _ => textFiles == 1 ? "Edit 1 file" : $"Edit {textFiles} files",
    };

    private static string Summarize(IReadOnlyList<FileOperation> operations, IEnumerable<(string Path, int Changes)> texts)
    {
        var lines = operations.Select(operation => operation switch
        {
            CreateFileOperation create => $"Create {create.FilePath}",
            RenameFileOperation rename => $"Rename {rename.OldPath} to {rename.NewPath}",
            DeleteFileOperation delete => $"Delete {delete.Path}",
            _ => "",
        }).Concat(texts.Select(t => t.Changes == 1 ? $"Change {t.Path} in 1 place" : $"Change {t.Path} in {t.Changes} places")).ToList();
        var shown = lines.Take(SummaryLines).ToList();
        if (lines.Count > SummaryLines)
            shown.Add($"and {lines.Count - SummaryLines} more.");
        return string.Join('\n', shown);
    }

    private bool Refuse(string title, string? problem)
    {
        notifications.Notify(NotificationKind.Warning, title, problem);
        return false;
    }

    // Returns what keeps the changes from applying to the text, or null when they apply.
    private static string? Check(TextSnapshot snapshot, bool isReadOnly, IReadOnlyList<DocumentEdit> edits, out IReadOnlyList<TextChange> changes)
    {
        changes = [.. edits.SelectMany(e => e.Changes).OrderBy(c => c.Span.Start).Select(c => c with { NewText = LineEndings.Normalize(c.NewText) })];
        if (isReadOnly)
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

    // Turns sorted changes of a text into the changes that take them back, in the coordinates of the changed text.
    private static List<TextChange> Inverse(IReadOnlyList<TextChange> changes, TextSnapshot before)
    {
        var inverse = new List<TextChange>(changes.Count);
        var shift = 0;
        foreach (var change in changes)
        {
            inverse.Add(new TextChange(new TextSpan(change.Span.Start + shift, change.NewText.Length), before.GetText(change.Span)));
            shift += change.NewText.Length - change.Span.Length;
        }

        return inverse;
    }

    private void Apply(IDocument document, IReadOnlyList<TextChange> changes)
    {
        if (EditorOf(document) is { } editor)
        {
            var selection = editor.Area.Selection;
            editor.Area.ApplyChanges(changes, new EditorSelection(OffsetMapping.Map(changes, selection.Anchor), OffsetMapping.Map(changes, selection.Caret)));
            return;
        }

        var changeSet = document.Buffer.Apply(changes);
        document.History.Push(changeSet, stateBefore: null, stateAfter: null);
    }

    private static FileStash DefaultStash()
    {
        var root = Path.Combine(RunesmithPaths.Cache, "workspace-edits");
        _ = Task.Run(() => FileStash.DeleteAbandoned(root));
        return new FileStash(Path.Combine(root, Guid.NewGuid().ToString("N")), SystemTrash.Create());
    }

    private sealed class Step(string label, List<FileAction> actions, List<TextRecord> texts)
    {
        public string Label { get; } = label;

        public List<FileAction> Actions { get; } = actions;

        public List<TextRecord> Texts { get; } = texts;
    }

    private sealed class TextRecord(IDocument document, IReadOnlyList<TextChange> changes, TextSnapshot before)
    {
        public IDocument Document { get; } = document;

        public IReadOnlyList<TextChange> Changes { get; } = changes;

        public TextSnapshot Before { get; } = before;

        public required TextSnapshot After { get; set; }

        /// <summary>Gets or sets the document now, when the one the edit changed was closed and its file opened again.</summary>
        public IDocument? Live { get; set; }

        public TextSnapshot? Undone { get; set; }
    }
}
