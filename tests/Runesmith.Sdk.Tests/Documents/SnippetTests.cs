using System.ComponentModel;
using System.Text;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Documents.Snippets;
using Runesmith.Text;

namespace Runesmith.Sdk.Tests.Documents;

public sealed class SnippetTests
{
    private static SnippetExpansion Expand(string snippet, Func<string, string?>? resolve = null, string indent = "", string unit = "\t") =>
        SnippetExpansion.Create(SnippetParser.Parse(snippet), resolve ?? (_ => null), indent, unit);

    private static string[] StopTexts(SnippetExpansion expansion) =>
        [.. expansion.Stops.Select(s => $"{s.Index}:{expansion.Text.Substring(s.Span.Start, s.Span.Length)}")];

    [Fact]
    public void TabStopsAndPlaceholdersBecomeTextWithStops()
    {
        var expansion = Expand("for (${1:i} = 0; $1 < ${2:count}; $1++) {\n\t$0\n}");

        Assert.Equal("for (i = 0; i < count; i++) {\n\t\n}", expansion.Text);
        Assert.Equal(["1:i", "1:i", "2:count", "1:i", "0:"], StopTexts(expansion));
        Assert.Equal(expansion.Text.IndexOf("\t\n", StringComparison.Ordinal) + 1, expansion.FinalOffset);
        Assert.True(expansion.HasTabStops);
    }

    [Fact]
    public void AMirrorBeforeItsPlaceholderTakesThePlaceholdersText()
    {
        var expansion = Expand("$1 = ${1:value}");

        Assert.Equal("value = value", expansion.Text);
        Assert.Equal(["1:value", "1:value"], StopTexts(expansion));
    }

    [Fact]
    public void NestedPlaceholdersAreStopsInsideTheirParent()
    {
        var expansion = Expand("${1:new ${2:Thing}()}");

        Assert.Equal("new Thing()", expansion.Text);
        Assert.Equal(["1:new Thing()", "2:Thing"], StopTexts(expansion));
    }

    [Fact]
    public void ChoicesInsertTheFirstOptionAndKeepTheRest()
    {
        var expansion = Expand(@"${1|public,private,a\,b|} void");

        Assert.Equal("public void", expansion.Text);
        Assert.Equal(["public", "private", "a,b"], expansion.Stops[0].Choices);
    }

    [Fact]
    public void EscapesAndBrokenSyntaxAreText()
    {
        Assert.Equal("$1 costs $ and } and \\", Expand(@"\$1 costs $ and \} and \\").Text);
        Assert.Equal("${1:open", Expand("${1:open").Text);
        Assert.Equal("${ x", Expand("${ x").Text);
        Assert.False(Expand("plain").HasTabStops);
        Assert.Equal(5, Expand("plain").FinalOffset);
    }

    [Fact]
    public void VariablesResolveUseTheirDefaultWhenEmptyAndUnknownOnesBecomePlaceholders()
    {
        var values = new Dictionary<string, string?> { ["TM_FILENAME"] = "Program.cs", ["TM_SELECTED_TEXT"] = "" };
        var expansion = Expand("$TM_FILENAME ${TM_SELECTED_TEXT:none} $UNKNOWN ${2:x}", name => values.GetValueOrDefault(name));

        Assert.Equal("Program.cs none UNKNOWN x", expansion.Text);
        Assert.Equal(["3:UNKNOWN", "2:x"], StopTexts(expansion));
    }

    [Fact]
    public void TransformsChangeVariablesAndMirrors()
    {
        Assert.Equal("PROGRAM", Expand("${TM_FILENAME_BASE/(.*)/${1:/upcase}/}", _ => "program").Text);
        Assert.Equal("my_file", Expand("${TM_FILENAME_BASE/-/_/g}", _ => "my-file").Text);
        Assert.Equal("MyFile", Expand("${TM_FILENAME_BASE/(.*)/${1:/pascalcase}/}", _ => "my-file").Text);
        Assert.Equal("yes", Expand("${TM_FILENAME_BASE/(x)?.*/${1:?yes:no}/}", _ => "xa").Text);
        Assert.Equal("no", Expand("${TM_FILENAME_BASE/(x)?.*/${1:?yes:no}/}", _ => "a").Text);

        var expansion = Expand("${1:name} ${1/(.*)/${1:/capitalize}/}");
        Assert.Equal("name Name", expansion.Text);
        Assert.NotNull(expansion.Stops[1].Transform);
    }

