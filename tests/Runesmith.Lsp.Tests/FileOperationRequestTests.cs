using System.Text.Json;
using System.Threading.Channels;
using Runesmith.Lsp.Protocol;

namespace Runesmith.Lsp.Tests;

public sealed class FileOperationRequestTests
{
    private const string Filters = """
        {
          "workspace": {
            "workspaceFolders": { "supported": true },
            "fileOperations": {
              "willRename": { "filters": [
                { "scheme": "file", "pattern": { "glob": "**/*.src", "matches": "file", "options": { "ignoreCase": true } } },
                { "pattern": { "glob": "**/Models", "matches": "folder" } },
                { "scheme": "untitled", "pattern": { "glob": "**" } } ] },
              "didRename": { "filters": [ { "pattern": { "glob": "**/*.src" } } ] },
              "didCreate": { "filters": [ { "pattern": { "glob": "**/*.src", "matches": "file" } } ] },
              "didDelete": { "filters": [ { "pattern": { "glob": "**/Models", "matches": "folder" } } ] }
            }
          }
        }
        """;

    private static readonly string Root = Path.Combine(Path.GetTempPath(), "work");

    private static readonly string[] Operations = ["didCreate", "willCreate", "didRename", "willRename", "didDelete", "willDelete"];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DeclaresFileOperationsAndReadsTheFiltersOfTheServer()
    {
        await using var server = new FakeServer(Filters);
        await server.StartAsync();

        var declared = server.Parameters("initialize").GetProperty("capabilities").GetProperty("workspace").GetProperty("fileOperations");
        Assert.False(declared.GetProperty("dynamicRegistration").GetBoolean());
        Assert.All(Operations, name => Assert.True(declared.GetProperty(name).GetBoolean()));
        var willRename = server.Client.Capabilities.Workspace?.FileOperations?.WillRename?.Filters;
        Assert.Equal(3, willRename?.Count);
        Assert.Equal(FileOperationPatternKind.File, willRename![0].Pattern?.Matches);
        Assert.True(willRename[0].Pattern?.Options?.IgnoreCase);
        Assert.Null(server.Client.Capabilities.Workspace?.FileOperations?.WillCreate);
    }

    [Fact]
    public async Task AsksBeforeARenameAboutTheFilesAndFoldersItsFiltersMatch()
    {
        var program = LspUri.FromPath(Path.Combine(Root, "Program.src"));
        await using var server = new FakeServer(Filters);
        server.Answer("workspace/willRenameFiles", _ => $$"""
            { "changes": { "{{program}}": [ { "range": { "start": { "line": 0, "character": 0 }, "end": { "line": 0, "character": 0 } }, "newText": "using Entities;\n" } ] } }
            """);
        await server.StartAsync();

        var edit = await server.Client.WillChangeFilesAsync(FileOperationKind.Rename,
        [
            new FileOperationTarget(Path.Combine(Root, "A.SRC"), Path.Combine(Root, "B.SRC"), IsFolder: false),
            new FileOperationTarget(Path.Combine(Root, "notes.txt"), Path.Combine(Root, "todo.txt"), IsFolder: false),
            new FileOperationTarget(Path.Combine(Root, "Models"), Path.Combine(Root, "Entities"), IsFolder: true),
            new FileOperationTarget(Path.Combine(Root, "Old.src"), Path.Combine(Root, "New.src"), IsFolder: true),
            new FileOperationTarget(Path.Combine(Root, "readme.txt"), Path.Combine(Root, "Readme.src"), IsFolder: false),
        ], Token);

        var files = server.Parameters("workspace/willRenameFiles").GetProperty("files").EnumerateArray()
            .Select(file => (file.GetProperty("oldUri").GetString(), file.GetProperty("newUri").GetString()));
        Assert.Equal(
            [
                (Uri(Root, "A.SRC"), Uri(Root, "B.SRC")),
                (Uri(Root, "Models"), Uri(Root, "Entities")),
                (Uri(Root, "readme.txt"), Uri(Root, "Readme.src")),
            ],
            files);
        var (uri, _, edits) = Assert.Single(edit!.Documents);
        Assert.Equal((program, "using Entities;\n"), (uri, Assert.Single(edits).NewText));
    }

