using System.ComponentModel;
using System.Text.Json;
using Runesmith.Lsp;
using Runesmith.Sdk.Build;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Shell;
using Runesmith.Text;
using Protocol = Runesmith.Lsp.Protocol;

namespace Runesmith.Languages.Servers;

/// <summary>Where a session's server is in its life.</summary>
internal enum SessionState
{
    Starting,
    Running,

    /// <summary>The executable could not be started, usually because it is not installed.</summary>
    Missing,

    /// <summary>The server failed to start or kept crashing; it stays down until the user restarts it.</summary>
    Failed,
    Stopped,
}

/// <summary>One language server running for one root folder, and the documents it has open.</summary>
/// <remarks>Every message about documents goes through one queue, so a change is always sent before any request made after it.</remarks>
internal sealed class ServerSession : IAsyncDisposable
{
    private const string CommandId = LanguageServerCommands.ShowServerOutput;
    private const int MaxCrashes = 3;
    private static readonly TimeSpan CrashWindow = TimeSpan.FromMinutes(3);

    /// <summary>How long a server gets to exit after being asked to before it is killed; servers busy loading a project may not answer.</summary>
    public static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(3);

    private readonly SessionContext context;
    private readonly Lock gate = new();
    private readonly Dictionary<IDocument, SyncedDocument> documents = [];
    private readonly HashSet<string> publishedPaths = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<Protocol.Diagnostic>> published = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Protocol.WorkDoneProgress> progress = new(StringComparer.Ordinal);
    private readonly List<DateTime> crashes = [];
    private readonly IStatusBarItem status;
    private LanguageClient? client;
    private Task queue = Task.CompletedTask;
    private bool stopping;

    public ServerSession(LanguageServerDefinition definition, string rootPath, SessionContext context)
    {
        Definition = definition;
        RootPath = rootPath;
        this.context = context;
        Output = context.Output.GetChannel(definition.Name);
        status = context.StatusBar.AddItem($"languageServer:{definition.Id}:{rootPath}", StatusBarAlignment.Right);
        status.CommandId = CommandId;
        status.Icon = "code";
        SetState(SessionState.Starting);
    }

    public LanguageServerDefinition Definition { get; }

    public string RootPath { get; }

    public IOutputChannel Output { get; }

    public SessionState State { get; private set; }

    /// <summary>Gets the client while the server runs, or null.</summary>
    public LanguageClient? Client => State == SessionState.Running ? client : null;

    public Protocol.ServerCapabilities Capabilities => client?.Capabilities ?? new Protocol.ServerCapabilities();

    public bool HasDocuments
    {
        get
        {
            lock (gate)
                return documents.Count > 0;
        }
    }

    public void Start() => _ = StartAsync(TimeSpan.Zero);

    /// <summary>Gives the server a document; it is opened on the server as soon as the server runs.</summary>
    public void Attach(IDocument document)
    {
        lock (gate)
        {
            if (documents.ContainsKey(document) || document.FilePath is null)
                return;
            var synced = new SyncedDocument(document, LspUri.FromPath(document.FilePath), document.LanguageId);
            documents[document] = synced;
            if (State == SessionState.Running)
                EnqueueOpen(synced);
        }
    }

    /// <summary>Takes a document back, closing it on the server.</summary>
    public void Detach(IDocument document)
    {
        lock (gate)
        {
            if (!documents.Remove(document, out var synced) || !synced.IsOpen)
                return;
            var running = client;
            Enqueue(() => running!.CloseAsync(synced.Uri));
        }
    }

    public void OnChanged(IDocument document, TextChangeSet changeSet)
    {
        lock (gate)
        {
            if (!documents.TryGetValue(document, out var synced) || !synced.IsOpen || synced.Snapshot == changeSet.After)
                return;

            var running = client!;
            var version = ++synced.Version;
            // A change the server missed is covered by sending the whole text.
            IReadOnlyList<Protocol.TextDocumentContentChangeEvent> changes = synced.Snapshot == changeSet.Before
                ? LspConvert.ToContentChanges(changeSet)
                : [new Protocol.TextDocumentContentChangeEvent(null, changeSet.After.GetText())];
            synced.Snapshot = changeSet.After;
            var after = changeSet.After;
            Enqueue(() => running.ChangeAsync(synced.Uri, version, changes, after.GetText));
        }
    }

