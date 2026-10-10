using System.Text.Json;
using Runesmith.Lsp.Protocol;
using Range = Runesmith.Lsp.Protocol.Range;

namespace Runesmith.Lsp.Tests;

public sealed class EditingRequestTests
{
    private const string Uri = "file:///work/Program.cs";

    private const string AllFeatures = """
        {
          "codeActionProvider": { "resolveProvider": true },
          "renameProvider": { "prepareProvider": true },
          "documentFormattingProvider": true,
          "documentRangeFormattingProvider": {},
          "inlayHintProvider": true,
          "codeLensProvider": { "resolveProvider": true },
          "executeCommandProvider": { "commands": ["fix.all"] }
        }
        """;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReadsProvidersSentAsFlagsOrOptions()
    {
        await using var server = new FakeServer(AllFeatures);
        await server.StartAsync();

        var capabilities = server.Client.Capabilities;
        Assert.True(capabilities.CodeActionProvider?.ResolveProvider);
        Assert.True(capabilities.RenameProvider?.PrepareProvider);
        Assert.True(capabilities.DocumentFormattingProvider);
        Assert.True(capabilities.DocumentRangeFormattingProvider);
        Assert.False(capabilities.InlayHintProvider?.ResolveProvider);
        Assert.Equal(["fix.all"], capabilities.ExecuteCommandProvider?.Commands);
    }

    [Fact]
    public async Task AsksWithoutAProviderNever()
    {
        await using var server = new FakeServer("""{ "renameProvider": false }""");
        await server.StartAsync();

        Assert.Empty(await server.Client.CodeActionsAsync(Uri, Range(0, 0, 0, 1), new CodeActionContext([], null, null), Token));
        Assert.Null(await server.Client.PrepareRenameAsync(Uri, new Position(0, 0), Token));
        Assert.Null(await server.Client.FormattingAsync(Uri, new FormattingOptions(4, true), Token));
        Assert.Empty(await server.Client.InlayHintsAsync(Uri, Range(0, 0, 1, 0), Token));
        Assert.DoesNotContain(server.Received, r => r.Method != "initialize");
    }

