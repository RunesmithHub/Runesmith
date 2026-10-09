using Runesmith.Languages.Servers;
using Runesmith.Languages.Tests.Analyzers;
using Runesmith.Lsp;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Text;
using Protocol = Runesmith.Lsp.Protocol;

namespace Runesmith.Languages.Tests.Servers;

public sealed class LspMappingTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("runesmith-lsp-").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public void InlayHintsAndCodeLensesBecomeDecorations()
    {
        var snapshot = TextSnapshot.Create("var count = Add(1, 2);\nclass C { }");
        Protocol.InlayHint[] hints =
        [
            new() { Position = new Protocol.Position(0, 9), Label = [new Protocol.InlayHintLabelPart(": int", null)], Kind = Protocol.InlayHintKind.Type },
            new()
            {
                Position = new Protocol.Position(0, 16),
                Label = [new Protocol.InlayHintLabelPart("a:", null)],
                Kind = Protocol.InlayHintKind.Parameter,
                PaddingRight = true,
                Tooltip = new Protocol.MarkupContent(Protocol.MarkupKind.Markdown, "The **first** number"),
            },
            new() { Position = new Protocol.Position(0, 1), Label = [new Protocol.InlayHintLabelPart(" ", null)] },
        ];
        Protocol.CodeLens[] lenses =
        [
            new(Range(1, 6, 1, 7), new Protocol.Command("3 references", "references.show", null), null),
            new(Range(1, 0, 1, 1), null, null),
        ];

        var decorations = LspDecorationProvider.ToDecorations(snapshot, null, hints, lenses);

        var type = Assert.IsType<InlayHint>(decorations[0]);
        Assert.Equal((9, ": int", InlayHintSide.After, InlayHintKind.Type), (type.Offset, type.Text, type.Side, type.Kind));
        var parameter = Assert.IsType<InlayHint>(decorations[1]);
        Assert.Equal((16, "a: ", InlayHintSide.Before), (parameter.Offset, parameter.Text, parameter.Side));
        Assert.Equal("The **first** number", parameter.ToolTip);
        var lens = Assert.IsType<CodeLens>(Assert.Single(decorations.Skip(2)));
        Assert.Equal((new TextSpan(29, 1), "3 references"), (lens.Span, lens.Text));
        Assert.Null(lens.CommandId);
    }

    [Theory]
    [InlineData(null, CodeActionKind.QuickFix)]
    [InlineData("quickfix", CodeActionKind.QuickFix)]
    [InlineData("refactor.extract.function", CodeActionKind.Refactor)]
    [InlineData("source.organizeImports", CodeActionKind.Source)]
    public void CodeActionKindsMapByTheirFirstPart(string? kind, CodeActionKind expected) => Assert.Equal(expected, LspCodeActionProvider.ToKind(kind));

    [Fact]
    public void WorkspaceEditsUseOpenDocumentsAndReadOtherFilesWithoutCarriageReturns()
    {
        var openPath = Path.Combine(folder, "Open.cs");
        var closedPath = Path.Combine(folder, "Closed.cs");
        File.WriteAllText(closedPath, "one\r\ntwo\r\n");
        var documents = new TestDocuments();
        var open = new TestDocument(openPath, "Open.cs", "alpha\nbeta");
        documents.Add(open);
        var edit = new Protocol.WorkspaceEdit
        {
            DocumentChanges =
            [
                new(new Protocol.OptionalVersionedTextDocumentIdentifier(LspUri.FromPath(openPath), 2), [new Protocol.TextEdit(Range(1, 0, 1, 4), "BETA")]),
                new(new Protocol.OptionalVersionedTextDocumentIdentifier(LspUri.FromPath(closedPath), null),
                [
                    new Protocol.TextEdit(Range(1, 0, 1, 3), "TWO\r\n2"),
                    new Protocol.TextEdit(Range(0, 0, 0, 0), "zero\n"),
                ]),
            ],
        };

        var converted = LspConvert.ToWorkspaceEdit(edit, documents);

        Assert.Same(open.Buffer.Current, converted.Documents[0].Snapshot);
        Assert.Equal(new TextChange(new TextSpan(6, 4), "BETA"), Assert.Single(converted.Documents[0].Changes));
        Assert.Null(converted.Documents[1].Snapshot);
        Assert.Equal([new TextChange(new TextSpan(0, 0), "zero\n"), new TextChange(new TextSpan(4, 3), "TWO\n2")], converted.Documents[1].Changes);
    }

    [Fact]
    public void EditsOfFilesThatAreNotLocalAreRefused()
    {
        var edit = new Protocol.WorkspaceEdit { Changes = new Dictionary<string, IReadOnlyList<Protocol.TextEdit>> { ["untitled:Untitled-1"] = [] } };

        Assert.Throws<LanguageFeatureException>(() => LspConvert.ToWorkspaceEdit(edit, new TestDocuments()));
    }

    private static Protocol.Range Range(int startLine, int startCharacter, int endLine, int endCharacter) =>
        new(new Protocol.Position(startLine, startCharacter), new Protocol.Position(endLine, endCharacter));
}
