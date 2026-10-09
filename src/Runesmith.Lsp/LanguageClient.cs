using System.Diagnostics;
using System.Text.Json;
using Runesmith.Lsp.Protocol;

namespace Runesmith.Lsp;

/// <summary>Runs a language server as a child process and talks to it over its standard input and output.</summary>
/// <remarks>Events are raised on a background thread, notifications in the order the server sent them.</remarks>
public sealed partial class LanguageClient : IAsyncDisposable
{
    private readonly LanguageServerOptions _options;
    private Process? _process;
    private JsonRpcConnection? _connection;
    private int _state;

    public LanguageClient(LanguageServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public LanguageServerState State => (LanguageServerState)Volatile.Read(ref _state);

    /// <summary>Gets what the server offers, once it has started.</summary>
    public ServerCapabilities Capabilities { get; private set; } = new();

    public ServerInfo? ServerInfo { get; private set; }

    /// <summary>Raised when the server publishes the diagnostics of a document.</summary>
    public event EventHandler<PublishDiagnosticsParams>? DiagnosticsPublished;

    /// <summary>Raised when the server asks to show the user a message.</summary>
    public event EventHandler<ShowMessageParams>? MessageShown;

    /// <summary>Raised for the server's log messages and each line it writes to standard error.</summary>
    public event EventHandler<LogMessageParams>? MessageLogged;

    /// <summary>Raised for each step of the server's long-running work, such as loading a solution.</summary>
    public event EventHandler<ProgressParams>? Progress;

    public event EventHandler<LanguageServerState>? StateChanged;

    /// <summary>Raised for every JSON-RPC message, for a protocol log.</summary>
    public event Action<JsonRpcTrace>? Trace;

    /// <summary>Starts the server and completes the <c>initialize</c> handshake.</summary>
    /// <exception cref="LanguageServerException">The server could not be started or failed to initialize.</exception>
    public Task StartAsync(CancellationToken cancellationToken = default) => StartAsync(() =>
    {
        _process = StartProcess();
        _ = ReadErrorsAsync(_process);
        return (_process.StandardOutput.BaseStream, _process.StandardInput.BaseStream);
    }, cancellationToken);

    /// <summary>Completes the handshake with a server that is already running at the other end of two streams, for tests.</summary>
    internal Task StartAsync(Stream input, Stream output, CancellationToken cancellationToken) => StartAsync(() => (input, output), cancellationToken);

    private async Task StartAsync(Func<(Stream Input, Stream Output)> open, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _state, (int)LanguageServerState.Starting, (int)LanguageServerState.NotStarted) != (int)LanguageServerState.NotStarted)
            throw new InvalidOperationException("The server has already been started.");
        StateChanged?.Invoke(this, LanguageServerState.Starting);

