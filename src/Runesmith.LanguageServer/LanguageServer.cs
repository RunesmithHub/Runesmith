using System.Collections.Concurrent;
using System.Text.Json;
using Runesmith.LanguageServices;
using Runesmith.Lsp;
using Runesmith.Lsp.Protocol;
using Runesmith.Text;
using CompletionTriggerKind = Runesmith.LanguageServices.CompletionTriggerKind;
using LspCompletionItem = Runesmith.Lsp.Protocol.CompletionItem;
using LspCompletionItemKind = Runesmith.Lsp.Protocol.CompletionItemKind;
using LspCompletionList = Runesmith.Lsp.Protocol.CompletionList;
using LspDiagnostic = Runesmith.Lsp.Protocol.Diagnostic;
using LspDiagnosticSeverity = Runesmith.Lsp.Protocol.DiagnosticSeverity;
using LspRange = Runesmith.Lsp.Protocol.Range;

namespace Runesmith.LanguageServer;

/// <summary>Serves language analyzers over the Language Server Protocol: keeps the client's documents, answers completion, hover,
/// definition and signature help, and publishes problems.</summary>
public sealed class LanguageServer : IAsyncDisposable
{
    private const int ResolveEntriesKept = 4096;

    private readonly JsonRpcConnection connection;
    private readonly LanguageServiceHost host;
    private readonly ConcurrentDictionary<string, string> paths = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<long, (CompletionEntry Entry, string Path)> entries = new();
    private readonly TaskCompletionSource exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long nextEntry;

    /// <param name="cacheDirectory">Where analyzers keep caches between runs.</param>
    public LanguageServer(Stream input, Stream output, IEnumerable<ILanguageAnalyzer> analyzers, string cacheDirectory)
    {
        connection = new JsonRpcConnection(input, output);
        host = new LanguageServiceHost(cacheDirectory, (name, line) => _ = connection.SendNotificationAsync("window/logMessage",
            new LogMessageParams(MessageType.Log, $"{name}: {line}")));
        foreach (var analyzer in analyzers)
            host.Add(analyzer);

        host.ProblemsFound += Publish;
        connection.Closed += (_, _) => exited.TrySetResult();
        Register();
    }

    /// <summary>Completes when the client sends <c>exit</c> or closes the connection.</summary>
    public Task Exited => exited.Task;

    /// <summary>Starts reading requests.</summary>
    public void Start() => connection.Start();

    public async ValueTask DisposeAsync()
    {
        await host.DisposeAsync().ConfigureAwait(false);
        await connection.DisposeAsync().ConfigureAwait(false);
    }

    private void Register()
    {
        connection.OnRequest("initialize", async (parameters, cancellationToken) =>
        {
            var root = parameters?.TryGetProperty("rootUri", out var rootUri) == true && rootUri.ValueKind == JsonValueKind.String
                ? LspUri.ToPath(rootUri.GetString()!)
                : null;
            await host.OpenWorkspaceAsync(root, cancellationToken).ConfigureAwait(false);
            return new InitializeResult(Capabilities(), new ServerInfo("runesmith-language-server", typeof(LanguageServer).Assembly.GetName().Version?.ToString(3)));
        });
        connection.OnRequest("shutdown", (_, _) => Task.FromResult<object?>(null));
        connection.OnNotification("initialized", _ => { });
        connection.OnNotification("exit", _ => exited.TrySetResult());
        connection.OnNotification("textDocument/didOpen", parameters => Read(parameters, LspJsonContext.Default.DidOpenTextDocumentParams, Open));
        connection.OnNotification("textDocument/didChange", parameters => Read(parameters, LspJsonContext.Default.DidChangeTextDocumentParams, Change));
        connection.OnNotification("textDocument/didClose", parameters => Read(parameters, LspJsonContext.Default.DidCloseTextDocumentParams, p =>
        {
            if (paths.TryRemove(p.TextDocument.Uri, out var path))
                host.Close(path);
        }));
        connection.OnRequest("textDocument/completion", CompleteAsync);
        connection.OnRequest("completionItem/resolve", ResolveAsync);
        connection.OnRequest("textDocument/hover", (parameters, token) => PositionRequest(parameters, async (path, document, offset) =>
            await host.HoverAsync(path, document.Snapshot, offset, token).ConfigureAwait(false) is { } hover
                ? new Hover(new MarkupContent("markdown", hover.Markdown), hover.Span is { } span ? RangeOf(document.Snapshot, span) : null)
                : null));
        connection.OnRequest("textDocument/definition", (parameters, token) => PositionRequest(parameters, async (path, document, offset) =>
        {
            var locations = await host.DefinitionAsync(path, document.Snapshot, offset, token).ConfigureAwait(false);
            return locations.Select(l => new Location(LspUri.FromPath(l.Path), new LspRange(ToPosition(l.Start), ToPosition(l.End ?? l.Start)))).ToArray();
        }));
        connection.OnRequest("textDocument/signatureHelp", (parameters, token) => PositionRequest(parameters, async (path, document, offset) =>
        {
            if (await host.SignatureHelpAsync(path, document.Snapshot, offset, token).ConfigureAwait(false) is not { } help)
                return null;

            return new SignatureHelp(
                [
                    .. help.Signatures.Select(s => new SignatureInformation(
                        s.Label,
                        s.Documentation is null ? null : new MarkupContent("markdown", s.Documentation),
                        [.. s.Parameters.Select(p => new ParameterInformation(new ParameterLabel(null, p.Start, p.End), null))],
                        null)),
                ],
                help.ActiveSignature,
                help.ActiveParameter);
        }));
    }