    [Fact]
    public async Task SendsTheDiagnosticsAndTheirDataAndReadsActionsAndBareCommands()
    {
        await using var server = new FakeServer(AllFeatures);
        server.Answer("textDocument/codeAction", _ => """
            [
              { "title": "Run fixer", "command": "fix.all", "arguments": [1] },
              { "title": "Add using", "kind": "quickfix", "isPreferred": true,
                "edit": { "changes": { "file:///work/Program.cs": [ { "range": { "start": { "line": 0, "character": 0 }, "end": { "line": 0, "character": 0 } }, "newText": "using System;\n" } ] } } },
              { "title": "Extract method", "kind": "refactor.extract", "data": { "id": 7 } }
            ]
            """);
        await server.StartAsync();
        var diagnostic = new Diagnostic(Range(0, 0, 0, 6), DiagnosticSeverity.Error, "CS0246", "csharp", "Missing type", null)
        {
            Data = JsonDocument.Parse("""{ "fixer": "add-using" }""").RootElement,
        };

        var actions = await server.Client.CodeActionsAsync(Uri, Range(0, 0, 0, 6),
            new CodeActionContext([diagnostic], [CodeActionKinds.QuickFix], CodeActionTriggerKind.Automatic), Token);

        var sent = server.Parameters("textDocument/codeAction");
        Assert.Equal("add-using", sent.GetProperty("context").GetProperty("diagnostics")[0].GetProperty("data").GetProperty("fixer").GetString());
        Assert.Equal("quickfix", sent.GetProperty("context").GetProperty("only")[0].GetString());
        Assert.Equal(2, sent.GetProperty("context").GetProperty("triggerKind").GetInt32());
        Assert.Equal(["Run fixer", "Add using", "Extract method"], actions.Select(a => a.Title));
        Assert.Equal("fix.all", actions[0].Command?.Name);
        Assert.Null(actions[0].Edit);
        var edit = Assert.Single(actions[1].Edit!.Documents);
        Assert.Equal(Uri, edit.Uri);
        Assert.Equal("using System;\n", Assert.Single(edit.Edits).NewText);
        Assert.True(actions[1].IsPreferred);
        Assert.Equal(7, actions[2].Data?.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task ResolvesAnActionWithItsData()
    {
        await using var server = new FakeServer(AllFeatures);
        server.Answer("codeAction/resolve", parameters => $$"""
            { "title": "Extract method", "data": {{parameters!.Value.GetProperty("data").GetRawText()}},
              "edit": { "documentChanges": [ { "textDocument": { "uri": "{{Uri}}", "version": 3 },
                "edits": [ { "range": { "start": { "line": 1, "character": 0 }, "end": { "line": 1, "character": 4 } }, "newText": "Run()" } ] } ] } }
            """);
        await server.StartAsync();

        var resolved = await server.Client.ResolveCodeActionAsync(
            new CodeAction { Title = "Extract method", Data = JsonDocument.Parse("""{ "id": 7 }""").RootElement }, Token);

        var (uri, version, edits) = Assert.Single(resolved.Edit!.Documents);
        Assert.Equal((Uri, 3), (uri, version));
        Assert.Equal("Run()", Assert.Single(edits).NewText);
    }

    [Theory]
    [InlineData("""{ "start": { "line": 2, "character": 4 }, "end": { "line": 2, "character": 9 } }""", true, null)]
    [InlineData("""{ "range": { "start": { "line": 2, "character": 4 }, "end": { "line": 2, "character": 9 } }, "placeholder": "count" }""", true, "count")]
    [InlineData("""{ "defaultBehavior": true }""", false, null)]
    public async Task ReadsEveryFormOfPrepareRename(string answer, bool hasRange, string? placeholder)
    {
        await using var server = new FakeServer(AllFeatures);
        server.Answer("textDocument/prepareRename", _ => answer);
        await server.StartAsync();

        var result = await server.Client.PrepareRenameAsync(Uri, new Position(2, 6), Token);

        Assert.NotNull(result);
        Assert.Equal(hasRange, result.Range is not null);
        Assert.Equal(placeholder, result.Placeholder);
        if (hasRange)
            Assert.Equal(Range(2, 4, 2, 9), result.Range);
    }

    [Fact]
    public async Task APrepareRenameAnswerOfNullMeansNothingToRename()
    {
        await using var server = new FakeServer(AllFeatures);
        server.Answer("textDocument/prepareRename", _ => "null");
        await server.StartAsync();

        Assert.Null(await server.Client.PrepareRenameAsync(Uri, new Position(0, 0), Token));
    }

    [Fact]
    public async Task RenamesWithTheNewName()
    {
        await using var server = new FakeServer(AllFeatures);
        server.Answer("textDocument/rename", parameters => $$"""
            { "changes": {
                "{{Uri}}": [ { "range": { "start": { "line": 0, "character": 4 }, "end": { "line": 0, "character": 9 } }, "newText": "{{parameters!.Value.GetProperty("newName").GetString()}}" } ],
                "file:///work/Other.cs": [ { "range": { "start": { "line": 5, "character": 1 }, "end": { "line": 5, "character": 6 } }, "newText": "total" } ] } }
            """);
        await server.StartAsync();

        var edit = await server.Client.RenameAsync(Uri, new Position(0, 5), "total", Token);

        Assert.Equal(2, edit!.Documents.Count());
        Assert.All(edit.Documents, document => Assert.Equal("total", Assert.Single(document.Edits).NewText));
    }

    [Fact]
    public async Task ReadsRenamesThatCreateRenameAndDeleteFilesInOrderAndDeclaresThem()
    {
        await using var server = new FakeServer(AllFeatures);
        server.Answer("textDocument/rename", _ => """
            { "documentChanges": [
                { "kind": "create", "uri": "file:///work/New.cs", "options": { "ignoreIfExists": true } },
                { "textDocument": { "uri": "file:///work/New.cs", "version": null },
                  "edits": [ { "range": { "start": { "line": 0, "character": 0 }, "end": { "line": 0, "character": 0 } }, "newText": "class New {}" } ] },
                { "kind": "rename", "oldUri": "file:///work/Program.cs", "newUri": "file:///work/Main.cs", "options": { "overwrite": true } },
                { "kind": "delete", "uri": "file:///work/obj", "options": { "recursive": true, "ignoreIfNotExists": true } } ] }
            """);
        await server.StartAsync();

        var edit = await server.Client.RenameAsync(Uri, new Position(0, 5), "Main", Token);

        Assert.Collection(edit!.DocumentChanges!,
            change => Assert.Equal(new CreateFile("file:///work/New.cs", new CreateFileOptions(null, true)), change),
            change => Assert.Equal("class New {}", Assert.Single(Assert.IsType<TextDocumentEdit>(change).Edits).NewText),
            change => Assert.Equal(new RenameFile(Uri, "file:///work/Main.cs", new RenameFileOptions(true, null)), change),
            change => Assert.Equal(new DeleteFile("file:///work/obj", new DeleteFileOptions(true, true)), change));
        Assert.Single(edit.Documents);
        var workspaceEdit = server.Parameters("initialize").GetProperty("capabilities").GetProperty("workspace").GetProperty("workspaceEdit");
        Assert.Equal(["create", "rename", "delete"], workspaceEdit.GetProperty("resourceOperations").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("transactional", workspaceEdit.GetProperty("failureHandling").GetString());
    }

    [Fact]
    public void WritesResourceOperationsWithTheirKind()
    {
        var edit = new WorkspaceEdit { DocumentChanges = [new DeleteFile("file:///work/a.txt"), new TextDocumentEdit(new OptionalVersionedTextDocumentIdentifier(Uri, 3), [])] };

        var json = JsonSerializer.Serialize(edit, LspJsonContext.Default.WorkspaceEdit);

        Assert.Contains("""{"uri":"file:///work/a.txt","kind":"delete"}""", json, StringComparison.Ordinal);
        var read = JsonSerializer.Deserialize(json, LspJsonContext.Default.WorkspaceEdit)!.DocumentChanges!;
        Assert.Equal(new DeleteFile("file:///work/a.txt"), read[0]);
        Assert.Equal(3, Assert.IsType<TextDocumentEdit>(read[1]).TextDocument.Version);
    }

    [Fact]
    public async Task FormatsWithTheEditorsIndentation()
    {
        await using var server = new FakeServer(AllFeatures);
        server.Answer("textDocument/formatting", _ => """[ { "range": { "start": { "line": 1, "character": 0 }, "end": { "line": 1, "character": 2 } }, "newText": "\t" } ]""");
        server.Answer("textDocument/rangeFormatting", _ => "null");
        await server.StartAsync();

        var edits = await server.Client.FormattingAsync(Uri, new FormattingOptions(8, false), Token);
        var rangeEdits = await server.Client.RangeFormattingAsync(Uri, Range(1, 0, 3, 0), new FormattingOptions(8, false), Token);

        Assert.Equal("\t", Assert.Single(edits!).NewText);
        var options = server.Parameters("textDocument/formatting").GetProperty("options");
        Assert.Equal(8, options.GetProperty("tabSize").GetInt32());
        Assert.False(options.GetProperty("insertSpaces").GetBoolean());
        Assert.Empty(rangeEdits!);
        Assert.Equal(3, server.Parameters("textDocument/rangeFormatting").GetProperty("range").GetProperty("end").GetProperty("line").GetInt32());
    }

    [Fact]
    public async Task ReadsInlayHintLabelsAsTextOrParts()
    {
        await using var server = new FakeServer(AllFeatures);
        server.Answer("textDocument/inlayHint", _ => """
            [
              { "position": { "line": 0, "character": 8 }, "label": "count:", "kind": 2, "paddingRight": true },
              { "position": { "line": 1, "character": 5 }, "label": [ { "value": ": " }, { "value": "List<int>", "tooltip": "A list" } ], "kind": 1, "tooltip": { "kind": "markdown", "value": "**type**" } }
            ]
            """);
        await server.StartAsync();

        var hints = await server.Client.InlayHintsAsync(Uri, Range(0, 0, 10, 0), Token);

        Assert.Equal(["count:", ": List<int>"], hints.Select(h => h.Text));
        Assert.Equal([InlayHintKind.Parameter, InlayHintKind.Type], hints.Select(h => h.Kind!.Value));
        Assert.True(hints[0].PaddingRight);
        Assert.Equal("**type**", hints[1].Tooltip?.Value);
    }

    [Fact]
    public async Task ResolvesCodeLensesThatCameWithoutACommand()
    {
        await using var server = new FakeServer(AllFeatures);
        server.Answer("textDocument/codeLens", _ => """
            [
              { "range": { "start": { "line": 3, "character": 4 }, "end": { "line": 3, "character": 8 } }, "command": { "title": "Run test", "command": "test.run" } },
              { "range": { "start": { "line": 9, "character": 4 }, "end": { "line": 9, "character": 8 } }, "data": { "symbol": "Main" } }
            ]
            """);
        server.Answer("codeLens/resolve", parameters => $$"""
            { "range": {{parameters!.Value.GetProperty("range").GetRawText()}}, "command": { "title": "2 references", "command": "" } }
            """);
        await server.StartAsync();

        var lenses = await server.Client.CodeLensAsync(Uri, Token);

        Assert.Equal(["Run test", "2 references"], lenses.Select(l => l.Command?.Title));
        Assert.Equal("Main", server.Parameters("codeLens/resolve").GetProperty("data").GetProperty("symbol").GetString());
    }

    [Fact]
    public async Task RunsServerCommandsAndAppliesTheEditsTheServerAsksFor()
    {
        await using var server = new FakeServer(AllFeatures);
        server.Answer("workspace/executeCommand", _ => "null");
        await server.StartAsync();
        ApplyWorkspaceEditParams? asked = null;
        server.Client.ApplyEdit = request =>
        {
            asked = request;
            return Task.FromResult(true);
        };

        await server.Client.ExecuteCommandAsync(new Command("Fix all", "fix.all", [JsonDocument.Parse("1").RootElement]), Token);
        var answer = await server.Connection.SendRequestAsync<ApplyWorkspaceEditResult>("workspace/applyEdit",
            new ApplyWorkspaceEditParams("Fix all", new WorkspaceEdit { Changes = new Dictionary<string, IReadOnlyList<TextEdit>> { [Uri] = [new TextEdit(Range(0, 0, 0, 0), "x")] } }),
            Token);

        Assert.Equal("fix.all", server.Parameters("workspace/executeCommand").GetProperty("command").GetString());
        Assert.True(answer?.Applied);
        Assert.Equal("Fix all", asked?.Label);
        Assert.Equal("x", Assert.Single(Assert.Single(asked!.Edit.Documents).Edits).NewText);
    }

    [Fact]
    public async Task RefusesEditsWithoutAnApplierAndPassesOnRefreshes()
    {
        await using var server = new FakeServer(AllFeatures);
        await server.StartAsync();
        var refreshes = 0;
        server.Client.DecorationsRefreshRequested += (_, _) => Interlocked.Increment(ref refreshes);

        var answer = await server.Connection.SendRequestAsync<ApplyWorkspaceEditResult>("workspace/applyEdit",
            new ApplyWorkspaceEditParams(null, new WorkspaceEdit()), Token);
        await server.Connection.SendRequestAsync<JsonElement>("workspace/inlayHint/refresh", null, Token);
        await server.Connection.SendRequestAsync<JsonElement>("workspace/codeLens/refresh", null, Token);

        Assert.False(answer?.Applied);
        Assert.Equal(2, refreshes);
    }

    private static Range Range(int startLine, int startCharacter, int endLine, int endCharacter) =>
        new(new Position(startLine, startCharacter), new Position(endLine, endCharacter));
}
