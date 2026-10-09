using System.IO.Pipes;
using System.Text.Json;

namespace Runesmith.Lsp.Tests;

/// <summary>A language server at the other end of two pipes, answering requests with JSON the test gives, and a client connected to it.</summary>
internal sealed class FakeServer : IAsyncDisposable
{
    private readonly AnonymousPipeServerStream toServer = new(PipeDirection.Out);
    private readonly AnonymousPipeServerStream toClient = new(PipeDirection.Out);

    public FakeServer(string capabilities)
    {
        Connection = new JsonRpcConnection(new AnonymousPipeClientStream(PipeDirection.In, toServer.ClientSafePipeHandle), toClient);
        Answer("initialize", _ => $$"""{ "capabilities": {{capabilities}} }""");
        Connection.OnNotification("initialized", _ => { });
        Connection.OnRequest("shutdown", (_, _) => Task.FromResult<object?>(null));
        Connection.OnNotification("exit", _ => { });
        Client = new LanguageClient(new LanguageServerOptions { Command = "fake-server", RootPath = Path.GetTempPath(), ShutdownTimeout = TimeSpan.FromSeconds(1) });
    }

    public JsonRpcConnection Connection { get; }

    public LanguageClient Client { get; }

    /// <summary>Gets the parameters of every request the server got, by method.</summary>
    public List<(string Method, JsonElement? Parameters)> Received { get; } = [];

    public async Task StartAsync()
    {
        Connection.Start();
        await Client.StartAsync(new AnonymousPipeClientStream(PipeDirection.In, toClient.ClientSafePipeHandle), toServer, TestContext.Current.CancellationToken);
    }

    /// <summary>Answers a method with JSON made from the request's parameters.</summary>
    public void Answer(string method, Func<JsonElement?, string> json) =>
        Connection.OnRequest(method, (parameters, _) =>
        {
            lock (Received)
                Received.Add((method, parameters?.Clone()));
            using var document = JsonDocument.Parse(json(parameters));
            return Task.FromResult<object?>(document.RootElement.Clone());
        });

    public JsonElement Parameters(string method)
    {
        lock (Received)
            return Received.Last(r => r.Method == method).Parameters ?? throw new InvalidOperationException($"{method} had no parameters.");
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await Connection.DisposeAsync();
        toServer.Dispose();
        toClient.Dispose();
    }
}