    [Fact]
    public void LinesAreIndentedLikeTheFirstAndTabsBecomeTheIndentUnit()
    {
        var expansion = Expand("if ($1)\n{\n\t$0\n}", indent: "    ", unit: "    ");

        Assert.Equal("if ()\n    {\n        \n    }", expansion.Text);
    }

    [Fact]
    public void TheBuilderEscapesTextAndNumbersStopsInOrder()
    {
        var snippet = new SnippetString()
            .AppendText("price: $")
            .AppendPlaceholder("amount")
            .AppendText(" ")
            .AppendChoice(["EUR", "USD"])
            .AppendText(" ")
            .AppendPlaceholder(inner => inner.AppendText("a ").AppendTabStop())
            .AppendVariable("TM_FILENAME", "file")
            .AppendFinalTabStop();

        Assert.Equal(@"price: \$${1:amount} ${2|EUR,USD|} ${3:a $4}${TM_FILENAME:file}$0", snippet.Value);
        Assert.Equal(5, snippet.NextTabStop);
        Assert.Equal("price: $amount EUR a file", Expand(snippet.Value).Text);
    }

    [Fact]
    public void VariablesReadTheDocumentAndTheSelection()
    {
        var snapshot = TextSnapshot.Create("first\n  second word");
        var context = new SnippetContext("/src/app/Main.cs", "Main.cs", snapshot, new TextSpan(15, 4)) { LineComment = "//", Now = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero) };

        Assert.Equal("Main.cs", SnippetVariables.Resolve("TM_FILENAME", context));
        Assert.Equal("Main", SnippetVariables.Resolve("TM_FILENAME_BASE", context));
        Assert.Equal("word", SnippetVariables.Resolve("SELECTED_TEXT", context));
        Assert.Equal("  second word", SnippetVariables.Resolve("TM_CURRENT_LINE", context));
        Assert.Equal("2", SnippetVariables.Resolve("TM_LINE_NUMBER", context));
        Assert.Equal("//", SnippetVariables.Resolve("LINE_COMMENT", context));
        Assert.Equal("2026-03-04", $"{SnippetVariables.Resolve("CURRENT_YEAR", context)}-{SnippetVariables.Resolve("CURRENT_MONTH", context)}-{SnippetVariables.Resolve("CURRENT_DATE", context)}");
        Assert.Equal("", SnippetVariables.Resolve("CLIPBOARD", context));
        Assert.Null(SnippetVariables.Resolve("NOPE", context));
        Assert.Equal("  ", SnippetVariables.IndentOf(snapshot, 15));
    }

    [Fact]
    public void AnEditorWithoutTabStopsInsertsTheTextAndPutsTheCaretAtTheEnd()
    {
        var document = new PlainDocument("x = ;");
        IEditorView editor = new PlainEditor(document) { CaretOffset = 4 };

        editor.InsertSnippet(new SnippetString().AppendPlaceholder("value").AppendFinalTabStop().AppendText(" + 1"));

        Assert.Equal("x = value + 1;", document.Buffer.Current.GetText());
        Assert.Equal(9, editor.CaretOffset);
        Assert.True(document.History.CanUndo);
    }

    private sealed class PlainDocument(string text) : IDocument
    {
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }

        public string? FilePath => null;

        public string Name => "Untitled-1";

        public TextBuffer Buffer { get; } = new(text);

        public UndoHistory History { get; } = new();

        public string LanguageId { get; set; } = "plaintext";

        public LineEnding LineEnding { get; set; }

        public Encoding Encoding { get; set; } = Encoding.UTF8;

        public bool IsModified => false;

        public bool IsReadOnly => false;
    }

    private sealed class PlainEditor(IDocument document) : IEditorView
    {
        public event EventHandler? CaretMoved { add { } remove { } }

        public IDocument Document => document;

        public int CaretOffset { get; set; }

        public TextSpan Selection => new(CaretOffset, 0);

        public EditorCaretStyle CaretStyle { get; set; }

        public void Select(TextSpan span) => CaretOffset = span.End;

        public void ScrollTo(int offset)
        {
        }

        public void Focus()
        {
        }
    }
}
