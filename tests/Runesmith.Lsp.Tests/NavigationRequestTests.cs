using System.Text.Json;
using Runesmith.Lsp.Protocol;
using Range = Runesmith.Lsp.Protocol.Range;

namespace Runesmith.Lsp.Tests;

public sealed class NavigationRequestTests
{
    private const string Uri = "file:///work/app.ts";

    private const string AllFeatures = """
        {
          "referencesProvider": true,
          "implementationProvider": {},
          "documentHighlightProvider": true,
          "documentSymbolProvider": { "label": "app" },
          "workspaceSymbolProvider": true,
          "foldingRangeProvider": true,
          "selectionRangeProvider": true,
          "callHierarchyProvider": true,
          "typeHierarchyProvider": {},
          "semanticTokensProvider": {
            "legend": { "tokenTypes": ["class", "function", "parameter"], "tokenModifiers": ["declaration", "readonly"] },
            "range": true,
            "full": { "delta": true }
          }
        }
        """;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DeclaresTheNavigationAndSyntaxFeaturesItSupports()
    {
        await using var server = new FakeServer("{}");
        await server.StartAsync();

        var capabilities = server.Parameters("initialize").GetProperty("capabilities");
        var document = capabilities.GetProperty("textDocument");
        Assert.True(document.GetProperty("documentSymbol").GetProperty("hierarchicalDocumentSymbolSupport").GetBoolean());
        Assert.True(document.GetProperty("foldingRange").GetProperty("lineFoldingOnly").GetBoolean());
        Assert.True(document.GetProperty("implementation").GetProperty("linkSupport").GetBoolean());
        foreach (var feature in (string[])["references", "documentHighlight", "selectionRange", "callHierarchy", "typeHierarchy"])
            Assert.True(document.TryGetProperty(feature, out _), feature);
        var semantic = document.GetProperty("semanticTokens");
        Assert.Equal(["relative"], semantic.GetProperty("formats").EnumerateArray().Select(f => f.GetString()));
        Assert.Contains("typeParameter", semantic.GetProperty("tokenTypes").EnumerateArray().Select(t => t.GetString()));
        Assert.Contains("deprecated", semantic.GetProperty("tokenModifiers").EnumerateArray().Select(t => t.GetString()));
        Assert.True(semantic.GetProperty("requests").GetProperty("range").GetBoolean());
        var workspace = capabilities.GetProperty("workspace");
        Assert.Equal(26, workspace.GetProperty("symbol").GetProperty("symbolKind").GetProperty("valueSet").GetArrayLength());
        Assert.True(workspace.GetProperty("semanticTokens").GetProperty("refreshSupport").GetBoolean());
    }

    [Fact]
    public async Task ReadsProvidersSentAsFlagsOrOptionsAndTheSemanticTokensLegend()
    {
        await using var server = new FakeServer(AllFeatures);
        await server.StartAsync();

        var capabilities = server.Client.Capabilities;
        Assert.True(capabilities.ReferencesProvider);
        Assert.True(capabilities.ImplementationProvider);
        Assert.True(capabilities.DocumentSymbolProvider);
        Assert.True(capabilities.TypeHierarchyProvider);
        var semantic = Assert.IsType<SemanticTokensOptions>(capabilities.SemanticTokensProvider);
        Assert.Equal(["class", "function", "parameter"], semantic.Legend.TokenTypes);
        Assert.True(semantic.Range);
        Assert.True(semantic.Full);
    }

    [Fact]
    public async Task AsksWithoutAProviderNever()
    {
        await using var server = new FakeServer("{}");
        await server.StartAsync();

        Assert.Empty(await server.Client.ImplementationAsync(Uri, new Position(0, 0), Token));
        Assert.Null(await server.Client.DocumentHighlightAsync(Uri, new Position(0, 0), Token));
        Assert.Null(await server.Client.DocumentSymbolAsync(Uri, Token));
        Assert.Empty(await server.Client.WorkspaceSymbolAsync("app", Token));
        Assert.Null(await server.Client.FoldingRangeAsync(Uri, Token));
        Assert.Null(await server.Client.SemanticTokensAsync(Uri, null, Token));
        Assert.Null(await server.Client.SelectionRangeAsync(Uri, new Position(0, 0), Token));
        Assert.Empty(await server.Client.PrepareCallHierarchyAsync(Uri, new Position(0, 0), Token));
        Assert.Empty(await server.Client.PrepareTypeHierarchyAsync(Uri, new Position(0, 0), Token));
        Assert.DoesNotContain(server.Received, r => r.Method != "initialize");
    }

