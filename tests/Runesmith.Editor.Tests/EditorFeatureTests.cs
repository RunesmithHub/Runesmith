using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Text;

namespace Runesmith.Editor.Tests;

public sealed class EditorFeatureTests
{
    private static readonly string FilePath = Path.Combine(Path.GetTempPath(), "runesmith-tests", "Program.cs");

    [Fact]
    public Task FormattingIsOneUndoStepAndKeepsTheCaretOnItsText() => Run(async () =>
    {
        var fakes = new FakeEditorServices();
        fakes.Features.Format = snapshot => [new TextChange(new TextSpan(0, 0), "    "), new TextChange(new TextSpan(snapshot.Length, 0), "\n")];
        var document = new FileTestDocument(FilePath, "int x;");
        using var editor = new TextEditor(document, fakes.Create());
        editor.CaretOffset = 4;

        var result = await editor.FormatAsync();

        Assert.Equal(FormatResult.Formatted, result);
        Assert.Equal("    int x;\n", document.Buffer.Current.GetText());
        Assert.Equal(8, editor.CaretOffset);
        editor.Undo();
        Assert.Equal("int x;", document.Buffer.Current.GetText());
    });

    [Fact]
    public Task FormattingSaysWhenNoFormatterServesTheLanguage() => Run(async () =>
    {
        var fakes = new FakeEditorServices();
        using var editor = new TextEditor(new FileTestDocument(FilePath, "text"), fakes.Create());

        Assert.Equal(FormatResult.NoFormatter, await editor.FormatAsync(quiet: true));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        fakes.Features.Format = _ => [];
        Assert.Equal(FormatResult.Cancelled, await editor.FormatAsync(cancellationToken: cancelled.Token));
    });

    [Fact]
    public Task RenameOpensTheBoxOverTheNameAndAppliesTheProvidersEdit() => Run(async () =>
    {
        var fakes = new FakeEditorServices();
        fakes.Features.RenameTarget = new RenameTarget(new TextSpan(4, 5), "count");
        fakes.Features.Rename = name => new WorkspaceEdit([new DocumentEdit(FilePath, [new TextChange(new TextSpan(4, 5), name)])]);
        using var editor = new TextEditor(new FileTestDocument(FilePath, "int count = 0;"), fakes.Create());
        var window = Show(editor);
        editor.CaretOffset = 6;

        await editor.RenameAsync();
        Assert.True(editor.IsRenaming);

        await editor.Renaming.CommitAsync("total");

        Assert.False(editor.IsRenaming);
        var edit = Assert.Single(fakes.Edits.Applied);
        Assert.Equal("total", Assert.Single(Assert.Single(edit.Documents).Changes).NewText);
        window.Close();
    });

    [Fact]
    public Task RenameSaysWhenThereIsNothingToRename() => Run(async () =>
    {
        var fakes = new FakeEditorServices();
        using var editor = new TextEditor(new FileTestDocument(FilePath, "// comment"), fakes.Create());
        var window = Show(editor);

        await editor.RenameAsync();

        Assert.False(editor.IsRenaming);
        Assert.Empty(fakes.Edits.Applied);
        window.Close();
    });

    [Fact]
    public Task TheLightBulbShowsOnTheCaretsLineWhenActionsAreOffered() => Run(async () =>
    {
        var fakes = new FakeEditorServices();
        fakes.Features.Actions.Add(new CodeAction("Add using System.Text", CodeActionKind.QuickFix));
        using var editor = new TextEditor(new FileTestDocument(FilePath, "one\nStringBuilder b;\nthree"), fakes.Create());

        editor.CaretOffset = 6;
        await WaitAsync(() => editor.Area.LightBulbLine == 1);

        var request = fakes.Features.ActionRequests[^1];
        Assert.Equal(CodeActionTrigger.Automatic, request.Trigger);
        Assert.Equal(new TextSpan(4, 16), request.Span);
        editor.CaretOffset = 0;
        Assert.Null(editor.Area.LightBulbLine);
    });