    public void OnSaved(IDocument document)
    {
        lock (gate)
        {
            if (!documents.TryGetValue(document, out var synced) || !synced.IsOpen)
                return;
            var running = client!;
            var snapshot = synced.Snapshot;
            Enqueue(() => running.SaveAsync(synced.Uri, snapshot.GetText));
        }
    }

    /// <summary>Gets the diagnostics the server last published for a file, as it sent them.</summary>
    public IReadOnlyList<Protocol.Diagnostic> PublishedDiagnostics(string path)
    {
        lock (gate)
            return published.GetValueOrDefault(path) ?? [];
    }

    /// <summary>Completes once every message queued so far has been sent.</summary>
    public Task WhenSynced()
    {
        lock (gate)
            return queue;
    }

    public async ValueTask DisposeAsync()
    {
        lock (gate)
        {
            stopping = true;
            documents.Clear();
        }

        var running = client;
        if (running is not null)
        {
            await running.DisposeAsync().ConfigureAwait(false);
        }

        ClearDiagnostics();
        SetState(SessionState.Stopped);
        status.Dispose();
    }

    private async Task StartAsync(TimeSpan delay)
    {
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay).ConfigureAwait(false);

        LanguageClient started;
        lock (gate)
        {
            if (stopping)
                return;
            started = CreateClient();
            client = started;
            progress.Clear();
        }

        SetState(SessionState.Starting);
        Output.AppendLine($"[{DateTime.Now:HH:mm:ss}] Starting {ServerCommand.Resolve(context.GetCommand(Definition))} {string.Join(' ', Definition.Arguments)} in {RootPath}");
        try
        {
            await started.StartAsync().ConfigureAwait(false);
        }
        catch (LanguageServerException exception)
        {
            OnStartFailed(exception);
            return;
        }

        lock (gate)
        {
            if (stopping || client != started)
                return;
            State = SessionState.Running;
            foreach (var synced in documents.Values)
                EnqueueOpen(synced);
        }

