using System.IO.Pipes;
using Runesmith.Lsp;
using Runesmith.Lsp.Protocol;

namespace Runesmith.LanguageServer.Tests;

public sealed class CatalogTests
{
    [Fact]
    public async Task CompletesCSharpThroughTheProtocol()
    {
        using var toServer = new AnonymousPipeServerStream(PipeDirection.Out);
        using var toClient = new AnonymousPipeServerStream(PipeDirection.Out);
        await using var server = new LanguageServer(new AnonymousPipeClientStream(PipeDirection.In, toServer.ClientSafePipeHandle), toClient,
            AnalyzerCatalog.Create(), Path.GetTempPath());
        await using var client = new JsonRpcConnection(new AnonymousPipeClientStream(PipeDirection.In, toClient.ClientSafePipeHandle), toServer);
        client.OnNotification("textDocument/publishDiagnostics", _ => { });
        client.OnNotification("window/logMessage", _ => { });
        server.Start();
        client.Start();
        var token = TestContext.Current.CancellationToken;

        await client.SendRequestAsync<InitializeResult>("initialize", new { processId = 1, rootUri = (string?)null, capabilities = new { } }, token);
        await client.SendNotificationAsync("initialized", new { });
        var uri = new Uri(Path.Combine(Path.GetTempPath(), "Program.cs")).AbsoluteUri;
        await client.SendNotificationAsync("textDocument/didOpen", new DidOpenTextDocumentParams(new TextDocumentItem(uri, "csharp", 1, "Console.Wri")));
        var list = await client.SendRequestAsync<CompletionList>("textDocument/completion",
            new CompletionParams(new TextDocumentIdentifier(uri), new Position(0, 11), null), token);

        Assert.Contains(list?.Items ?? [], item => item.Label == "WriteLine");
    }

    [Fact]
    public async Task CompletesJavaThroughTheProtocol()
    {
        using var toServer = new AnonymousPipeServerStream(PipeDirection.Out);
        using var toClient = new AnonymousPipeServerStream(PipeDirection.Out);
        await using var server = new LanguageServer(new AnonymousPipeClientStream(PipeDirection.In, toServer.ClientSafePipeHandle), toClient,
            AnalyzerCatalog.Create(), Path.GetTempPath());
        await using var client = new JsonRpcConnection(new AnonymousPipeClientStream(PipeDirection.In, toClient.ClientSafePipeHandle), toServer);
        client.OnNotification("textDocument/publishDiagnostics", _ => { });
        client.OnNotification("window/logMessage", _ => { });
        server.Start();
        client.Start();
        var token = TestContext.Current.CancellationToken;

        await client.SendRequestAsync<InitializeResult>("initialize", new { processId = 1, rootUri = (string?)null, capabilities = new { } }, token);
        await client.SendNotificationAsync("initialized", new { });
        var uri = new Uri(Path.Combine(Path.GetTempPath(), "Main.java")).AbsoluteUri;
        const string Text = "class Main { void run() { String s = \"\"; s. } }";
        await client.SendNotificationAsync("textDocument/didOpen", new DidOpenTextDocumentParams(new TextDocumentItem(uri, "java", 1, Text)));
        var list = await client.SendRequestAsync<CompletionList>("textDocument/completion",
            new CompletionParams(new TextDocumentIdentifier(uri), new Position(0, Text.IndexOf("s. ", StringComparison.Ordinal) + 2), null), token);

        Assert.Contains(list?.Items ?? [], item => item.Label == "length");
    }
}
