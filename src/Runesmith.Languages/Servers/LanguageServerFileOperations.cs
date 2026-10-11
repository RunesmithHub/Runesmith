using System.Collections.Concurrent;
using System.Composition;
using Runesmith.Lsp;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Protocol = Runesmith.Lsp.Protocol;

namespace Runesmith.Languages.Servers;

/// <summary>Tells the running language servers of the files and folders that workspace edits, the Explorer's among them, create, rename and
/// delete: each server gets the <c>workspace/will…Files</c> requests and <c>did…Files</c> notifications its filters ask for, and the changes
/// it answers with before the files change join the edit.</summary>
[Export(typeof(IFileOperationParticipant))]
[Shared]
public sealed class LanguageServerFileOperations : IFileOperationParticipant
{
    private const int RememberedPaths = 1000;

    private readonly Func<IReadOnlyList<ServerSession>> sessions;
    private readonly IDocumentService documents;
    private readonly ConcurrentDictionary<string, bool> folders = new(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

    [ImportingConstructor]
    public LanguageServerFileOperations(LanguageServerManager manager, IDocumentService documents)
        : this(manager.RunningSessions, documents)
    {
    }

    internal LanguageServerFileOperations(Func<IReadOnlyList<ServerSession>> sessions, IDocumentService documents)
    {
        this.sessions = sessions;
        this.documents = documents;
    }

    /// <summary>Gets how long the servers get to answer before the files change: less than the participants' budget, so the changes of the
    /// servers that answered in time are kept when another one is late.</summary>
    internal TimeSpan ServerBudget { get; init; } = IFileOperationParticipant.TimeBudget - TimeSpan.FromMilliseconds(500);

    public async Task<WorkspaceEdit?> WillApplyAsync(IReadOnlyList<FileOperation> operations, CancellationToken cancellationToken)
    {
        var running = sessions();
        if (running.Count == 0)
            return null;

        var changes = Describe(operations, before: true);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(ServerBudget);
        var asked = running.SelectMany(session => changes.Select(change =>
            (Session: session, change.Kind, Task: session.WillChangeFilesAsync(change.Kind, change.Targets, budget.Token)))).ToList();
        await Task.WhenAll(asked.Select(a => (Task)a.Task)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        var edits = new List<DocumentEdit>();
        foreach (var (session, kind, task) in asked)
        {
            if (task.IsCompletedSuccessfully)
            {
                if (task.Result is { } edit)
                    edits.AddRange(TextEdits(session, kind, edit));
            }
            else
            {
                Log(session, task.IsCanceled
                    ? $"{session.Definition.Name} did not answer {Method(kind)} within {ServerBudget.TotalSeconds:0.#} seconds, so its changes were left out."
                    : $"{session.Definition.Name} failed {Method(kind)}: {task.Exception?.GetBaseException().Message}");
            }
        }

        return edits.Count == 0 ? null : new WorkspaceEdit(edits);
    }

    public Task DidApplyAsync(IReadOnlyList<FileOperation> operations, CancellationToken cancellationToken)
    {
        var running = sessions();
        if (running.Count == 0)
            return Task.CompletedTask;

        var changes = Describe(operations, before: false);
        foreach (var session in running)
        {
            foreach (var (kind, targets) in changes)
                session.DidChangeFiles(kind, targets);
        }

        return Task.CompletedTask;
    }

    // The operations by the kind of request that tells servers of them, each with whether it is a file or a folder.
    private List<(FileOperationKind Kind, IReadOnlyList<FileOperationTarget> Targets)> Describe(IReadOnlyList<FileOperation> operations, bool before)
    {
        var targets = new Dictionary<FileOperationKind, List<FileOperationTarget>>();
        void Add(FileOperationKind kind, FileOperationTarget target)
        {
            if (!targets.TryGetValue(kind, out var list))
                targets[kind] = list = [];
            list.Add(target);
        }

        foreach (var operation in operations)
        {
            switch (operation)
            {
                case CreateFileOperation create:
                    Add(FileOperationKind.Create, new FileOperationTarget(create.FilePath, IsFolder: Remember(create.FilePath, folder: false)));
                    break;
                case CreateFolderOperation create:
                    Add(FileOperationKind.Create, new FileOperationTarget(create.FolderPath, IsFolder: Remember(create.FolderPath, folder: true)));
                    break;
                case RenameFileOperation rename:
                    Add(FileOperationKind.Rename, new FileOperationTarget(rename.OldPath, rename.NewPath, IsFolder(before ? rename.OldPath : rename.NewPath)));
                    break;
                case DeleteFileOperation delete:
                    Add(FileOperationKind.Delete, new FileOperationTarget(delete.Path, IsFolder: IsFolder(delete.Path)));
                    break;
            }
        }

        return [.. targets.Select(pair => (pair.Key, (IReadOnlyList<FileOperationTarget>)pair.Value))];
    }

    // Whether a path is a folder: as it is on disk, or as it was when last seen, since servers hear of a delete once the path is gone.
    private bool? IsFolder(string path)
    {
        if (Directory.Exists(path))
            return Remember(path, folder: true);
        if (File.Exists(path))
            return Remember(path, folder: false);
        return folders.TryGetValue(path, out var folder) ? folder : null;
    }

    private bool Remember(string path, bool folder)
    {
        if (folders.Count >= RememberedPaths)
            folders.Clear();
        folders[path] = folder;
        return folder;
    }

    // The server's text changes; resource operations are left out, as for every participant, and so are files that cannot be read.
    private List<DocumentEdit> TextEdits(ServerSession session, FileOperationKind kind, Protocol.WorkspaceEdit edit)
    {
        var edits = new List<DocumentEdit>();
        foreach (var (uri, _, changes) in edit.Documents)
        {
            try
            {
                if (LspConvert.ToDocumentEdit(uri, changes, documents) is { Changes.Count: > 0 } converted)
                    edits.Add(converted);
            }
            catch (LanguageFeatureException exception)
            {
                Log(session, $"A change {session.Definition.Name} answered {Method(kind)} with was left out: {exception.Message}");
            }
        }

        return edits;
    }

    private static string Method(FileOperationKind kind) => $"workspace/will{kind}Files";

    private static void Log(ServerSession session, string message) => session.Output.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message}");
}