        try
        {
            var (input, output) = open();
            var connection = new JsonRpcConnection(input, output);
            Register(connection);
            _connection = connection;
            connection.Start();

            var root = Path.GetFullPath(_options.RootPath);
            var rootUri = LspUri.FromPath(root);
            var parameters = new InitializeParams(
                Environment.ProcessId,
                new ClientInfo(_options.ClientName, _options.ClientVersion),
                rootUri,
                root,
                ClientCapabilities.Create(),
                _options.InitializationOptions,
                [new WorkspaceFolder(rootUri, Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar)))]);
            var result = await connection.SendRequestAsync<InitializeResult>("initialize", parameters, cancellationToken).ConfigureAwait(false)
                         ?? throw new LanguageServerException($"{_options.Command} answered initialize without a result.");
            Capabilities = result.Capabilities;
            ServerInfo = result.ServerInfo;
            await connection.SendNotificationAsync("initialized", new Dictionary<string, object>()).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not LanguageServerException and not OperationCanceledException)
        {
            await FailAsync().ConfigureAwait(false);
            throw new LanguageServerException($"{_options.Command} could not be started: {ex.Message}", ex);
        }
        catch
        {
            await FailAsync().ConfigureAwait(false);
            throw;
        }

        SetState(LanguageServerState.Running);
    }

    /// <summary>Tells the server a document is open; from now on the editor, not the disk, owns its text.</summary>
    public Task OpenAsync(string uri, string languageId, int version, string text) =>
        SyncsOpenClose
            ? NotifyAsync("textDocument/didOpen", new DidOpenTextDocumentParams(new TextDocumentItem(uri, languageId, version, text)))
            : Task.CompletedTask;

    /// <summary>Sends a document's changes, in the order they were made, or its whole text when the server only takes full text.</summary>
    /// <param name="fullText">Gets the document's text after the changes; only called when the server wants full text.</param>
    public Task ChangeAsync(string uri, int version, IReadOnlyList<TextDocumentContentChangeEvent> changes, Func<string> fullText)
    {
        ArgumentNullException.ThrowIfNull(fullText);
        var kind = Capabilities.TextDocumentSync?.Change ?? TextDocumentSyncKind.None;
        if (kind == TextDocumentSyncKind.None)
            return Task.CompletedTask;
        IReadOnlyList<TextDocumentContentChangeEvent> sent = kind == TextDocumentSyncKind.Incremental ? changes : [new TextDocumentContentChangeEvent(null, fullText())];
        return NotifyAsync("textDocument/didChange", new DidChangeTextDocumentParams(new VersionedTextDocumentIdentifier(uri, version), sent));
    }

    /// <summary>Tells the server a document was saved.</summary>
    /// <param name="text">Gets the saved text; only called when the server asks for it.</param>
    public Task SaveAsync(string uri, Func<string> text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var sync = Capabilities.TextDocumentSync;
        return sync is { Save: true }
            ? NotifyAsync("textDocument/didSave", new DidSaveTextDocumentParams(new TextDocumentIdentifier(uri), sync.SaveIncludesText ? text() : null))
            : Task.CompletedTask;
    }

    /// <summary>Tells the server a document was closed; the disk owns its text again.</summary>
    public Task CloseAsync(string uri) =>
        SyncsOpenClose ? NotifyAsync("textDocument/didClose", new DidCloseTextDocumentParams(new TextDocumentIdentifier(uri))) : Task.CompletedTask;

    /// <summary>Gets completions at a position; empty when the server offers none.</summary>
    public async Task<CompletionList> CompletionAsync(string uri, Position position, CompletionContext? context, CancellationToken cancellationToken)
    {
        if (Capabilities.CompletionProvider is null)
            return CompletionList.Empty;
        var result = await RequestAsync<JsonElement>("textDocument/completion", new CompletionParams(new TextDocumentIdentifier(uri), position, context), cancellationToken)
            .ConfigureAwait(false);
        return result.ValueKind switch
        {
            JsonValueKind.Array => new CompletionList(false, result.Deserialize(LspJsonContext.Default.CompletionItemArray) ?? []),
            JsonValueKind.Object => result.Deserialize(LspJsonContext.Default.CompletionList) ?? CompletionList.Empty,
            _ => CompletionList.Empty,
        };
    }

    /// <summary>Fills in the details of a completion item that the server left out of the list, such as its documentation; returns the item
    /// unchanged when the server does not resolve items.</summary>
    public async Task<CompletionItem> ResolveCompletionItemAsync(CompletionItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (Capabilities.CompletionProvider?.ResolveProvider != true)
            return item;
        return await RequestAsync<CompletionItem>("completionItem/resolve", item, cancellationToken).ConfigureAwait(false) ?? item;
    }

    /// <summary>Gets hover information at a position, or null when there is none.</summary>
    public Task<Hover?> HoverAsync(string uri, Position position, CancellationToken cancellationToken) =>
        Capabilities.HoverProvider
            ? RequestAsync<Hover>("textDocument/hover", new TextDocumentPositionParams(new TextDocumentIdentifier(uri), position), cancellationToken)
            : Task.FromResult<Hover?>(null);

    /// <summary>Gets the signatures of the call at a position, or null outside a call.</summary>
    public Task<SignatureHelp?> SignatureHelpAsync(string uri, Position position, CancellationToken cancellationToken) =>
        Capabilities.SignatureHelpProvider is not null
            ? RequestAsync<SignatureHelp>("textDocument/signatureHelp", new TextDocumentPositionParams(new TextDocumentIdentifier(uri), position), cancellationToken)
            : Task.FromResult<SignatureHelp?>(null);

    /// <summary>Gets where the symbol at a position is defined; links arrive as the location of their target's selection.</summary>
    public async Task<IReadOnlyList<Location>> DefinitionAsync(string uri, Position position, CancellationToken cancellationToken)
    {
        if (!Capabilities.DefinitionProvider)
            return [];
        var result = await RequestAsync<JsonElement>("textDocument/definition", new TextDocumentPositionParams(new TextDocumentIdentifier(uri), position), cancellationToken)
            .ConfigureAwait(false);
        return result.ValueKind switch
        {
            JsonValueKind.Object => [ToLocation(result)],
            JsonValueKind.Array => [.. result.EnumerateArray().Select(ToLocation)],
            _ => [],
        };
    }

    /// <summary>Gets every reference to the symbol at a position.</summary>
    public async Task<IReadOnlyList<Location>> ReferencesAsync(string uri, Position position, bool includeDeclaration, CancellationToken cancellationToken)
    {
        if (!Capabilities.ReferencesProvider)
            return [];
        var parameters = new ReferenceParams(new TextDocumentIdentifier(uri), position, new ReferenceContext(includeDeclaration));
        return await RequestAsync<Location[]>("textDocument/references", parameters, cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Asks the server to shut down and exit, and kills it when it does not exit in time.</summary>
    public async Task StopAsync()
    {
        var current = State;
        if (current is LanguageServerState.ShuttingDown or LanguageServerState.Stopped)
            return;
        if (current != LanguageServerState.Running)
        {
            await FailAsync().ConfigureAwait(false);
            return;
        }

        SetState(LanguageServerState.ShuttingDown);
        using var timeout = new CancellationTokenSource(_options.ShutdownTimeout);
        try
        {
            await _connection!.SendRequestAsync<JsonElement>("shutdown", null, timeout.Token).ConfigureAwait(false);
            await _connection.SendNotificationAsync("exit", null).ConfigureAwait(false);
            if (_process is not null)
                await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or LspException or ObjectDisposedException)
        {
            Kill();
        }

        await DisposeConnectionAsync().ConfigureAwait(false);
        SetState(LanguageServerState.Stopped);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _process?.Dispose();
    }

    private bool SyncsOpenClose => Capabilities.TextDocumentSync?.OpenClose == true;

    private Process StartProcess()
    {
        var info = new ProcessStartInfo(_options.Command)
        {
            WorkingDirectory = _options.WorkingDirectory ?? _options.RootPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in _options.Arguments)
            info.ArgumentList.Add(argument);
        foreach (var (name, value) in _options.Environment)
            info.Environment[name] = value;

        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        process.Exited += OnProcessExited;
        process.Start();
        return process;
    }

    private void Register(JsonRpcConnection connection)
    {
        connection.Trace += trace => Trace?.Invoke(trace);
        connection.OnNotification("textDocument/publishDiagnostics", p => Raise(DiagnosticsPublished, p, LspJsonContext.Default.PublishDiagnosticsParams));
        connection.OnNotification("window/showMessage", p => Raise(MessageShown, p, LspJsonContext.Default.ShowMessageParams));
        connection.OnNotification("window/logMessage", p => Raise(MessageLogged, p, LspJsonContext.Default.LogMessageParams));
        connection.OnNotification("$/progress", p => Raise(Progress, p, LspJsonContext.Default.ProgressParams));
        connection.OnRequest("workspace/configuration", (p, _) => Task.FromResult<object?>(Configuration(p)));
        connection.OnRequest("workspace/workspaceFolders", (_, _) =>
        {
            var root = Path.GetFullPath(_options.RootPath);
            return Task.FromResult<object?>(new[] { new WorkspaceFolder(LspUri.FromPath(root), Path.GetFileName(root)) });
        });
        // The client declares no dynamic registration, but some servers register anyway and stop if refused.
        connection.OnRequest("client/registerCapability", (_, _) => Task.FromResult<object?>(null));
        connection.OnRequest("client/unregisterCapability", (_, _) => Task.FromResult<object?>(null));
        connection.OnRequest("window/workDoneProgress/create", (_, _) => Task.FromResult<object?>(null));
        RegisterEditing(connection);
        connection.OnRequest("window/showMessageRequest", (p, _) =>
        {
            if (p is { } element && element.Deserialize(LspJsonContext.Default.ShowMessageParams) is { } message)
                MessageShown?.Invoke(this, message);
            return Task.FromResult<object?>(null);
        });
    }

    private JsonElement?[] Configuration(JsonElement? parameters)
    {
        var request = parameters?.Deserialize(LspJsonContext.Default.ConfigurationParams);
        if (request is null)
            return [];
        return [.. request.Items.Select(item => item.Section is not null && _options.Configuration.TryGetValue(item.Section, out var value) ? value : (JsonElement?)null)];
    }

    private void Raise<T>(EventHandler<T>? handler, JsonElement? parameters, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        if (handler is not null && parameters is { } element && element.Deserialize(typeInfo) is { } value)
            handler(this, value);
    }

    private async Task ReadErrorsAsync(Process process)
    {
        try
        {
            while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
                MessageLogged?.Invoke(this, new LogMessageParams(MessageType.Log, line));
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }
    }

    private async Task<T?> RequestAsync<T>(string method, object parameters, CancellationToken cancellationToken)
    {
        if (State != LanguageServerState.Running)
            return default;
        return await _connection!.SendRequestAsync<T>(method, parameters, cancellationToken).ConfigureAwait(false);
    }

    private Task NotifyAsync(string method, object parameters) =>
        State == LanguageServerState.Running ? _connection!.SendNotificationAsync(method, parameters) : Task.CompletedTask;

    private static Location ToLocation(JsonElement element) =>
        element.TryGetProperty("targetUri", out _)
            ? element.Deserialize(LspJsonContext.Default.LocationLink) is { } link ? new Location(link.TargetUri, link.TargetSelectionRange) : throw new JsonException("Expected a location link.")
            : element.Deserialize(LspJsonContext.Default.Location) ?? throw new JsonException("Expected a location.");

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (State is LanguageServerState.Starting or LanguageServerState.Running)
            SetState(LanguageServerState.Failed);
    }

    private async Task FailAsync()
    {
        Kill();
        await DisposeConnectionAsync().ConfigureAwait(false);
        SetState(LanguageServerState.Failed);
    }

    private void Kill()
    {
        try
        {
            if (_process is { HasExited: false })
                _process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private async Task DisposeConnectionAsync()
    {
        if (Interlocked.Exchange(ref _connection, null) is { } connection)
            await connection.DisposeAsync().ConfigureAwait(false);
    }

    private void SetState(LanguageServerState state)
    {
        if (Interlocked.Exchange(ref _state, (int)state) != (int)state)
            StateChanged?.Invoke(this, state);
    }
}