    [Fact]
    public async Task AsksNothingWhenTheServerWantsNoneOfTheFiles()
    {
        await using var server = new FakeServer(Filters);
        await server.StartAsync();

        Assert.Null(await server.Client.WillChangeFilesAsync(FileOperationKind.Create, [new FileOperationTarget(Path.Combine(Root, "A.src"), IsFolder: false)], Token));
        Assert.Null(await server.Client.WillChangeFilesAsync(FileOperationKind.Rename,
            [new FileOperationTarget(Path.Combine(Root, "a.txt"), Path.Combine(Root, "b.txt"), IsFolder: false)], Token));
        Assert.Null(await server.Client.WillChangeFilesAsync(FileOperationKind.Delete, [new FileOperationTarget(Path.Combine(Root, "A.src"))], Token));
        Assert.DoesNotContain(server.Received, r => r.Method != "initialize");
    }

    [Fact]
    public async Task TellsOfTheCreatedRenamedAndDeletedFilesItsFiltersMatch()
    {
        await using var server = new FakeServer(Filters);
        var created = server.Listen("workspace/didCreateFiles");
        var renamed = server.Listen("workspace/didRenameFiles");
        var deleted = server.Listen("workspace/didDeleteFiles");
        await server.StartAsync();

        await server.Client.DidChangeFilesAsync(FileOperationKind.Create,
            [new FileOperationTarget(Path.Combine(Root, "A.src"), IsFolder: false), new FileOperationTarget(Path.Combine(Root, "B.txt"), IsFolder: false)]);
        await server.Client.DidChangeFilesAsync(FileOperationKind.Rename, [new FileOperationTarget(Path.Combine(Root, "A.src"), Path.Combine(Root, "C.src"), IsFolder: false)]);
        await server.Client.DidChangeFilesAsync(FileOperationKind.Delete,
            [new FileOperationTarget(Path.Combine(Root, "Models")), new FileOperationTarget(Path.Combine(Root, "Models.src"), IsFolder: false)]);

        Assert.Equal([Uri(Root, "A.src")], Uris(await NextAsync(created), "uri"));
        Assert.Equal([Uri(Root, "C.src")], Uris(await NextAsync(renamed), "newUri"));
        Assert.Equal([Uri(Root, "Models")], Uris(await NextAsync(deleted), "uri"));
    }

    [Theory]
    [InlineData("**/*.{ts,js}", "/w/src/a.ts", true)]
    [InlineData("**/*.{ts,js}", "/w/a.js", true)]
    [InlineData("**/*.{ts,js}", "/w/a.tsx", false)]
    [InlineData("**/{src,test}/**/*.src", "/w/test/unit/A.src", true)]
    [InlineData("**/{src,test}/**/*.src", "/w/docs/A.src", false)]
    [InlineData("/w/*.src", "/w/A.src", true)]
    [InlineData("/w/*.src", "/w/sub/A.src", false)]
    [InlineData("**/example.[0-9]", "/w/example.1", true)]
    [InlineData("**/example.[0-9]", "/w/example.a", false)]
    [InlineData("**/example.[!0-9]", "/w/example.a", true)]
    [InlineData("**/example.[!0-9]", "/w/example.1", false)]
    [InlineData("**/a?c", "/w/abc", true)]
    [InlineData("**/a?c", "/w/a/c", false)]
    [InlineData("**", "/any/path/at/all", true)]
    [InlineData("**/a+b (1).src", "/w/a+b (1).src", true)]
    [InlineData("**/[x", "/w/[x", true)]
    public void GlobsMatchWholePaths(string glob, string path, bool matches) =>
        Assert.Equal(matches, FileOperationFilters.ToRegex(glob, ignoreCase: false).IsMatch(path));

    [Fact]
    public void CaseCountsUnlessThePatternIgnoresIt()
    {
        Assert.Matches(FileOperationFilters.ToRegex("**/*.SRC", ignoreCase: true), "/w/a.src");
        Assert.DoesNotMatch(FileOperationFilters.ToRegex("**/*.SRC", ignoreCase: false), "/w/a.src");
    }

    private static string Uri(string folder, string name) => LspUri.FromPath(Path.Combine(folder, name));

    private static List<string?> Uris(JsonElement? parameters, string property) =>
        [.. parameters!.Value.GetProperty("files").EnumerateArray().Select(file => file.GetProperty(property).GetString())];

    private static async Task<JsonElement?> NextAsync(ChannelReader<JsonElement?> notifications) =>
        await notifications.ReadAsync(Token).AsTask().WaitAsync(TimeSpan.FromSeconds(10), Token);
}