    private ServerCapabilities Capabilities()
    {
        var analyzers = host.Analyzers;
        return new ServerCapabilities
        {
            TextDocumentSync = new TextDocumentSyncOptions(true, TextDocumentSyncKind.Incremental, false, false),
            CompletionProvider = new CompletionOptions([.. analyzers.SelectMany(a => a.CompletionTriggerCharacters).Distinct().Select(c => c.ToString())], null, true),
            HoverProvider = true,
            DefinitionProvider = true,
            SignatureHelpProvider = new SignatureHelpOptions(
                [.. analyzers.SelectMany(a => a.SignatureHelpTriggerCharacters).Distinct().Select(c => c.ToString())],
                [.. analyzers.SelectMany(a => a.SignatureHelpRetriggerCharacters).Distinct().Select(c => c.ToString())]),
        };
    }

    private void Open(DidOpenTextDocumentParams parameters)
    {
        var item = parameters.TextDocument;
        var path = LspUri.ToPath(item.Uri) ?? item.Uri;
        paths[item.Uri] = path;
        host.Open(path, item.LanguageId, TextSnapshot.Create(item.Text));
    }

    private void Change(DidChangeTextDocumentParams parameters)
    {
        if (!paths.TryGetValue(parameters.TextDocument.Uri, out var path) || host.GetDocument(path) is not { } document)
            return;

        // Each change event applies to the text as the events before it left it.
        var snapshot = document.Snapshot;
        foreach (var change in parameters.ContentChanges)
        {
            var span = change.Range is { } range
                ? TextSpan.FromBounds(OffsetOf(snapshot, range.Start), OffsetOf(snapshot, range.End))
                : new TextSpan(0, snapshot.Length);
            var edit = new TextChange(span, change.Text);
            var next = snapshot.Apply([edit]);
            host.Change(path, next, [edit]);
            snapshot = next;
        }
    }

    private async Task<object?> CompleteAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        if (parameters?.Deserialize(LspJsonContext.Default.CompletionParams) is not { } request
            || !paths.TryGetValue(request.TextDocument.Uri, out var path)
            || host.GetDocument(path) is not { } document)
        {
            return LspCompletionList.Empty;
        }

        var offset = OffsetOf(document.Snapshot, request.Position);
        var trigger = request.Context?.TriggerKind switch
        {
            Lsp.Protocol.CompletionTriggerKind.TriggerCharacter => CompletionTriggerKind.Character,
            Lsp.Protocol.CompletionTriggerKind.TriggerForIncompleteCompletions => CompletionTriggerKind.Incomplete,
            _ => CompletionTriggerKind.Invoked,
        };
        var character = request.Context?.TriggerCharacter is { Length: 1 } text ? text[0] : (char?)null;
        var result = await host.CompleteAsync(path, document.Snapshot, offset, trigger, character, cancellationToken).ConfigureAwait(false);
        var range = RangeOf(document.Snapshot, result.ReplaceSpan);
        if (entries.Count > ResolveEntriesKept)
            entries.Clear();