    [Fact]
    public async Task ReadsImplementationsAsLocationsOrLinksAndHighlightsWithTheirKinds()
    {
        await using var server = new FakeServer(AllFeatures);
        server.Answer("textDocument/implementation", _ => $$"""
            [
              { "targetUri": "file:///work/a.ts", "targetRange": {{Json(Range(0, 0, 9, 1))}}, "targetSelectionRange": {{Json(Range(1, 6, 1, 9))}} },
              { "uri": "file:///work/b.ts", "range": {{Json(Range(2, 0, 2, 3))}} }
            ]
            """);
        server.Answer("textDocument/documentHighlight", _ => $$"""[ { "range": {{Json(Range(0, 4, 0, 7))}}, "kind": 3 }, { "range": {{Json(Range(2, 0, 2, 3))}} } ]""");
        await server.StartAsync();

        var implementations = await server.Client.ImplementationAsync(Uri, new Position(0, 5), Token);
        var highlights = await server.Client.DocumentHighlightAsync(Uri, new Position(0, 5), Token);

        Assert.Equal([new Location("file:///work/a.ts", Range(1, 6, 1, 9)), new Location("file:///work/b.ts", Range(2, 0, 2, 3))], implementations);
        Assert.Equal([3, null], highlights!.Select(h => h.Kind));
        Assert.Equal(5, server.Parameters("textDocument/implementation").GetProperty("position").GetProperty("character").GetInt32());
    }

    [Fact]
    public async Task ReadsHierarchicalSymbolsAndNestsAFlatListByRange()
    {
        await using var server = new FakeServer(AllFeatures);
        var flat = false;
        server.Answer("textDocument/documentSymbol", _ => flat
            ? $$"""
              [
                { "name": "App", "kind": 5, "location": { "uri": "{{Uri}}", "range": {{Json(Range(0, 0, 10, 1))}} } },
                { "name": "run", "kind": 6, "containerName": "App", "location": { "uri": "{{Uri}}", "range": {{Json(Range(2, 2, 4, 3))}} } },
                { "name": "main", "kind": 12, "tags": [1], "location": { "uri": "{{Uri}}", "range": {{Json(Range(12, 0, 14, 1))}} } }
              ]
              """
            : $$"""
              [ { "name": "App", "detail": "class", "kind": 5, "range": {{Json(Range(0, 0, 10, 1))}}, "selectionRange": {{Json(Range(0, 6, 0, 9))}},
                  "children": [ { "name": "run", "kind": 6, "range": {{Json(Range(2, 2, 4, 3))}}, "selectionRange": {{Json(Range(2, 2, 2, 5))}} } ] } ]
              """);
        await server.StartAsync();

        var tree = await server.Client.DocumentSymbolAsync(Uri, Token);
        flat = true;
        var nested = (await server.Client.DocumentSymbolAsync(Uri, Token))!;

        var app = Assert.Single(tree!);
        Assert.Equal(("App", "class", "run"), (app.Name, app.Detail, Assert.Single(app.Children!).Name));
        Assert.Equal(["App", "main"], nested.Select(s => s.Name));
        Assert.Equal("run", Assert.Single(nested[0].Children!).Name);
        Assert.Equal([1], nested[1].Tags);
    }

    [Fact]
    public async Task ReadsWorkspaceSymbolsWhoseLocationsMayHaveNoRange()
    {
        await using var server = new FakeServer(AllFeatures);
        server.Answer("workspace/symbol", _ => $$"""
            [ { "name": "App", "kind": 5, "location": { "uri": "file:///work/app.ts", "range": {{Json(Range(0, 6, 0, 9))}} } },
              { "name": "Lazy", "kind": 5, "containerName": "lib", "location": { "uri": "file:///work/lazy.ts" } } ]
            """);
        await server.StartAsync();

        var symbols = await server.Client.WorkspaceSymbolAsync("a", Token);

        Assert.Equal("a", server.Parameters("workspace/symbol").GetProperty("query").GetString());
        Assert.Equal(["App", "Lazy"], symbols.Select(s => s.Name));
        Assert.Null(symbols[1].Location.Range);
        Assert.Equal("lib", symbols[1].ContainerName);
    }

