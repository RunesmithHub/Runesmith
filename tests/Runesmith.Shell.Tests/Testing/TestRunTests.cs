using Runesmith.Sdk.Running;
using Runesmith.Sdk.Testing;
using Runesmith.Shell.Testing;
using Runesmith.Text;
using TestResult = Runesmith.Shell.Testing.TestResult;

namespace Runesmith.Shell.Tests.Testing;

public sealed class TestRunTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly FakeTestProvider provider = new() { Items = Items.Calculator("Add", "Subtract", "Divide") };

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARunQueuesItsTestsAndKeepsWhatTheProviderReports()
    {
        var service = await DiscoveredAsync();
        var gate = new TaskCompletionSource();
        var states = new List<TestState>();
        provider.Run = async (request, run, token) =>
        {
            states.Add(service.Tree.Find(provider, Items.Id("Add"))!.State);
            run.Started(Items.Id("Add"));
            states.Add(service.Tree.Find(provider, Items.Id("Add"))!.State);
            run.AppendOutput("adding\n", Items.Id("Add"));
            run.Passed(Items.Id("Add"), TimeSpan.FromMilliseconds(12));
            run.Failed(Items.Id("Subtract"), new TestFailure("Assert.Equal() Failure") { Expected = "1", Actual = "2" }, TimeSpan.FromMilliseconds(3));
            run.Skipped(Items.Id("Divide"), "Not ready");
            await gate.Task;
        };

        var run = (await service.RunAllAsync())!;
        gate.SetResult();
        await run.Completion.WaitAsync(Timeout, Token);

        Assert.Equal([TestState.Queued, TestState.Running], states);
        var add = service.Tree.Find(provider, Items.Id("Add"))!;
        Assert.Equal(TestState.Passed, add.State);
        Assert.Equal(TimeSpan.FromMilliseconds(12), add.Result!.Duration);
        Assert.Equal("adding\n", add.Result.Output);
        Assert.Equal("adding\n", run.OutputOf(add));
        var subtract = service.Tree.Find(provider, Items.Id("Subtract"))!.Result!;
        Assert.Equal((TestState.Failed, "1", "2"), (subtract.State, subtract.Failure!.Expected, subtract.Failure.Actual));
        Assert.Equal("Not ready", service.Tree.Find(provider, Items.Id("Divide"))!.Result!.SkipReason);
        Assert.Equal(TestRunState.Finished, run.State);
        Assert.Contains("adding", run.Output.GetText(), StringComparison.Ordinal);
        Assert.Equal(1, run.Count()[TestState.Failed]);
    }

    [Fact]
    public async Task TheRequestNamesTheItemsTheUserRan()
    {
        var service = await DiscoveredAsync();
        var type = service.Tree.Find(provider, "Calculator.Tests.CalculatorTests")!;

        var run = (await service.RunAsync([type, service.Tree.Find(provider, Items.Id("Add"))!], RunMode.Run))!;
        await run.Completion.WaitAsync(Timeout, Token);

        var request = Assert.Single(provider.Requests);
        var item = Assert.Single(request.Tests);
        Assert.Equal("Calculator.Tests.CalculatorTests", item.Id);
        Assert.Equal(3, item.Children.Count);
        Assert.Equal(RunMode.Run, request.Mode);
        Assert.Equal("/", request.RootPath);
    }

    [Fact]
    public async Task TestsWithoutAResultGetBackTheirLastOne()
    {
        var service = await DiscoveredAsync();
        provider.Run = (_, run, _) =>
        {
            run.Passed(Items.Id("Add"));
            run.Failed(Items.Id("Subtract"), new TestFailure("no"));
            return Task.CompletedTask;
        };
        await (await service.RunAllAsync())!.Completion.WaitAsync(Timeout, Token);

        provider.Run = (_, run, _) =>
        {
            run.Started(Items.Id("Subtract"));
            return Task.CompletedTask;
        };
        await (await service.RunAllAsync())!.Completion.WaitAsync(Timeout, Token);

        Assert.Equal(TestState.Passed, service.Tree.Find(provider, Items.Id("Add"))!.State);
        Assert.Equal(TestState.Failed, service.Tree.Find(provider, Items.Id("Subtract"))!.State);
        Assert.Equal(TestState.None, service.Tree.Find(provider, Items.Id("Divide"))!.State);
    }

    [Fact]
    public async Task ReportsAboutOtherTestsOrAfterTheRunAreIgnored()
    {
        var service = await DiscoveredAsync();
        ITestRun? kept = null;
        provider.Run = (_, run, _) =>
        {
            kept = run;
            run.Passed(Items.Id("Subtract"));
            run.Passed("not a test");
            run.Passed(Items.Id("Add"));
            run.Started(Items.Id("Add"));
            return Task.CompletedTask;
        };

        var run = (await service.RunAsync([service.Tree.Find(provider, Items.Id("Add"))!]))!;
        await run.Completion.WaitAsync(Timeout, Token);
        kept!.Failed(Items.Id("Add"), new TestFailure("late"));

        Assert.Equal(TestState.Passed, service.Tree.Find(provider, Items.Id("Add"))!.State);
        Assert.Equal(TestState.None, service.Tree.Find(provider, Items.Id("Subtract"))!.State);
    }

    [Fact]
    public async Task StoppingCancelsTheProviderAndEndsTheRun()
    {
        var service = await DiscoveredAsync();
        var started = new TaskCompletionSource();
        provider.Run = async (_, run, token) =>
        {
            run.Started(Items.Id("Add"));
            started.SetResult();
            await Task.Delay(Timeout, token);
        };

        var run = (await service.RunAllAsync())!;
        await started.Task.WaitAsync(Timeout, Token);
        Assert.True(service.IsRunning);
        service.Stop();
        await run.Completion.WaitAsync(Timeout, Token);

        Assert.Equal(TestRunState.Stopped, run.State);
        Assert.False(service.IsRunning);
        Assert.Equal(TestState.None, service.Tree.Find(provider, Items.Id("Add"))!.State);
    }

    [Fact]
    public async Task AProviderThatThrowsEndsTheRunWithItsMessage()
    {
        var service = await DiscoveredAsync();
        provider.Run = (_, _, _) => throw new InvalidOperationException("The test host crashed.");

        var run = (await service.RunAllAsync())!;
        await run.Completion.WaitAsync(Timeout, Token);

        Assert.Equal(TestRunState.Finished, run.State);
        Assert.Contains("The test host crashed.", run.Output.GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CasesFoundWhileRunningCanBeReported()
    {
        var service = await DiscoveredAsync();
        provider.Run = (_, run, _) =>
        {
            run.AddItem(Items.Id("Add"), new TestItem(Items.Id("Add") + "(1)", "Add(1)", TestItemKind.Case));
            run.AddItem(Items.Id("Add"), new TestItem(Items.Id("Add") + "(2)", "Add(2)", TestItemKind.Case));
            run.Passed(Items.Id("Add") + "(1)");
            run.Failed(Items.Id("Add") + "(2)", new TestFailure("no"));
            return Task.CompletedTask;
        };

        var run = (await service.RunAsync([service.Tree.Find(provider, Items.Id("Add"))!]))!;
        await run.Completion.WaitAsync(Timeout, Token);

        var add = service.Tree.Find(provider, Items.Id("Add"))!;
        Assert.Equal(2, add.Children.Count);
        Assert.Equal(TestState.Failed, add.State);
        Assert.Equal(TestState.Passed, add.Children[0].State);
    }

    [Fact]
    public async Task RerunningFailedTestsRunsOnlyThem()
    {
        var service = await DiscoveredAsync();
        provider.Run = (_, run, _) =>
        {
            run.Passed(Items.Id("Add"));
            run.Failed(Items.Id("Subtract"), new TestFailure("no"));
            run.Errored(Items.Id("Divide"), new TestFailure("crash"));
            return Task.CompletedTask;
        };
        await (await service.RunAllAsync())!.Completion.WaitAsync(Timeout, Token);

        await (await service.RerunFailedAsync())!.Completion.WaitAsync(Timeout, Token);

        Assert.Equal([Items.Id("Subtract"), Items.Id("Divide")], provider.Requests[^1].Tests.Select(t => t.Id));
        await (await service.RerunLastAsync())!.Completion.WaitAsync(Timeout, Token);
        Assert.Equal(provider.Requests[^2].Tests.Select(t => t.Id), provider.Requests[^1].Tests.Select(t => t.Id));
    }

    [Fact]
    public async Task DebuggingNeedsAProviderThatCanDebug()
    {
        var service = await DiscoveredAsync();

        Assert.Null(await service.RunAllAsync(RunMode.Debug));

        provider.CanDebug = true;
        int? exitCode = 0;
        provider.Run = async (request, run, token) =>
        {
            Assert.Equal(RunMode.Debug, request.Mode);
            exitCode = await run.DebugAsync(new LaunchPlan("dotnet", ["test.dll"], "/") { Debug = new DebugLaunch("dotnet", new Dictionary<string, string>()) }, token);
        };
        var run = (await service.RunAllAsync(RunMode.Debug))!;
        await run.Completion.WaitAsync(Timeout, Token);

        Assert.Null(exitCode);
        Assert.Contains("cannot be debugged", run.Output.GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANewRunStopsTheOneThatGoesOn()
    {
        var service = await DiscoveredAsync();
        provider.Run = (_, _, token) => Task.Delay(Timeout, token);
        var first = (await service.RunAllAsync())!;

        provider.Run = (_, run, _) =>
        {
            run.Passed(Items.Id("Add"));
            return Task.CompletedTask;
        };
        var second = (await service.RunAllAsync())!;
        await second.Completion.WaitAsync(Timeout, Token);

        Assert.Equal(TestRunState.Stopped, first.State);
        Assert.Equal(TestRunState.Finished, second.State);
    }

    [Fact]
    public async Task TheTestAtALineIsTheInnermostOneAroundIt()
    {
        var service = await DiscoveredAsync();

        Assert.Equal(Items.Id("Add"), service.FindAt(Items.File, 5)!.Id);
        Assert.Equal("Calculator.Tests.CalculatorTests", service.FindAt(Items.File, 3)!.Id);
        Assert.Null(service.FindAt(Items.File, 0));
        Assert.Null(service.FindAt("/elsewhere.cs", 5));
    }

    [Fact]
    public async Task AFilesDiscoveryUsesTheProvidersAnswerOrTheWholeFolder()
    {
        var service = await DiscoveredAsync();
        Assert.Equal(1, provider.Discoveries);

        await service.DiscoverFileAsync(Items.File, TextSnapshot.Create("class CalculatorTests {}"), Token);
        Assert.Equal(2, provider.Discoveries);

        provider.Document = context => context.Snapshot.GetText().Contains("Modulo", StringComparison.Ordinal) ? Items.Calculator("Modulo") : [];
        await service.DiscoverFileAsync(Items.File, TextSnapshot.Create("void Modulo() {}"), Token);

        Assert.Equal(2, provider.Discoveries);
        Assert.NotNull(service.Tree.Find(provider, Items.Id("Modulo")));
        Assert.Null(service.Tree.Find(provider, Items.Id("Add")));
    }

    [Fact]
    public async Task ManyChangedFilesDiscoverTheWholeFolderOnce()
    {
        var service = await DiscoveredAsync();

        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => service.DiscoverFileAsync($"/src/File{i}.cs", TextSnapshot.Create("x"), Token)));

        Assert.Equal(2, provider.Discoveries);
    }

    private async Task<TestService> DiscoveredAsync()
    {
        var service = Items.Service(provider);
        await service.DiscoverAllAsync();
        return service;
    }
}
