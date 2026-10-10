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
                new Protocol.TextDocumentEdit(new Protocol.OptionalVersionedTextDocumentIdentifier(LspUri.FromPath(openPath), 2), [new Protocol.TextEdit(Range(1, 0, 1, 4), "BETA")]),
                new Protocol.TextDocumentEdit(new Protocol.OptionalVersionedTextDocumentIdentifier(LspUri.FromPath(closedPath), null),
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
    public void ResourceOperationsBecomeFileOperationsAndTextEditsNameTheirFilesAsTheyEndUp()
    {
        var program = Path.Combine(folder, "Program.cs");
        File.WriteAllText(program, "class Program {}\r\n");
        var main = Path.Combine(folder, "App", "Main.cs");
        var created = Path.Combine(folder, "New.cs");
        var edit = new Protocol.WorkspaceEdit
        {
            DocumentChanges =
            [
                new Protocol.TextDocumentEdit(new Protocol.OptionalVersionedTextDocumentIdentifier(LspUri.FromPath(program), null), [new Protocol.TextEdit(Range(0, 6, 0, 13), "Main")]),
                new Protocol.RenameFile(LspUri.FromPath(program), LspUri.FromPath(main), new Protocol.RenameFileOptions(true, null)),
                new Protocol.TextDocumentEdit(new Protocol.OptionalVersionedTextDocumentIdentifier(LspUri.FromPath(main), null), [new Protocol.TextEdit(Range(0, 0, 0, 0), "// moved\n")]),
                new Protocol.CreateFile(LspUri.FromPath(created)),
                new Protocol.TextDocumentEdit(new Protocol.OptionalVersionedTextDocumentIdentifier(LspUri.FromPath(created), null), [new Protocol.TextEdit(Range(0, 0, 0, 0), "class New {}")]),
                new Protocol.DeleteFile(LspUri.FromPath(Path.Combine(folder, "obj")), new Protocol.DeleteFileOptions(true, true)),
            ],
        };

        var converted = LspConvert.ToWorkspaceEdit(edit, new TestDocuments());

        Assert.Equal(
            [
                new RenameFileOperation(program, main) { Overwrite = true },
                new CreateFileOperation(created),
                new DeleteFileOperation(Path.Combine(folder, "obj")) { Recursive = true, IgnoreIfMissing = true },
            ],
            converted.FileOperations);
        Assert.Equal([main, main, created], converted.Documents.Select(d => d.FilePath));
        Assert.Equal(new TextChange(new TextSpan(6, 7), "Main"), Assert.Single(converted.Documents[0].Changes));
        Assert.Equal(new TextChange(new TextSpan(0, 0), "class New {}"), Assert.Single(converted.Documents[2].Changes));
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
