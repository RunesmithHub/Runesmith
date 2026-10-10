using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Runesmith.Editor.Completion;
using Runesmith.Editor.Snippets;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Documents.Snippets;
using Runesmith.Sdk.Languages;
using Runesmith.Text;

namespace Runesmith.Editor.Tests;

public sealed class SnippetTests
{
    private static readonly string FilePath = Path.Combine(Path.GetTempPath(), "runesmith-tests", "Snippets.cs");

    [Fact]
    public Task TabAndShiftTabMoveBetweenStopsAndTheLastTabEndsAtTheFinalPosition() => Run((editor, _) =>
    {
        editor.InsertSnippet("for (${1:i} = 0; ${2:n}; $1++) {$0}");

        Assert.Equal("for (i = 0; n; i++) {}", Text(editor));
        Assert.Equal(new TextSpan(5, 1), editor.Selection);
        Press(editor, Key.Tab);
        Assert.Equal(new TextSpan(12, 1), editor.Selection);
        Press(editor, Key.Tab, KeyModifiers.Shift);
        Assert.Equal(new TextSpan(5, 1), editor.Selection);
        Press(editor, Key.Tab);
        Press(editor, Key.Tab);
        Assert.Equal(21, editor.CaretOffset);
        Assert.True(editor.Selection.IsEmpty);
        Assert.False(editor.Area.IsInSnippet);
    });

    [Fact]
    public Task TypingInALinkedStopChangesItsOtherPlacesInOneUndoStep() => Run((editor, _) =>
    {
        editor.InsertSnippet("for (${1:i} = 0; $1 < ${2:n}; $1++)");

        Type(editor, "index");
        Assert.Equal("for (index = 0; index < n; index++)", Text(editor));
        Assert.Equal(10, editor.CaretOffset);
        Press(editor, Key.Back);
        Assert.Equal("for (inde = 0; inde < n; inde++)", Text(editor));

        Press(editor, Key.Tab);
        Assert.Equal("n", editor.Area.Snapshot.GetText(editor.Selection));
        Type(editor, "count");
        Assert.Equal("for (inde = 0; inde < count; inde++)", Text(editor));

        editor.Undo();
        Assert.Equal("for (inde = 0; inde < n; inde++)", Text(editor));
        editor.Undo();
        Assert.Equal("for (index = 0; index < n; index++)", Text(editor));
        editor.Undo();
        Assert.Equal("for (i = 0; i < n; i++)", Text(editor));
        editor.Undo();
        Assert.Equal("", Text(editor));
    });

    [Fact]
    public Task InsertingASnippetIsOneUndoStepAndKeepsTheLinesIndentation() => Run((editor, _) =>
    {
        editor.Area.InsertText("    ");
        editor.InsertSnippet("if ($1)\n{\n\t$0\n}");

        Assert.Equal("    if ()\n    {\n        \n    }", Text(editor));
        Assert.Equal(8, editor.CaretOffset);
        editor.Undo();
        Assert.Equal("    ", Text(editor));
    });

    [Fact]
    public Task TransformedMirrorsFollowTheirTabStop() => Run((editor, _) =>
    {
        editor.InsertSnippet("${1:name}: ${1/(.*)/${1:/upcase}/}");

        Type(editor, "id");
        Assert.Equal("id: ID", Text(editor));
    });

    [Fact]
    public Task EscOrMovingAwayLeavesTheSnippet() => Run((editor, _) =>
    {
        editor.Area.InsertText("start ");
        editor.InsertSnippet("(${1:a}, ${2:b})");
        Assert.True(editor.Area.IsInSnippet);
        Press(editor, Key.Escape);
        Assert.False(editor.Area.IsInSnippet);

        editor.InsertSnippet(" [${1:c}]");
        Assert.True(editor.Area.IsInSnippet);
        editor.CaretOffset = 0;
        Assert.False(editor.Area.IsInSnippet);
        Press(editor, Key.Tab);
        Assert.StartsWith("    ", Text(editor), StringComparison.Ordinal);
    });

