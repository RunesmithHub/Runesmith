using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Runesmith.Languages.Servers;
using Runesmith.Languages.Tests.Analyzers;
using Runesmith.Lsp;
using Runesmith.Lsp.Tests;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Shell;
using Runesmith.Text;

namespace Runesmith.Languages.Tests.Servers;

public sealed class LanguageServerFileOperationsTests : IAsyncDisposable
{
    private const string Filters = """
        {
          "workspace": {
            "fileOperations": {
              "willRename": { "filters": [ { "pattern": { "glob": "**/*.cs", "matches": "file" } }, { "pattern": { "glob": "**", "matches": "folder" } } ] },
              "didRename": { "filters": [ { "pattern": { "glob": "**", "matches": "folder" } } ] },
              "willDelete": { "filters": [ { "pattern": { "glob": "**/*.cs", "matches": "file" } } ] },
              "didDelete": { "filters": [ { "pattern": { "glob": "**/*.cs", "matches": "file" } } ] },
              "didCreate": { "filters": [ { "pattern": { "glob": "**", "matches": "folder" } } ] }
            }
          }
        }
        """;

    private readonly string folder = Directory.CreateTempSubdirectory("runesmith-lsp-files-").FullName;
    private readonly TestDocuments documents = new();
    private readonly TestOutput output = new();
    private readonly List<(ServerSession Session, FakeServer Server)> started = [];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        foreach (var (session, server) in started)
        {
            await session.DisposeAsync();
            await server.DisposeAsync();
        }

        Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public async Task AsksTheServersBeforeTheFilesChangeAndAddsTheirChanges()
    {
        var program = Write("Program.cs", "class Program { Models.Order order; }\n");
        var models = Directory.CreateDirectory(Path.Combine(folder, "Models")).FullName;
        var billing = Path.Combine(folder, "Billing");
        var (session, server) = await StartAsync("fake", fake => fake.Answer("workspace/willRenameFiles", _ => Change(program, 16, 22, "Billing")));
        var participant = new LanguageServerFileOperations(() => [session], documents);

        var edit = await participant.WillApplyAsync([new RenameFileOperation(models, billing), new CreateFileOperation(Path.Combine(folder, "notes.txt"))], Token);

        var document = Assert.Single(edit!.Documents);
        Assert.Equal((program, new TextChange(new TextSpan(16, 6), "Billing")), (document.FilePath, Assert.Single(document.Changes)));
        var renamed = Assert.Single(server.Parameters("workspace/willRenameFiles").GetProperty("files").EnumerateArray());
        Assert.Equal((LspUri.FromPath(models), LspUri.FromPath(billing)), (renamed.GetProperty("oldUri").GetString(), renamed.GetProperty("newUri").GetString()));
        Assert.DoesNotContain(server.Received, request => request.Method == "workspace/willCreateFiles");
    }

    [Fact]
    public async Task TellsTheServersAfterwardsAndRemembersWhetherDeletedPathsWereFolders()
    {
        var file = Write("Order.cs", "class Order {}");
        var oldFolder = Directory.CreateDirectory(Path.Combine(folder, "Old.cs")).FullName;
        var newFolder = Path.Combine(folder, "New");
        var (session, server) = await StartAsync("fake", fake => fake.Answer("workspace/willDeleteFiles", _ => "null"));
        var created = server.Listen("workspace/didCreateFiles");
        var deleted = server.Listen("workspace/didDeleteFiles");
        var participant = new LanguageServerFileOperations(() => [session], documents);
        FileOperation[] operations = [new DeleteFileOperation(file), new DeleteFileOperation(oldFolder) { Recursive = true }, new CreateFolderOperation(newFolder)];

        Assert.Null(await participant.WillApplyAsync(operations, Token));
        File.Delete(file);
        Directory.Delete(oldFolder);
        Directory.CreateDirectory(newFolder);
        await participant.DidApplyAsync(operations, Token);

        Assert.Equal([LspUri.FromPath(file)], Uris(server.Parameters("workspace/willDeleteFiles"), "uri"));
        Assert.Equal([LspUri.FromPath(file)], Uris(await NextAsync(deleted), "uri"));
        Assert.Equal([LspUri.FromPath(newFolder)], Uris(await NextAsync(created), "uri"));
    }

