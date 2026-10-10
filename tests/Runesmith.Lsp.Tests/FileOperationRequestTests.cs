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
                { "scheme": "file", "pattern": { "glob": "**/*.cs", "matches": "file", "options": { "ignoreCase": true } } },
                { "pattern": { "glob": "**/Models", "matches": "folder" } },
                { "scheme": "untitled", "pattern": { "glob": "**" } } ] },
              "didRename": { "filters": [ { "pattern": { "glob": "**/*.cs" } } ] },
              "didCreate": { "filters": [ { "pattern": { "glob": "**/*.cs", "matches": "file" } } ] },
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
        var program = LspUri.FromPath(Path.Combine(Root, "Program.cs"));
        await using var server = new FakeServer(Filters);
        server.Answer("workspace/willRenameFiles", _ => $$"""
            { "changes": { "{{program}}": [ { "range": { "start": { "line": 0, "character": 0 }, "end": { "line": 0, "character": 0 } }, "newText": "using Entities;\n" } ] } }
            """);
        await server.StartAsync();

        var edit = await server.Client.WillChangeFilesAsync(FileOperationKind.Rename,
        [
            new FileOperationTarget(Path.Combine(Root, "A.CS"), Path.Combine(Root, "B.CS"), IsFolder: false),
            new FileOperationTarget(Path.Combine(Root, "notes.txt"), Path.Combine(Root, "todo.txt"), IsFolder: false),
            new FileOperationTarget(Path.Combine(Root, "Models"), Path.Combine(Root, "Entities"), IsFolder: true),
            new FileOperationTarget(Path.Combine(Root, "Old.cs"), Path.Combine(Root, "New.cs"), IsFolder: true),
            new FileOperationTarget(Path.Combine(Root, "readme.txt"), Path.Combine(Root, "Readme.cs"), IsFolder: false),
        ], Token);

        var files = server.Parameters("workspace/willRenameFiles").GetProperty("files").EnumerateArray()
            .Select(file => (file.GetProperty("oldUri").GetString(), file.GetProperty("newUri").GetString()));
        Assert.Equal(
            [
                (Uri(Root, "A.CS"), Uri(Root, "B.CS")),
                (Uri(Root, "Models"), Uri(Root, "Entities")),
                (Uri(Root, "readme.txt"), Uri(Root, "Readme.cs")),
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

        Assert.Null(await server.Client.WillChangeFilesAsync(FileOperationKind.Create, [new FileOperationTarget(Path.Combine(Root, "A.cs"), IsFolder: false)], Token));
        Assert.Null(await server.Client.WillChangeFilesAsync(FileOperationKind.Rename,
            [new FileOperationTarget(Path.Combine(Root, "a.txt"), Path.Combine(Root, "b.txt"), IsFolder: false)], Token));
        Assert.Null(await server.Client.WillChangeFilesAsync(FileOperationKind.Delete, [new FileOperationTarget(Path.Combine(Root, "A.cs"))], Token));
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
            [new FileOperationTarget(Path.Combine(Root, "A.cs"), IsFolder: false), new FileOperationTarget(Path.Combine(Root, "B.txt"), IsFolder: false)]);
        await server.Client.DidChangeFilesAsync(FileOperationKind.Rename, [new FileOperationTarget(Path.Combine(Root, "A.cs"), Path.Combine(Root, "C.cs"), IsFolder: false)]);
        await server.Client.DidChangeFilesAsync(FileOperationKind.Delete,
            [new FileOperationTarget(Path.Combine(Root, "Models")), new FileOperationTarget(Path.Combine(Root, "Models.cs"), IsFolder: false)]);

        Assert.Equal([Uri(Root, "A.cs")], Uris(await NextAsync(created), "uri"));
        Assert.Equal([Uri(Root, "C.cs")], Uris(await NextAsync(renamed), "newUri"));
        Assert.Equal([Uri(Root, "Models")], Uris(await NextAsync(deleted), "uri"));
    }

    [Theory]
    [InlineData("**/*.{ts,js}", "/w/src/a.ts", true)]
    [InlineData("**/*.{ts,js}", "/w/a.js", true)]
    [InlineData("**/*.{ts,js}", "/w/a.tsx", false)]
    [InlineData("**/{src,test}/**/*.cs", "/w/test/unit/A.cs", true)]
    [InlineData("**/{src,test}/**/*.cs", "/w/docs/A.cs", false)]
    [InlineData("/w/*.cs", "/w/A.cs", true)]
    [InlineData("/w/*.cs", "/w/sub/A.cs", false)]
    [InlineData("**/example.[0-9]", "/w/example.1", true)]
    [InlineData("**/example.[0-9]", "/w/example.a", false)]
    [InlineData("**/example.[!0-9]", "/w/example.a", true)]
    [InlineData("**/example.[!0-9]", "/w/example.1", false)]
    [InlineData("**/a?c", "/w/abc", true)]
    [InlineData("**/a?c", "/w/a/c", false)]
    [InlineData("**", "/any/path/at/all", true)]
    [InlineData("**/a+b (1).cs", "/w/a+b (1).cs", true)]
    [InlineData("**/[x", "/w/[x", true)]
    public void GlobsMatchWholePaths(string glob, string path, bool matches) =>
        Assert.Equal(matches, FileOperationFilters.ToRegex(glob, ignoreCase: false).IsMatch(path));

    [Fact]
    public void CaseCountsUnlessThePatternIgnoresIt()
    {
        Assert.Matches(FileOperationFilters.ToRegex("**/*.CS", ignoreCase: true), "/w/a.cs");
        Assert.DoesNotMatch(FileOperationFilters.ToRegex("**/*.CS", ignoreCase: false), "/w/a.cs");
    }

    private static string Uri(string folder, string name) => LspUri.FromPath(Path.Combine(folder, name));

    private static List<string?> Uris(JsonElement? parameters, string property) =>
        [.. parameters!.Value.GetProperty("files").EnumerateArray().Select(file => file.GetProperty(property).GetString())];

    private static async Task<JsonElement?> NextAsync(ChannelReader<JsonElement?> notifications) =>
        await notifications.ReadAsync(Token).AsTask().WaitAsync(TimeSpan.FromSeconds(10), Token);
}
