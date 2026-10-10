using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Documents;
using Runesmith.Text;

namespace Runesmith.Editor.Tests;

public sealed class GlyphMarginTests
{
    private static readonly string FilePath = Path.Combine(Path.GetTempPath(), "runesmith-tests", "Program.cs");

    [Fact]
    public Task AClickBesideALineTogglesABreakpointThere() => Run(async () =>
    {
        var fakes = new FakeEditorServices();
        using var editor = new TextEditor(new FileTestDocument(FilePath, "one\ntwo\nthree\n"), fakes.Create());
        var window = Show(editor);

        Click(window, editor, 2);

        var (id, argument) = Assert.Single(fakes.Commands.Ran);
        Assert.Equal(CommandIds.ToggleBreakpoint, id);
        var target = Assert.IsType<ContextMenuTarget>(argument);
        Assert.Equal(FilePath, target.Path);
        Assert.Equal((3, 3), target.Lines);
        Assert.Equal(0, editor.Area.Selection.Span.Length);
        window.Close();
        await Task.CompletedTask;
    });

    [Fact]
    public Task AClickOnAMarkerRunsTheMarkersCommandInstead() => Run(async () =>
    {
        var fakes = new FakeEditorServices();
        using var editor = new TextEditor(new FileTestDocument(FilePath, "one\ntwo\n"), fakes.Create());
        var window = Show(editor);
        editor.Area.SetDecorations(this, [new GutterMarker(new TextSpan(4, 0), "circle") { IsFilled = true, CommandId = "test.marker", CommandArgument = 7 }]);

        Click(window, editor, 1);

        Assert.Equal(("test.marker", (object?)7), Assert.Single(fakes.Commands.Ran));
        window.Close();
        await Task.CompletedTask;
    });

    [Fact]
    public Task EditorsWithoutDecorationsOrAFileSelectTheLineInstead() => Run(async () =>
    {
        var fakes = new FakeEditorServices();
        using var diff = new TextEditor(new FileTestDocument(FilePath, "one\ntwo\n"), fakes.Create()) { IsDecorated = false };
        var window = Show(diff);

        Click(window, diff, 0);

        Assert.Empty(fakes.Commands.Ran);
        Assert.Equal(4, diff.Area.Selection.Span.Length);
        window.Close();
        await Task.CompletedTask;
    });

    private static void Click(Window window, TextEditor editor, int line)
    {
        var area = editor.Area;
        var point = area.TranslatePoint(new Point(12, (line + 0.5) * area.LineHeight), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }

    private static Window Show(Control content)
    {
        var window = new Window { Width = 600, Height = 400, Content = content };
        window.Show();
        return window;
    }

    private static Task<bool> Run(Func<Task> test) =>
        HeadlessSession.Value.Dispatch(async () =>
        {
            await test();
            return true;
        }, TestContext.Current.CancellationToken);
}