    [Fact]
    public Task SelectedTextAndTheFileNameAreVariables() => Run((editor, _) =>
    {
        editor.Area.InsertText("value");
        editor.Select(new TextSpan(0, 5));

        editor.InsertSnippet(new SnippetString().AppendText("/* ").AppendVariable("TM_FILENAME").AppendText(" */ (").AppendVariable("SELECTED_TEXT").AppendText(")"));

        Assert.Equal("/* Snippets.cs */ (value)", Text(editor));
    });

    [Fact]
    public Task AChoiceIsOfferedInTheListAndPickingOneReplacesTheStop() => Run(async (editor, _) =>
    {
        var popup = editor.GetVisualDescendants().OfType<CompletionPopup>().Single();
        editor.InsertSnippet("${1|public,private|} ${2:int} x;");

        Assert.True(popup.IsOpen);
        Assert.Equal(2, popup.Count);
        Assert.Equal("public", popup.Selected?.Item.Label);
        Press(editor, Key.Down);
        Press(editor, Key.Enter);
        Assert.Equal("private int x;", Text(editor));
        Assert.False(popup.IsOpen);

        Press(editor, Key.Tab);
        Assert.Equal("int", editor.Area.Snapshot.GetText(editor.Selection));
        await Task.CompletedTask;
    });

    [Fact]
    public Task ACompletionItemThatIsASnippetUsesTheSameTabStops() => Run(async (editor, fakes) =>
    {
        editor.Area.InsertText("fo");
        fakes.Language.Completions = new CompletionList([new CompletionItem("for", CompletionItemKind.Snippet) { InsertText = "for (${1:i}; $1 < ${2:n}; $1++)", IsSnippet = true }]);
        var popup = editor.GetVisualDescendants().OfType<CompletionPopup>().Single();

        editor.TriggerCompletion();
        await WaitAsync(() => popup.IsOpen);
        Press(editor, Key.Enter);

        Assert.Equal("for (i; i < n; i++)", Text(editor));
        Assert.Equal(new TextSpan(5, 1), editor.Selection);
        Type(editor, "k");
        Assert.Equal("for (k; k < n; k++)", Text(editor));
        editor.Undo();
        editor.Undo();
        Assert.Equal("fo", Text(editor));
    });

    [Fact]
    public void ARangeGrowsWithTypingAtItsEdgeOnlyWhileItIsTheCurrentStop()
    {
        var expansion = SnippetExpansion.Create(SnippetParser.Parse("$1$2"), _ => null);
        var session = new SnippetSession(expansion, 10);
        session.Move(1);

        session.Map([new TextChange(new TextSpan(10, 0), "ab")]);

        Assert.Equal(new TextSpan(10, 2), session.Groups[0].Ranges[0].Span);
        Assert.Equal(new TextSpan(12, 0), session.Groups[1].Ranges[0].Span);
        Assert.Equal(new TextSpan(10, 2), session.Whole);
    }

    private static string Text(TextEditor editor) => editor.Area.Snapshot.GetText();

    private static void Press(TextEditor editor, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        editor.Area.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = editor.Area });

    private static void Type(TextEditor editor, string text)
    {
        foreach (var character in text)
            editor.Area.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = character.ToString(), Source = editor.Area });
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition was not met in time.");
            await Task.Delay(10);
        }
    }

    private static Task<bool> Run(Action<TextEditor, FakeEditorServices> test) => Run((editor, fakes) =>
    {
        test(editor, fakes);
        return Task.CompletedTask;
    });

    private static Task<bool> Run(Func<TextEditor, FakeEditorServices, Task> test) =>
        HeadlessSession.Value.Dispatch(async () =>
        {
            var fakes = new FakeEditorServices();
            using var editor = new TextEditor(new FileTestDocument(FilePath, ""), fakes.Create());
            var window = new Window { Width = 600, Height = 400, Content = editor };
            window.Show();
            editor.Area.Focus();
            await test(editor, fakes);
            window.Close();
            return true;
        }, TestContext.Current.CancellationToken);
}