    [Fact]
    public async Task ReadsFoldingRangesSelectionRangesAndSemanticTokensForRangesOrWholeDocuments()
    {
        await using var server = new FakeServer(AllFeatures);
        server.Answer("textDocument/foldingRange", _ => """[ { "startLine": 1, "endLine": 4, "kind": "imports", "collapsedText": "imports" }, { "startLine": 6, "endLine": 9 } ]""");
        server.Answer("textDocument/selectionRange", _ => $$"""
            [ { "range": {{Json(Range(1, 4, 1, 7))}}, "parent": { "range": {{Json(Range(1, 0, 1, 20))}}, "parent": { "range": {{Json(Range(0, 0, 3, 0))}} } } } ]
            """);
        server.Answer("textDocument/semanticTokens/full", _ => """{ "resultId": "1", "data": [0, 6, 3, 0, 1] }""");
        server.Answer("textDocument/semanticTokens/range", _ => """{ "data": [2, 2, 3, 1, 0] }""");
        await server.StartAsync();

        var folding = await server.Client.FoldingRangeAsync(Uri, Token);
        var selection = await server.Client.SelectionRangeAsync(Uri, new Position(1, 5), Token);
        var full = await server.Client.SemanticTokensAsync(Uri, null, Token);
        var range = await server.Client.SemanticTokensAsync(Uri, Range(2, 0, 3, 0), Token);

        Assert.Equal([("imports", "imports"), (null, null)], folding!.Select(f => (f.Kind, f.CollapsedText)));
        Assert.Equal([Range(1, 4, 1, 7), Range(1, 0, 1, 20), Range(0, 0, 3, 0)], selection);
        Assert.Equal(5, server.Parameters("textDocument/selectionRange").GetProperty("positions")[0].GetProperty("character").GetInt32());
        Assert.Equal([0, 6, 3, 0, 1], full!.Data);
        Assert.Equal([2, 2, 3, 1, 0], range!.Data);
        Assert.Equal(2, server.Parameters("textDocument/semanticTokens/range").GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
    }

    [Fact]
    public async Task AsksForTheWholeDocumentsTokensWhenTheServerDoesNotAnswerForRanges()
    {
        await using var server = new FakeServer("""{ "semanticTokensProvider": { "legend": { "tokenTypes": ["class"], "tokenModifiers": [] }, "full": true } }""");
        server.Answer("textDocument/semanticTokens/full", _ => """{ "data": [] }""");
        await server.StartAsync();

        Assert.Empty((await server.Client.SemanticTokensAsync(Uri, Range(0, 0, 1, 0), Token))!.Data);
        Assert.DoesNotContain(server.Received, r => r.Method == "textDocument/semanticTokens/range");
    }

    [Fact]
    public async Task WalksCallAndTypeHierarchiesSendingTheItemsBackWithTheirData()
    {
        await using var server = new FakeServer(AllFeatures);
        var item = $$"""{ "name": "run", "kind": 6, "uri": "{{Uri}}", "range": {{Json(Range(2, 0, 6, 1))}}, "selectionRange": {{Json(Range(2, 0, 2, 3))}}, "data": { "id": 42 } }""";
        var caller = $$"""{ "name": "main", "kind": 12, "uri": "file:///work/main.ts", "range": {{Json(Range(0, 0, 3, 1))}}, "selectionRange": {{Json(Range(0, 9, 0, 13))}} }""";
        server.Answer("textDocument/prepareCallHierarchy", _ => $"[{item}]");
        server.Answer("callHierarchy/incomingCalls", _ => $$"""[ { "from": {{caller}}, "fromRanges": [ {{Json(Range(1, 4, 1, 7))}} ] } ]""");
        server.Answer("callHierarchy/outgoingCalls", _ => $$"""[ { "to": {{caller}}, "fromRanges": [ {{Json(Range(3, 4, 3, 8))}}, {{Json(Range(4, 4, 4, 8))}} ] } ]""");
        server.Answer("textDocument/prepareTypeHierarchy", _ => $"[{item}]");
        server.Answer("typeHierarchy/supertypes", _ => $"[{caller}]");
        server.Answer("typeHierarchy/subtypes", _ => "[]");
        await server.StartAsync();

        var prepared = Assert.Single(await server.Client.PrepareCallHierarchyAsync(Uri, new Position(2, 1), Token));
        var incoming = await server.Client.IncomingCallsAsync(prepared, Token);
        var outgoing = await server.Client.OutgoingCallsAsync(prepared, Token);
        var type = Assert.Single(await server.Client.PrepareTypeHierarchyAsync(Uri, new Position(2, 1), Token));
        var supertypes = await server.Client.SupertypesAsync(type, Token);
        var subtypes = await server.Client.SubtypesAsync(type, Token);

        Assert.Equal(42, server.Parameters("callHierarchy/incomingCalls").GetProperty("item").GetProperty("data").GetProperty("id").GetInt32());
        Assert.Equal(("main", 1), (Assert.Single(incoming).From.Name, incoming[0].FromRanges.Count));
        Assert.Equal(2, Assert.Single(outgoing).FromRanges.Count);
        Assert.Equal("main", Assert.Single(supertypes).Name);
        Assert.Empty(subtypes);
    }

    [Fact]
    public async Task RaisesARefreshWhenTheServerAsksForSemanticTokensOrFoldingAgain()
    {
        await using var server = new FakeServer(AllFeatures);
        await server.StartAsync();
        var refreshed = 0;
        server.Client.SyntaxRefreshRequested += (_, _) => Interlocked.Increment(ref refreshed);

        await server.Connection.SendRequestAsync<JsonElement>("workspace/semanticTokens/refresh", null, Token);
        await server.Connection.SendRequestAsync<JsonElement>("workspace/foldingRange/refresh", null, Token);

        Assert.Equal(2, refreshed);
    }

    private static Range Range(int startLine, int startCharacter, int endLine, int endCharacter) =>
        new(new Position(startLine, startCharacter), new Position(endLine, endCharacter));

    private static string Json(Range range) => JsonSerializer.Serialize(range, LspJsonContext.Default.Range);
}