    [Fact]
    public Task ApplyingAnActionAppliesItsEditThenRunsItsCommand() => Run(async () =>
    {
        var fakes = new FakeEditorServices();
        var edit = new WorkspaceEdit([new DocumentEdit(FilePath, [new TextChange(new TextSpan(0, 0), "using System.Text;\n")])]);
        using var editor = new TextEditor(new FileTestDocument(FilePath, "StringBuilder b;"), fakes.Create());

        await editor.CodeActions.ApplyAsync(new CodeAction("Add using", CodeActionKind.QuickFix) { Edit = edit, CommandId = "test.after", CommandArgument = 7 });

        Assert.Same(edit, Assert.Single(fakes.Edits.Applied));
        Assert.Equal(("test.after", (object?)7), Assert.Single(fakes.Commands.Ran));
    });

    [Fact]
    public Task KeyboardHooksSeeKeysAndTextBeforeTheEditor() => Run(() =>
    {
        var fakes = new FakeEditorServices();
        var hook = new ModalHook();
        fakes.Hooks.Add(hook);
        var document = new FileTestDocument(FilePath, "abc");
        using var editor = new TextEditor(document, fakes.Create());
        var window = Show(editor);
        editor.Area.Focus();

        Press(editor, Key.J);
        Type(editor, "j");
        Assert.Equal("abc", document.Buffer.Current.GetText());
        Assert.Equal(1, editor.CaretOffset);
        Assert.Equal(EditorCaretStyle.Block, editor.CaretStyle);

        hook.Insert = true;
        Press(editor, Key.I);
        Type(editor, "x");
        Assert.Equal("axbc", document.Buffer.Current.GetText());
        Assert.Equal(EditorCaretStyle.Line, editor.CaretStyle);
        window.Close();
        return Task.CompletedTask;
    });

    [Fact]
    public Task DecorationsFromProvidersAppearAndFollowEdits() => Run(async () =>
    {
        var fakes = new FakeEditorServices();
        var provider = new ScriptedDecorations
        {
            Decorations = [new CodeLens(new TextSpan(4, 3), "2 references"), new InlayHint(10, "name:"), new GutterMarker(new TextSpan(0, 3), "flag")],
        };
        fakes.Features.DecorationProviders.Add(provider);
        var document = new FileTestDocument(FilePath, "one\ntwo\nthree");
        using var editor = new TextEditor(document, fakes.Create());

        await WaitAsync(() => editor.Area.Decorations.Count == 3);
        Assert.Equal(2, editor.Area.RowOf(1));
        Assert.Equal(3, editor.Area.RowOf(2));

        document.Buffer.Insert(0, "zero\n");
        Assert.Equal(3, editor.Area.RowOf(2));

        provider.Decorations = [];
        provider.Raise();
        await WaitAsync(() => editor.Area.Decorations.Count == 0);
        Assert.Equal(2, editor.Area.RowOf(2));
    });

    [Fact]
    public Task DiffSidesAskForNoDecorations() => Run(async () =>
    {
        var fakes = new FakeEditorServices();
        var provider = new ScriptedDecorations { Decorations = [new GutterMarker(new TextSpan(0, 1), "flag")] };
        fakes.Features.DecorationProviders.Add(provider);
        using var editor = new TextEditor(new FileTestDocument(FilePath, "text"), fakes.Create()) { IsDecorated = false };

        await Task.Delay(100);

        Assert.Equal(0, provider.Requests);
        Assert.Equal(0, editor.Area.Decorations.Count);
    });

    private static Window Show(Control content)
    {
        var window = new Window { Width = 600, Height = 400, Content = content };
        window.Show();
        return window;
    }

    private static void Press(TextEditor editor, Key key) =>
        editor.Area.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = editor.Area });

    private static void Type(TextEditor editor, string text) =>
        editor.Area.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text, Source = editor.Area });

    private static async Task WaitAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition was not met in time.");
            await Task.Delay(10);
        }
    }

    private static Task<bool> Run(Func<Task> test) =>
        HeadlessSession.Value.Dispatch(async () =>
        {
            await test();
            return true;
        }, TestContext.Current.CancellationToken);

    /// <summary>A two-mode hook: in normal mode, j and l move the caret and text is not inserted; i enters insert mode.</summary>
    private sealed class ModalHook : IEditorKeyHook
    {
        public bool Insert { get; set; }

        public bool OnKeyDown(IEditorView editor, EditorKeyPress key)
        {
            if (Insert)
            {
                editor.CaretStyle = EditorCaretStyle.Line;
                return false;
            }

            editor.CaretStyle = EditorCaretStyle.Block;
            if (key.Key == Key.J)
                editor.CaretOffset++;
            return true;
        }

        public bool OnTextInput(IEditorView editor, string text) => !Insert;
    }
}
