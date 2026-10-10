using Runesmith.Sdk.Build;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Testing;
using Runesmith.Shell.Testing;
using Runesmith.Text;
using TestResult = Runesmith.Shell.Testing.TestResult;

namespace Runesmith.Shell.Tests.Testing;

/// <summary>What failed tests show outside the Test Explorer: problems, and gutter markers, code lenses and highlights in the editor.</summary>
public sealed class TestFeedbackTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly FakeTestProvider provider = new() { Items = Items.Calculator("Add", "Subtract") };
    private readonly RecordingDiagnostics diagnostics = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AFailedAssertionIsAProblemAtTheLineItFailedOn()
    {
        var service = Items.Service(provider, diagnostics, root: "/src");
        await service.DiscoverAllAsync();
        provider.Run = (_, run, _) =>
        {
            run.Failed(Items.Id("Add"), new TestFailure("Assert.Equal() Failure: Values differ")
            {
                Expected = "4",
                Actual = "5",
                StackTrace =
                [
                    new TestStackFrame("at Xunit.Assert.Equal()") { FilePath = "/xunit/Assert.cs", Position = new TextPosition(99, 0) },
                    new TestStackFrame("at CalculatorTests.Add() in /src/CalculatorTests.cs:line 7") { FilePath = Items.File, Position = new TextPosition(6, 8) },
                ],
            });
            run.Passed(Items.Id("Subtract"));
            return Task.CompletedTask;
        };

        await (await service.RunAllAsync())!.Completion.WaitAsync(Timeout, Token);
        var problem = await WaitForAsync(() => diagnostics.Get(Items.File).SingleOrDefault());

        Assert.Equal(new TextPosition(6, 8), problem.Start);
        Assert.Equal(DiagnosticSeverity.Error, problem.Severity);
        Assert.Equal(TestService.DiagnosticSource, problem.Source);
        Assert.Equal("Add: Assert.Equal() Failure: Values differ (expected 4, actual 5)", problem.Message);
        Assert.Empty(diagnostics.Get("/xunit/Assert.cs"));
    }

    [Fact]
    public async Task APassAfterAFailureClearsTheProblem()
    {
        var service = Items.Service(provider, diagnostics, root: "/src");
        await service.DiscoverAllAsync();
        provider.Run = (_, run, _) =>
        {
            run.Failed(Items.Id("Add"), new TestFailure("no") { FilePath = Items.File, Position = new TextPosition(5, 0) });
            return Task.CompletedTask;
        };
        await (await service.RunAllAsync())!.Completion.WaitAsync(Timeout, Token);
        await WaitForAsync(() => diagnostics.Get(Items.File).SingleOrDefault());

        provider.Run = (_, run, _) =>
        {
            run.Passed(Items.Id("Add"));
            return Task.CompletedTask;
        };
        await (await service.RunAllAsync())!.Completion.WaitAsync(Timeout, Token);

        await WaitForAsync(() => diagnostics.Get(Items.File).Count == 0 ? "cleared" : null);
    }

    [Fact]
    public void EachTestAndGroupGetsAMarkerAndCodeLenses()
    {
        var tree = new TestTree();
        tree.ReplaceAll(provider, Items.Calculator("Add", "Subtract"));
        provider.CanDebug = true;
        tree.SetResults([(tree.Find(provider, Items.Id("Add"))!, new TestResult(TestState.Passed) { Duration = TimeSpan.FromMilliseconds(12) })]);
        var snapshot = Snapshot(20);

        var decorations = TestDecorations.Decorate(Items.File, snapshot, tree, "/src");

        var markers = decorations.OfType<GutterMarker>().ToList();
        Assert.Equal([2, 4, 10], markers.Select(m => snapshot.GetLineFromPosition(m.Span.Start).LineNumber));
        Assert.Equal([TestIcons.Passed, TestIcons.Passed, TestIcons.Run], markers.Select(m => m.Icon));
        Assert.All(markers, m => Assert.Equal(TestCommands.RunItem, m.CommandId));
        Assert.Same(tree.Find(provider, Items.Id("Add")), markers[1].CommandArgument);
        Assert.Equal(DecorationTone.Success, markers[1].Tone);

        var lenses = decorations.OfType<CodeLens>().Where(l => snapshot.GetLineFromPosition(l.Span.Start).LineNumber == 4).ToList();
        Assert.Equal(["Run", "Debug", "Passed in 12 ms"], lenses.Select(l => l.Text));
        Assert.Equal([TestCommands.RunItem, TestCommands.DebugItem, TestCommands.ShowItem], lenses.Select(l => l.CommandId));
    }

    [Fact]
    public void AFailedTestsLineIsHighlightedWithItsMessage()
    {
        var tree = new TestTree();
        tree.ReplaceAll(provider, Items.Calculator("Add", "Subtract"));
        var failure = new TestFailure("Expected 4\nbut was 5") { FilePath = Items.File, Position = new TextPosition(6, 8) };
        tree.SetResults([(tree.Find(provider, Items.Id("Add"))!, new TestResult(TestState.Failed) { Failure = failure })]);
        var snapshot = Snapshot(20);

        var decorations = TestDecorations.Decorate(Items.File, snapshot, tree, "/src");

        var marker = decorations.OfType<GutterMarker>().Single(m => snapshot.GetLineFromPosition(m.Span.Start).LineNumber == 4);
        Assert.Equal((TestIcons.Failed, DecorationTone.Error), (marker.Icon, marker.Tone));
        var highlight = Assert.Single(decorations.OfType<TextHighlight>());
        Assert.Equal(6, snapshot.GetLineFromPosition(highlight.Span.Start).LineNumber);
        Assert.Equal(DecorationTone.Error, highlight.Tone);
        Assert.True(highlight.Background);
        var hint = Assert.Single(decorations.OfType<InlayHint>());
        Assert.Equal(snapshot.GetLine(6).End, hint.Offset);
        Assert.Equal(InlayHintSide.After, hint.Side);
        Assert.Equal("  Expected 4", hint.Text);
        Assert.Contains(decorations.OfType<CodeLens>(), l => l.Text == "Failed: Expected 4");
    }

    [Fact]
    public void AFailureInAnotherFileNamesItsTest()
    {
        var tree = new TestTree();
        tree.ReplaceAll(provider, Items.Calculator("Add"));
        var failure = new TestFailure("Index out of range") { FilePath = Items.OtherFile, Position = new TextPosition(2, 0) };
        tree.SetResults([(tree.Find(provider, Items.Id("Add"))!, new TestResult(TestState.Errored) { Failure = failure })]);

        var hint = Assert.Single(TestDecorations.Decorate(Items.OtherFile, Snapshot(5), tree, "/src").OfType<InlayHint>());

        Assert.Equal("  Add: Index out of range", hint.Text);
    }

    [Fact]
    public void TestsOutsideTheSnapshotAndOtherFilesAreLeftOut()
    {
        var tree = new TestTree();
        tree.ReplaceAll(provider, Items.Calculator("Add", "Subtract"));

        Assert.Empty(TestDecorations.Decorate(Items.OtherFile, Snapshot(20), tree, "/src"));
        Assert.Single(TestDecorations.Decorate(Items.File, Snapshot(3), tree, "/src").OfType<GutterMarker>());
    }

    [Theory]
    [InlineData(0.4, "0 ms")]
    [InlineData(12, "12 ms")]
    [InlineData(1460, "1.5 s")]
    public void DurationsReadInMillisecondsOrSeconds(double milliseconds, string expected) =>
        Assert.Equal(expected, TestDecorations.Format(TimeSpan.FromMilliseconds(milliseconds)).Replace(',', '.'));

    private static TextSnapshot Snapshot(int lines) => TextSnapshot.Create(string.Join('\n', Enumerable.Range(0, lines).Select(i => $"        line {i}")));

    private static async Task<T> WaitForAsync<T>(Func<T?> find)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (true)
        {
            if (find() is { } found)
                return found;
            Assert.True(DateTime.UtcNow < deadline, "The problems did not change in time.");
            await Task.Delay(20);
        }
    }
}
