using System.ComponentModel;
using System.Composition;
using Runesmith.Sdk.Build;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Settings;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.Workspace;
using Runesmith.Text;

namespace Runesmith.Languages.Servers;

/// <summary>Starts language servers for the documents that need them, keeps the servers' copies of those documents up to date, and stops
/// the servers when Runesmith closes or the folder changes.</summary>
/// <remarks>It starts working when it is created, so the shell creates it once at startup. A server starts the first time a document of one
/// of its languages opens, in the nearest folder above the document that has one of the server's root markers, or in the open folder.</remarks>
[Export]
[Shared]
public sealed class LanguageServerManager : IDisposable
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    private readonly IReadOnlyList<LanguageServerDefinition> definitions;
    private readonly IDocumentService documents;
    private readonly IWorkspace workspace;
    private readonly ISettingsService settings;
    private readonly INotificationService notifications;
    private readonly SessionContext context;
    private readonly Dictionary<(string ServerId, string Root), ServerSession> sessions = [];
    private readonly Dictionary<IDocument, ServerSession?> routes = [];
    private readonly HashSet<string> reportedServers = new(StringComparer.Ordinal);
    private readonly HashSet<Task> stopping = [];
    private readonly Lock gate = new();
    private string? workspaceRoot;
    private bool disposed;

    [ImportingConstructor]
    public LanguageServerManager(
        [ImportMany] IEnumerable<LanguageServerDefinition> definitions,
        IDocumentService documents,
        IWorkspace workspace,
        ISettingsService settings,
        IDiagnosticService diagnostics,
        IStatusBar statusBar,
        IOutputService output,
        INotificationService notifications,
        [Import(AllowDefault = true)] Lazy<IWorkspaceEditService>? workspaceEdits = null)
    {
        this.definitions = [.. definitions];
        this.documents = documents;
        this.workspace = workspace;
        this.settings = settings;
        this.notifications = notifications;
        context = new SessionContext(output, statusBar, diagnostics, notifications, GetCommand, () => Read(LanguageServerSettings.Trace, false), ReportFailure)
        {
            ApplyEdit = workspaceEdits is null ? null : request => ApplyEditAsync(workspaceEdits.Value, request.Edit),
            DecorationsChanged = () => DecorationsChanged?.Invoke(this, EventArgs.Empty),
        };
        workspaceRoot = workspace.RootPath;

        documents.Opened += OnDocumentOpened;
        documents.Closed += OnDocumentClosed;
        documents.Saved += OnDocumentSaved;
        workspace.Changed += OnWorkspaceChanged;
        settings.Changed += OnSettingChanged;
        foreach (var document in documents.Documents)
            Track(document);
    }

    /// <summary>Raised, on any thread, when a server starts running or asks for its inlay hints and code lenses to be requested again.</summary>
    public event EventHandler? DecorationsChanged;

    /// <summary>Gets the running server that has a document open, or null.</summary>
    internal ServerSession? FindSession(IDocument document)
    {
        lock (gate)
            return routes.GetValueOrDefault(document) is { State: SessionState.Running } session ? session : null;
    }

    /// <summary>Shows the output of a server: the one with the given id, or else the one that most needs attention.</summary>
    public void ShowOutput(string? serverId = null)
    {
        ServerSession? session;
        lock (gate)
        {
            session = sessions.Values.Where(s => serverId is null || s.Definition.Id == serverId)
                .OrderByDescending(s => s.State is SessionState.Failed or SessionState.Missing)
                .FirstOrDefault();
        }

        session?.Output.Show();
    }

    /// <summary>Stops every server and starts the ones the open documents need again, retrying servers that failed or were missing.</summary>
    public async Task RestartAsync()
    {
        await StopAllAsync().ConfigureAwait(false);
        lock (gate)
            reportedServers.Clear();
        RouteAll();
    }

    /// <summary>Stops every server, waiting for each to shut down cleanly for a few seconds before it is killed.</summary>
    public async Task StopAllAsync()
    {
        ServerSession[] running;
        Task[] alreadyStopping;
        lock (gate)
        {
            running = [.. sessions.Values];
            alreadyStopping = [.. stopping];
            sessions.Clear();
            foreach (var document in routes.Keys.ToList())
                routes[document] = null;
        }

        await Task.WhenAll(running.Select(session => session.DisposeAsync().AsTask()).Concat(alreadyStopping)).ConfigureAwait(false);
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;
            disposed = true;
        }

        documents.Opened -= OnDocumentOpened;
        documents.Closed -= OnDocumentClosed;
        documents.Saved -= OnDocumentSaved;
        workspace.Changed -= OnWorkspaceChanged;
        settings.Changed -= OnSettingChanged;
        foreach (var document in routes.Keys.ToList())
            Untrack(document);

        // Runesmith is closing; servers that do not exit in time are killed by their clients.
        Task.Run(StopAllAsync).Wait(ServerSession.ShutdownTimeout + TimeSpan.FromSeconds(1));
    }

    private void OnDocumentOpened(object? sender, DocumentEventArgs e) => Track(e.Document);

    private void OnDocumentClosed(object? sender, DocumentEventArgs e) => Untrack(e.Document);

    private void OnDocumentSaved(object? sender, DocumentEventArgs e)
    {
        lock (gate)
            routes.GetValueOrDefault(e.Document)?.OnSaved(e.Document);
    }

    private void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        if (PathComparer.Equals(workspaceRoot, workspace.RootPath))
            return;
        workspaceRoot = workspace.RootPath;
        _ = RestartAsync();
    }

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.Key.StartsWith("languageServers.", StringComparison.Ordinal))
            _ = RestartAsync();
    }

    private void Track(IDocument document)
    {
        lock (gate)
        {
            if (disposed || routes.ContainsKey(document))
                return;
            routes[document] = null;
        }

        document.PropertyChanged += OnDocumentPropertyChanged;
        document.Buffer.Changed += OnBufferChanged;
        Route(document);
    }

    private void Untrack(IDocument document)
    {
        ServerSession? session;
        lock (gate)
        {
            if (!routes.Remove(document, out session))
                return;
        }

        document.PropertyChanged -= OnDocumentPropertyChanged;
        document.Buffer.Changed -= OnBufferChanged;
        session?.Detach(document);
        StopIfUnused(session);
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is IDocument document && e.PropertyName is nameof(IDocument.FilePath) or nameof(IDocument.LanguageId))
            Route(document, reopen: true);
    }

    private void OnBufferChanged(object? sender, TextChangedEventArgs e)
    {
        ServerSession? session = null;
        IDocument? document = null;
        lock (gate)
        {
            foreach (var (candidate, route) in routes)
            {
                if (candidate.Buffer == sender)
                {
                    (document, session) = (candidate, route);
                    break;
                }
            }
        }

        if (document is not null)
            session?.OnChanged(document, e.ChangeSet);
    }

    private void RouteAll()
    {
        List<IDocument> tracked;
        lock (gate)
            tracked = [.. routes.Keys];
        foreach (var document in tracked)
            Route(document);
    }

    /// <summary>Gives a document to the server that should have it, taking it from another server first if it moved.</summary>
    private void Route(IDocument document, bool reopen = false)
    {
        var target = FindTarget(document);
        ServerSession? previous;
        ServerSession? next = null;
        lock (gate)
        {
            if (disposed || !routes.TryGetValue(document, out previous))
                return;

            if (target is { } t)
            {
                var key = (t.Definition.Id, t.Root);
                if (!sessions.TryGetValue(key, out next))
                {
                    next = new ServerSession(t.Definition, t.Root, context);
                    sessions[key] = next;
                    next.Start();
                }
            }

            if (previous == next && !reopen)
                return;
            routes[document] = next;
        }

        previous?.Detach(document);
        next?.Attach(document);
        if (previous != next)
            StopIfUnused(previous);
    }

    private (LanguageServerDefinition Definition, string Root)? FindTarget(IDocument document)
    {
        if (document.FilePath is not { } path)
            return null;

        var definition = definitions.FirstOrDefault(d =>
            d.LanguageIds.Contains(document.LanguageId, StringComparer.OrdinalIgnoreCase) && Read(LanguageServerSettings.EnabledKey(d.Id), true));
        if (definition is null)
            return null;

        lock (gate)
        {
            // A server that is missing or kept crashing stays down until the user restarts the servers or changes their settings.
            if (reportedServers.Contains(definition.Id))
                return null;
        }

        return (definition, FindRoot(definition, Path.GetFullPath(path)));
    }

    private string FindRoot(LanguageServerDefinition definition, string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var root = workspaceRoot is { } workspacePath && IsInside(path, workspacePath) ? Path.GetFullPath(workspacePath) : null;
        for (var current = directory; current is not null; current = Path.GetDirectoryName(current))
        {
            if (HasMarker(current, definition.RootMarkers))
                return current;
            if (root is not null && PathComparer.Equals(Path.TrimEndingDirectorySeparator(current), Path.TrimEndingDirectorySeparator(root)))
                break;
        }

        return root ?? directory;
    }

    private static bool HasMarker(string directory, IReadOnlyList<string> markers)
    {
        try
        {
            return markers.Any(marker => Directory.EnumerateFileSystemEntries(directory, marker).Any());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsInside(string path, string folder)
    {
        var relative = Path.GetRelativePath(folder, path);
        return !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    private void StopIfUnused(ServerSession? session)
    {
        if (session is null || session.HasDocuments)
            return;

        Task stop;
        lock (gate)
        {
            if (routes.ContainsValue(session) || !sessions.Remove((session.Definition.Id, session.RootPath)))
                return;

            // Kept until it completes, so closing Runesmith waits for the server to exit instead of leaving it running.
            stop = session.DisposeAsync().AsTask();
            stopping.Add(stop);
        }

        _ = stop.ContinueWith(
            completed =>
            {
                lock (gate)
                    stopping.Remove(completed);
            },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>Applies an edit a server sent, such as after running one of its commands.</summary>
    internal async Task<bool> ApplyEditAsync(IWorkspaceEditService service, Lsp.Protocol.WorkspaceEdit edit)
    {
        try
        {
            return await service.ApplyAsync(LspConvert.ToWorkspaceEdit(edit, documents)).ConfigureAwait(false);
        }
        catch (LanguageFeatureException exception)
        {
            notifications.Notify(NotificationKind.Warning, "The language server's edit was not applied", exception.Message);
            return false;
        }
    }

    private void ReportFailure(ServerSession session, string message)
    {
        lock (gate)
        {
            if (!reportedServers.Add(session.Definition.Id))
                return;
        }

        var kind = session.State == SessionState.Missing ? NotificationKind.Warning : NotificationKind.Error;
        var title = session.State == SessionState.Missing ? $"{session.Definition.Name} is not installed" : $"{session.Definition.Name} stopped";
        notifications.Notify(kind, title, message, "Show output", session.Output.Show);
    }

    private string GetCommand(LanguageServerDefinition definition) =>
        Read(LanguageServerSettings.CommandKey(definition.Id), "") is { Length: > 0 } command ? command : definition.Command;

    private T Read<T>(string key, T fallback)
    {
        try
        {
            return settings.Get<T>(key);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidCastException)
        {
            return fallback;
        }
    }
}