    [Fact]
    public async Task LeavesOutTheChangesOfAServerThatIsLateAndSaysSo()
    {
        var program = Write("Program.cs", "class Program {}\n");
        var file = Write("A.cs", "class A {}");
        var (fast, _) = await StartAsync("fast", fake => fake.Answer("workspace/willRenameFiles", _ => Change(program, 0, 0, "// renamed\n")));
        var (slow, _) = await StartAsync("slow", fake => fake.Connection.OnRequest("workspace/willRenameFiles", async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return null;
        }));
        var participant = new LanguageServerFileOperations(() => [fast, slow], documents) { ServerBudget = TimeSpan.FromMilliseconds(300) };
        var stopwatch = Stopwatch.StartNew();

        var edit = await participant.WillApplyAsync([new RenameFileOperation(file, Path.Combine(folder, "B.cs"))], Token);

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10));
        Assert.Equal("// renamed\n", Assert.Single(Assert.Single(edit!.Documents).Changes).NewText);
        Assert.Contains(output.Lines, line => line.Contains("slow did not answer workspace/willRenameFiles within", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AsksNothingWithoutRunningServers()
    {
        var participant = new LanguageServerFileOperations(() => [], documents);

        Assert.Null(await participant.WillApplyAsync([new CreateFileOperation(Path.Combine(folder, "A.cs"))], Token));
        await participant.DidApplyAsync([new CreateFileOperation(Path.Combine(folder, "A.cs"))], Token);
    }

    private async Task<(ServerSession Session, FakeServer Server)> StartAsync(string id, Action<FakeServer> answer)
    {
        var server = new FakeServer(Filters);
        answer(server);
        var context = new SessionContext(output, new TestStatusBar(), new TestDiagnostics(), new TestNotifications(), definition => definition.Command, () => false, (_, _) => { })
        {
            StartClient = server.ConnectAsync,
        };
        var session = new ServerSession(new LanguageServerDefinition(id, id, ["csharp"], "fake-server"), folder, context);
        started.Add((session, server));
        session.Start();
        for (var i = 0; i < 400 && session.State != SessionState.Running; i++)
            await Task.Delay(25, Token);

        Assert.Equal(SessionState.Running, session.State);
        return (session, server);
    }

    private string Write(string name, string text)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, text);
        return path;
    }

    private static string Change(string path, int start, int end, string text) => $$"""
        { "changes": { "{{LspUri.FromPath(path)}}": [ { "range": { "start": { "line": 0, "character": {{start}} }, "end": { "line": 0, "character": {{end}} } }, "newText": {{JsonSerializer.Serialize(text)}} } ] } }
        """;

    private static List<string?> Uris(JsonElement? parameters, string property) =>
        [.. parameters!.Value.GetProperty("files").EnumerateArray().Select(file => file.GetProperty(property).GetString())];

    private static async Task<JsonElement?> NextAsync(ChannelReader<JsonElement?> notifications) =>
        await notifications.ReadAsync(Token).AsTask().WaitAsync(TimeSpan.FromSeconds(10), Token);

    private sealed class TestStatusBar : IStatusBar
    {
        public IStatusBarItem AddItem(string id, StatusBarAlignment alignment, int priority = 0) => new Item();

        private sealed class Item : IStatusBarItem
        {
            public string Text { get; set; } = "";

            public string? Icon { get; set; }

            public string? ToolTip { get; set; }

            public StatusBarState State { get; set; }

            public string? CommandId { get; set; }

            public bool IsVisible { get; set; }

            public void Dispose()
            {
            }
        }
    }

    private sealed class TestNotifications : INotificationService
    {
        public void Notify(NotificationKind kind, string title, string? message = null, string? actionText = null, Action? action = null)
        {
        }

        public Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", bool isDestructive = false) => Task.FromResult(true);

        public Task<int?> ChooseAsync(string title, string message, IReadOnlyList<string> choices) => Task.FromResult<int?>(null);
    }
}