        var items = new LspCompletionItem[result.Items.Count];
        for (var i = 0; i < items.Length; i++)
        {
            var entry = result.Items[i];
            var id = Interlocked.Increment(ref nextEntry);
            entries[id] = (entry, path);
            items[i] = new LspCompletionItem
            {
                Label = entry.Label,
                Kind = (LspCompletionItemKind)((int)entry.Kind + 1),
                Detail = entry.Detail,
                FilterText = entry.FilterText,
                SortText = i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture),
                Edit = new CompletionEdit(entry.InsertText ?? entry.Label, range, null, null),
                Data = JsonDocument.Parse(id.ToString(System.Globalization.CultureInfo.InvariantCulture)).RootElement.Clone(),
            };
        }

        return new LspCompletionList(result.IsIncomplete, items);
    }

    private async Task<object?> ResolveAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        if (parameters?.Deserialize(LspJsonContext.Default.CompletionItem) is not { } item)
            return null;

        if (item.Data is not { ValueKind: JsonValueKind.Number } data || !entries.TryGetValue(data.GetInt64(), out var stored)
            || await host.ResolveAsync(stored.Entry, cancellationToken).ConfigureAwait(false) is not { } details)
        {
            return item;
        }

        var document = host.GetDocument(stored.Path);
        return item with
        {
            Detail = details.Detail ?? item.Detail,
            Documentation = details.Documentation is null ? item.Documentation : new MarkupContent("markdown", details.Documentation),
            AdditionalTextEdits = document is null || details.AdditionalChanges.Count == 0
                ? item.AdditionalTextEdits
                : [.. details.AdditionalChanges.Select(c => new TextEdit(RangeOf(document.Snapshot, c.Span), c.NewText))],
        };
    }

    private async Task<object?> PositionRequest<T>(JsonElement? parameters, Func<string, SourceDocument, int, Task<T>> answer)
    {
        if (parameters?.Deserialize(LspJsonContext.Default.TextDocumentPositionParams) is not { } request
            || !paths.TryGetValue(request.TextDocument.Uri, out var path)
            || host.GetDocument(path) is not { } document)
        {
            return null;
        }

        return await answer(path, document, OffsetOf(document.Snapshot, request.Position)).ConfigureAwait(false);
    }

    private void Publish(SourceDocument document, IReadOnlyList<Problem> problems)
    {
        var uri = paths.FirstOrDefault(pair => pair.Value == document.Path).Key ?? LspUri.FromPath(document.Path);
        var diagnostics = problems.Select(p => new LspDiagnostic(
            RangeOf(document.Snapshot, p.Span),
            p.Severity switch
            {
                ProblemSeverity.Error => LspDiagnosticSeverity.Error,
                ProblemSeverity.Warning => LspDiagnosticSeverity.Warning,
                ProblemSeverity.Information => LspDiagnosticSeverity.Information,
                _ => LspDiagnosticSeverity.Hint,
            },
            p.Code,
            "runesmith",
            p.Message,
            null)).ToArray();
        _ = connection.SendNotificationAsync("textDocument/publishDiagnostics", new PublishDiagnosticsParams(uri, document.Version, diagnostics));
    }

    private static void Read<T>(JsonElement? parameters, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type, Action<T> handle)
        where T : class
    {
        if (parameters is { } element && element.Deserialize(type) is { } value)
            handle(value);
    }

    private static int OffsetOf(TextSnapshot snapshot, Position position) => snapshot.GetOffset(new TextPosition(position.Line, position.Character));

    private static LspRange RangeOf(TextSnapshot snapshot, TextSpan span) =>
        new(ToPosition(snapshot.GetPosition(Math.Min(span.Start, snapshot.Length))), ToPosition(snapshot.GetPosition(Math.Min(span.End, snapshot.Length))));

    private static Position ToPosition(TextPosition position) => new(position.Line, position.Column);
}