        var info = started.ServerInfo is { } server ? $"{server.Name} {server.Version}".Trim() : Definition.Name;
        Output.AppendLine($"[{DateTime.Now:HH:mm:ss}] {info} is running.");
        SetState(SessionState.Running);
        context.DecorationsChanged?.Invoke();
        context.SyntaxChanged?.Invoke();
    }

    private LanguageClient CreateClient()
    {
        var options = new LanguageServerOptions
        {
            Command = ServerCommand.Resolve(context.GetCommand(Definition)),
            Arguments = Definition.Arguments,
            Environment = ServerCommand.Environment(),
            RootPath = RootPath,
            InitializationOptions = ParseOptions(Definition.InitializationOptions),
            ClientVersion = typeof(ServerSession).Assembly.GetName().Version?.ToString(3),
            ShutdownTimeout = ShutdownTimeout,
        };
        var created = new LanguageClient(options);
        created.DiagnosticsPublished += OnDiagnosticsPublished;
        created.MessageLogged += (_, message) => Output.AppendLine(message.Message);
        created.MessageShown += OnMessageShown;
        created.Progress += OnProgress;
        created.ApplyEdit = context.ApplyEdit;
        created.DecorationsRefreshRequested += (_, _) => context.DecorationsChanged?.Invoke();
        created.SyntaxRefreshRequested += (_, _) => context.SyntaxChanged?.Invoke();
        created.StateChanged += (sender, state) => OnClientStateChanged((LanguageClient)sender!, state);
        if (context.IsTracing())
            created.Trace += trace => Output.AppendLine($"{(trace.Direction == JsonRpcDirection.Sent ? "-->" : "<--")} {trace.Method ?? $"#{trace.Id}"} {trace.Json}");
        return created;
    }

    private void EnqueueOpen(SyncedDocument synced)
    {
        var running = client!;
        synced.Version = 1;
        synced.Snapshot = synced.Document.Buffer.Current;
        synced.IsOpen = true;
        var snapshot = synced.Snapshot;
        var version = synced.Version;
        Enqueue(() => running.OpenAsync(synced.Uri, synced.LanguageId, version, snapshot.GetText()));
    }

    private void Enqueue(Func<Task> send) =>
        queue = queue.ContinueWith(async _ =>
        {
            try
            {
                await send().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException or LspException or InvalidOperationException)
            {
                Output.AppendLine($"[{DateTime.Now:HH:mm:ss}] Could not send a document update: {exception.Message}");
            }
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();

    private void OnStartFailed(LanguageServerException exception)
    {
        var missing = IsMissingExecutable(exception);
        Output.AppendLine($"[{DateTime.Now:HH:mm:ss}] {exception.Message}");
        if (missing && Definition.InstallHint is { } hint)
            Output.AppendLine(hint);

        lock (gate)
        {
            if (stopping)
                return;
            foreach (var synced in documents.Values)
                synced.IsOpen = false;
        }

        SetState(missing ? SessionState.Missing : SessionState.Failed);
        context.ReportFailure(this, missing
            ? $"Runesmith could not start {context.GetCommand(Definition)}. {Definition.InstallHint ?? $"Install it, or set its path in the {Definition.Name} command setting."}"
            : $"{Definition.Name} failed to start. The output has the details.");
    }

    private void OnClientStateChanged(LanguageClient sender, LanguageServerState state)
    {
        if (state != LanguageServerState.Failed)
            return;

        TimeSpan? restartDelay;
        lock (gate)
        {
            // Failures while starting are reported by StartAsync; a stopped or replaced client is not a crash.
            if (stopping || sender != client || State != SessionState.Running)
                return;

            foreach (var synced in documents.Values)
                synced.IsOpen = false;
            var now = DateTime.UtcNow;
            crashes.RemoveAll(time => now - time > CrashWindow);
            crashes.Add(now);
            restartDelay = crashes.Count < MaxCrashes ? TimeSpan.FromSeconds(1 << crashes.Count) : null;
            State = SessionState.Failed;
        }

        ClearDiagnostics();
        if (restartDelay is { } delay)
        {
            Output.AppendLine($"[{DateTime.Now:HH:mm:ss}] {Definition.Name} stopped unexpectedly; restarting in {delay.TotalSeconds:0} s.");
            SetState(SessionState.Starting);
            _ = StartAsync(delay);
        }
        else
        {
            Output.AppendLine($"[{DateTime.Now:HH:mm:ss}] {Definition.Name} stopped {MaxCrashes} times within {CrashWindow.TotalMinutes:0} minutes and stays stopped.");
            SetState(SessionState.Failed);
            context.ReportFailure(this, $"{Definition.Name} kept stopping unexpectedly. Run \"Restart Language Servers\" to try again.");
        }
    }

    private void OnDiagnosticsPublished(object? sender, Protocol.PublishDiagnosticsParams parameters)
    {
        if (sender != client || LspUri.ToPath(parameters.Uri) is not { } path)
            return;

        var diagnostics = parameters.Diagnostics.Select(d => LspConvert.ToDiagnostic(path, d, Definition.Id)).ToList();
        lock (gate)
        {
            publishedPaths.Add(path);
            published[path] = parameters.Diagnostics;
        }
        context.Diagnostics.Set(Definition.Id, path, diagnostics);
    }

    private void OnMessageShown(object? sender, Protocol.ShowMessageParams message)
    {
        Output.AppendLine($"[{message.Type}] {message.Message}");
        if (message.Type is Protocol.MessageType.Error or Protocol.MessageType.Warning)
        {
            var kind = message.Type == Protocol.MessageType.Error ? NotificationKind.Error : NotificationKind.Warning;
            context.Notifications.Notify(kind, Definition.Name, message.Message, "Show output", Output.Show);
        }
    }

    private void OnProgress(object? sender, Protocol.ProgressParams parameters)
    {
        lock (gate)
        {
            if (parameters.Value.Kind == "end")
            {
                progress.Remove(parameters.Token);
            }
            else
            {
                var previous = progress.GetValueOrDefault(parameters.Token);
                progress[parameters.Token] = parameters.Value with { Title = parameters.Value.Title ?? previous?.Title };
            }
        }

        SetState(State);
    }

    private void ClearDiagnostics()
    {
        string[] paths;
        lock (gate)
        {
            paths = [.. publishedPaths];
            publishedPaths.Clear();
            published.Clear();
        }

        foreach (var path in paths)
            context.Diagnostics.Set(Definition.Id, path, []);
    }

    private void SetState(SessionState state)
    {
        Protocol.WorkDoneProgress? work;
        lock (gate)
        {
            State = state;
            work = progress.Values.LastOrDefault();
        }

        var name = Definition.Name;
        (status.Text, status.State, status.ToolTip) = state switch
        {
            SessionState.Starting => ($"{name}: starting", StatusBarState.Busy, $"Starting {name} in {RootPath}"),
            SessionState.Running when work is not null => ($"{name}: {Describe(work)}", StatusBarState.Busy, $"{name} is working in {RootPath}"),
            SessionState.Running => (name, StatusBarState.Success, $"{name} is running in {RootPath}. Click for its output."),
            SessionState.Missing => ($"{name}: not installed", StatusBarState.Warning, Definition.InstallHint ?? $"Runesmith could not start {Definition.Command}."),
            SessionState.Failed => ($"{name}: stopped", StatusBarState.Error, $"{name} stopped. Click for its output."),
            _ => (name, StatusBarState.Normal, $"{name} is stopped."),
        };
    }

    private static string Describe(Protocol.WorkDoneProgress work)
    {
        var text = string.Join(": ", new[] { work.Title, work.Message }.Where(part => !string.IsNullOrWhiteSpace(part)));
        if (text.Length == 0)
            text = "working";
        return work.Percentage is { } percentage ? $"{text} ({percentage}%)" : text;
    }

    private static bool IsMissingExecutable(Exception exception)
    {
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is Win32Exception or FileNotFoundException)
                return true;
        }

        return false;
    }

    private static JsonElement? ParseOptions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        using var parsed = JsonDocument.Parse(json);
        return parsed.RootElement.Clone();
    }

    private sealed class SyncedDocument(IDocument document, string uri, string languageId)
    {
        public IDocument Document { get; } = document;

        public string Uri { get; } = uri;

        public string LanguageId { get; } = languageId;

        public bool IsOpen { get; set; }

        public int Version { get; set; }

        public TextSnapshot Snapshot { get; set; } = TextSnapshot.Empty;
    }
}

/// <summary>The services a session reports to, and the settings it reads.</summary>
internal sealed record SessionContext(
    IOutputService Output,
    IStatusBar StatusBar,
    IDiagnosticService Diagnostics,
    INotificationService Notifications,
    Func<LanguageServerDefinition, string> GetCommand,
    Func<bool> IsTracing,
    Action<ServerSession, string> ReportFailure)
{
    /// <summary>Gets what applies an edit a server asks for; null refuses it.</summary>
    public Func<Protocol.ApplyWorkspaceEditParams, Task<bool>>? ApplyEdit { get; init; }

    /// <summary>Gets what runs when a server starts running or asks for its inlay hints and code lenses to be requested again.</summary>
    public Action? DecorationsChanged { get; init; }

    /// <summary>Gets what runs when a server starts running or asks for its semantic tokens or folding ranges to be requested again.</summary>
    public Action? SyntaxChanged { get; init; }
}
