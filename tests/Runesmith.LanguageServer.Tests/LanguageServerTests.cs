using System.IO.Pipes;
using System.Text.Json;
using Runesmith.LanguageServices.Tests;
using Runesmith.Lsp;
using Runesmith.Lsp.Protocol;

namespace Runesmith.LanguageServer.Tests;

public sealed class LanguageServerTests : IAsyncLifetime
{
    private const string Uri = "file:///work/A.fake";

    private readonly AnonymousPipeServerStream toServer = new(PipeDirection.Out);
    private readonly AnonymousPipeServerStream toClient = new(PipeDirection.Out);
    private readonly LanguageServer server;
    private readonly JsonRpcConnection client;
    private readonly List<PublishDiagnosticsParams> published = [];

    public LanguageServerTests()
    {
        var serverInput = new AnonymousPipeClientStream(PipeDirection.In, toServer.ClientSafePipeHandle);
        var clientInput = new AnonymousPipeClientStream(PipeDirection.In, toClient.ClientSafePipeHandle);
        server = new LanguageServer(serverInput, toClient, [new FakeAnalyzer("WriteLine", "Write", "Read")], Path.GetTempPath());
        client = new JsonRpcConnection(clientInput, toServer);
        client.OnNotification("textDocument/publishDiagnostics", parameters =>
        {
            if (parameters?.Deserialize(LspJsonContext.Default.PublishDiagnosticsParams) is { } value)
                lock (published)
                    published.Add(value);
        });
        client.OnNotification("window/logMessage", _ => { });
    }

    public async ValueTask InitializeAsync()
    {
        server.Start();
        client.Start();
        var result = await client.SendRequestAsync<InitializeResult>("initialize", new { processId = 1, rootUri = "file:///work", capabilities = new { } },
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal(TextDocumentSyncKind.Incremental, result.Capabilities.TextDocumentSync?.Change);
        Assert.Contains(".", result.Capabilities.CompletionProvider?.TriggerCharacters ?? []);
        await client.SendNotificationAsync("initialized", new { });
        await client.SendNotificationAsync("textDocument/didOpen", new DidOpenTextDocumentParams(new TextDocumentItem(Uri, "fake", 1, "Console.Wri\nbad")));
    }

    public async ValueTask DisposeAsync()
    {
        await client.DisposeAsync();
        await server.DisposeAsync();
        toServer.Dispose();
        toClient.Dispose();
    }

    [Fact]
    public async Task CompletesWithTheReplaceRangeAndResolves()
    {
        var list = await client.SendRequestAsync<CompletionList>("textDocument/completion",
            new CompletionParams(new TextDocumentIdentifier(Uri), new Position(0, 11), null), TestContext.Current.CancellationToken);

        Assert.NotNull(list);
        Assert.False(list.IsIncomplete);
        Assert.Equal(["Write", "WriteLine"], list.Items.Select(i => i.Label));
        Assert.Equal(new Lsp.Protocol.Range(new Position(0, 8), new Position(0, 11)), list.Items[0].TextEdit?.Range);

        var resolved = await client.SendRequestAsync<CompletionItem>("completionItem/resolve", list.Items[0], TestContext.Current.CancellationToken);
        Assert.Equal("detail of Write", resolved?.Detail);
    }

    [Fact]
    public async Task AppliesIncrementalChangesInOrder()
    {
        await client.SendNotificationAsync("textDocument/didChange", new DidChangeTextDocumentParams(new VersionedTextDocumentIdentifier(Uri, 2),
        [
            new TextDocumentContentChangeEvent(new Lsp.Protocol.Range(new Position(0, 0), new Position(0, 7)), "System.Console"),
            new TextDocumentContentChangeEvent(new Lsp.Protocol.Range(new Position(0, 18), new Position(0, 18)), "teL"),
        ]));

        var hover = await client.SendRequestAsync<Hover>("textDocument/hover",
            new TextDocumentPositionParams(new TextDocumentIdentifier(Uri), new Position(0, 21)), TestContext.Current.CancellationToken);

        Assert.Equal("hover at 21 in version 3", hover?.Contents.Value);
    }

    [Fact]
    public async Task PublishesProblems()
    {
        for (var i = 0; i < 200 && published.Count == 0; i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);

        var diagnostics = Assert.Single(published);
        Assert.Equal(Uri, diagnostics.Uri);
        var problem = Assert.Single(diagnostics.Diagnostics);
        Assert.Equal(new Lsp.Protocol.Range(new Position(1, 0), new Position(1, 3)), problem.Range);
    }

    [Fact]
    public async Task AnswersDefinitionsAsLocations()
    {
        var locations = await client.SendRequestAsync<Location[]>("textDocument/definition",
            new TextDocumentPositionParams(new TextDocumentIdentifier(Uri), new Position(0, 2)), TestContext.Current.CancellationToken);

        var location = Assert.Single(locations ?? []);
        Assert.Equal(new Position(0, 0), location.Range.Start);
    }
}
