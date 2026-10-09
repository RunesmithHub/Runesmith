using Avalonia.Threading;
using Runesmith.Text;

namespace Runesmith.Editor.Tests;

public sealed class ChangeMarkerTests
{
    private const string Base = "one\ntwo\nthree\nfour\nfive";

    [Fact]
    public Task FindsAddedChangedAndDeletedLinesAgainstTheBase() => Run(async () =>
    {
        var area = new TextArea(new TestDocument("zero\none\nTWO\nthree\nfive"));

        area.BaseText = Base;
        await ChangesAsync(area);

        Assert.Equal([new DiffHunk(0, 0, 0, 1), new DiffHunk(1, 1, 2, 1), new DiffHunk(3, 1, 4, 0)], area.LineChanges);
        Assert.Equal(["two"], area.GetBaseLines(area.LineChanges[1]));
    });

    [Fact]
    public Task FollowsEditsAndMovesMarkersBelowAnEditAtOnce() => Run(async () =>
    {
        var document = new TestDocument(Base);
        var area = new TextArea(document) { BaseText = Base };
        await ChangesAsync(area);
        Assert.Empty(area.LineChanges);

        document.Buffer.Replace(new TextSpan(document.Buffer.Current.GetLine(3).Start, 4), "FOUR");
        await ChangesAsync(area);
        Assert.Equal([new DiffHunk(3, 1, 3, 1)], area.LineChanges);

        document.Buffer.Insert(0, "new\n");
        Assert.Equal([new DiffHunk(3, 1, 4, 1)], area.LineChanges);
        await ChangesAsync(area);
        Assert.Equal([new DiffHunk(0, 0, 0, 1), new DiffHunk(3, 1, 4, 1)], area.LineChanges);
    });

    [Theory]
    [InlineData("one\nTWO\nthree\nfour\nfive")]
    [InlineData("one\ntwo\nadded\nthree\nfour\nfive")]
    [InlineData("one\nthree\nfour\nfive")]
    [InlineData("one\ntwo\nthree\nfour\nFIVE")]
    [InlineData("one\ntwo\nthree\nfour\nfive\nsix\nseven")]
    [InlineData("one\ntwo\nthree")]
    [InlineData("ONE")]
    [InlineData("")]
    public Task RollsEachChangeBackToTheBaseAsOneUndoStep(string text) => Run(async () =>
    {
        var document = new TestDocument(text);
        var area = new TextArea(document) { BaseText = Base };
        await ChangesAsync(area);

        area.Rollback(area.LineChanges);

        Assert.Equal(Base, document.Buffer.Current.GetText());
        area.Undo();
        Assert.Equal(text, document.Buffer.Current.GetText());
    });

    [Fact]
    public Task RollsBackOnlyTheChangesInTheSelectedLines() => Run(async () =>
    {
        var document = new TestDocument("ONE\ntwo\nthree\nFOUR\nfive");
        var area = new TextArea(document) { BaseText = Base };
        await ChangesAsync(area);

        area.Select(EditorSelection.At(document.Buffer.Current.GetLine(3).Start + 1));
        area.Rollback(area.ChangesInSelection());

        Assert.Equal("ONE\ntwo\nthree\nfour\nfive", document.Buffer.Current.GetText());
    });

    [Fact]
    public Task MovesBetweenChangesGoingRoundAtTheEnds() => Run(async () =>
    {
        var document = new TestDocument("ONE\ntwo\nthree\nFOUR\nfive");
        var area = new TextArea(document) { BaseText = Base };
        await ChangesAsync(area);

        Assert.Equal(3, area.FindChange(next: true)!.Value.NewStart);
        area.GoToChange(area.FindChange(next: true)!.Value);
        Assert.Equal(0, area.FindChange(next: true)!.Value.NewStart);
        Assert.Equal(0, area.FindChange(next: false)!.Value.NewStart);
    });

    [Fact]
    public Task ClearingTheBaseClearsTheMarkers() => Run(async () =>
    {
        var area = new TextArea(new TestDocument("changed")) { BaseText = Base };
        await ChangesAsync(area);
        Assert.NotEmpty(area.LineChanges);

        area.BaseText = null;

        Assert.Empty(area.LineChanges);
    });

    [Fact]
    public Task AReadOnlyAreaIgnoresEdits() => Run(() =>
    {
        var document = new TestDocument("text");
        var area = new TextArea(document) { IsReadOnly = true };

        area.InsertText("more ");

        Assert.Equal("text", document.Buffer.Current.GetText());
        return Task.CompletedTask;
    });

    private static Task<bool> Run(Func<Task> test) =>
        HeadlessSession.Value.Dispatch(async () =>
        {
            await test();
            return true;
        }, TestContext.Current.CancellationToken);

    // Waits for the comparison that runs in the background after the debounce.
    private static async Task ChangesAsync(TextArea area)
    {
        var changed = new TaskCompletionSource();
        void OnChanged(object? sender, EventArgs e) => changed.TrySetResult();
        area.LineChangesChanged += OnChanged;
        await Task.WhenAny(changed.Task, Task.Delay(400, TestContext.Current.CancellationToken));
        area.LineChangesChanged -= OnChanged;
        Dispatcher.UIThread.RunJobs();
    }
}
