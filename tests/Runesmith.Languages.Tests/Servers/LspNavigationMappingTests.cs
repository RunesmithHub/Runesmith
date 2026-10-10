using Runesmith.Languages.Servers;
using Runesmith.Sdk.Languages;
using Runesmith.Text;
using Protocol = Runesmith.Lsp.Protocol;

namespace Runesmith.Languages.Tests.Servers;

public sealed class LspNavigationMappingTests
{
    private static readonly TextSnapshot Text = TextSnapshot.Create("class App {\n  run(x) { }\n}\nconst y = 1;");

    [Fact]
    public void DecodesSemanticTokensWithTheServersLegend()
    {
        var legend = new Protocol.SemanticTokensLegend(["class", "method", "parameter", "custom"], ["declaration", "unknownModifier", "readonly", "deprecated"]);
        int[] data =
        [
            0, 6, 3, 0, 1,
            1, 2, 3, 1, 0b1001,
            0, 4, 1, 2, 0,
            0, 4, 50, 3, 0,
            0, 2, 1, 9, 0,
            2, 6, 1, 2, 0b0100,
            5, 0, 1, 0, 0,
        ];

        var tokens = LspConvert.ToSemanticTokens(Text, legend, data);

        Assert.Equal(
        [
            (new TextSpan(6, 3), SemanticTokenTypes.Class, SemanticTokenModifiers.Declaration),
            (new TextSpan(14, 3), SemanticTokenTypes.Method, SemanticTokenModifiers.Declaration | SemanticTokenModifiers.Deprecated),
            (new TextSpan(18, 1), SemanticTokenTypes.Parameter, SemanticTokenModifiers.None),
            (new TextSpan(22, 2), "custom", SemanticTokenModifiers.None),
            (new TextSpan(33, 1), SemanticTokenTypes.Parameter, SemanticTokenModifiers.Readonly),
        ], tokens.Select(t => (t.Span, t.Type, t.Modifiers)));
        Assert.Same(SemanticTokenTypes.Class, tokens[0].Type);
    }

    [Fact]
    public void ConvertsSymbolsKeepingTheSelectionInsideTheirRangeAndTheirDeprecation()
    {
        var symbol = new Protocol.DocumentSymbol("App", 5, Range(0, 0, 2, 1), Range(0, 6, 0, 9))
        {
            Detail = " ",
            Children = [new Protocol.DocumentSymbol("run", 6, Range(1, 2, 1, 12), Range(3, 0, 3, 5)) { Tags = [1] }],
        };

        var converted = LspConvert.ToDocumentSymbol(Text, symbol);

        Assert.Equal((SymbolKind.Class, new TextSpan(0, 26), new TextSpan(6, 3), (string?)null), (converted.Kind, converted.Range, converted.SelectionRange, converted.Detail));
        var child = Assert.Single(converted.Children);
        Assert.Equal((SymbolKind.Method, new TextSpan(14, 0), true), (child.Kind, child.SelectionRange, child.IsDeprecated));
        Assert.Equal(SymbolKind.Variable, LspConvert.ToSymbolKind(99));
    }

    [Fact]
    public void ConvertsWorkspaceSymbolsHighlightsAndFoldingRanges()
    {
        var withRange = LspConvert.ToWorkspaceSymbol(new Protocol.SymbolInformation("App", 5, new Protocol.SymbolLocation("file:///work/app.ts", Range(0, 6, 0, 9))) { ContainerName = "" });
        var withoutRange = LspConvert.ToWorkspaceSymbol(new Protocol.SymbolInformation("Lazy", 12, new Protocol.SymbolLocation("file:///work/lazy.ts", null)) { Deprecated = true });
        var remote = LspConvert.ToWorkspaceSymbol(new Protocol.SymbolInformation("Far", 5, new Protocol.SymbolLocation("https://example.com/far.ts", null)));

        Assert.Equal(("/work/app.ts", new TextPosition(0, 6), (string?)null), (withRange!.Location.FilePath, withRange.Location.Start, withRange.ContainerName));
        Assert.Equal((SymbolKind.Function, new TextPosition(0, 0), true), (withoutRange!.Kind, withoutRange.Location.Start, withoutRange.IsDeprecated));
        Assert.Null(remote);
        Assert.Equal([DocumentHighlightKind.Text, DocumentHighlightKind.Read, DocumentHighlightKind.Write], new int?[] { null, 2, 3 }.Select(LspConvert.ToHighlightKind));
        Assert.Equal(new FoldingRange(1, 4) { Kind = FoldingRangeKind.Imports, CollapsedText = "imports" },
            LspConvert.ToFoldingRange(new Protocol.FoldingRange(1, 4) { Kind = "imports", CollapsedText = "imports" }));
        Assert.Equal(FoldingRangeKind.Block, LspConvert.ToFoldingRange(new Protocol.FoldingRange(1, 4) { Kind = "other" })!.Kind);
        Assert.Null(LspConvert.ToFoldingRange(new Protocol.FoldingRange(4, 4)));
    }

    [Fact]
    public void ConvertsHierarchyItemsWithTheNameAsTheirSelection()
    {
        var item = LspConvert.ToHierarchyItem(new Protocol.HierarchyItem("run", 6, "file:///work/app.ts", Range(1, 2, 1, 12), Range(1, 2, 1, 5)) { Detail = "App" });

        Assert.Equal(("run", SymbolKind.Method, "App"), (item!.Name, item.Kind, item.Detail));
        Assert.Equal(new TextPosition(1, 2), item.SelectionLocation!.Start);
        Assert.Equal(new TextPosition(1, 12), item.Location.End);
        Assert.Equal([new TextPosition(4, 0)], LspConvert.ToLocations("/work/app.ts", [Range(4, 0, 4, 3)]).Select(l => l.Start));
    }

    private static Protocol.Range Range(int startLine, int startCharacter, int endLine, int endCharacter) =>
        new(new Protocol.Position(startLine, startCharacter), new Protocol.Position(endLine, endCharacter));
}
