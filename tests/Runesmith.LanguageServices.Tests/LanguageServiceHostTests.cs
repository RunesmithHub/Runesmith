using Runesmith.Text;

namespace Runesmith.LanguageServices.Tests;

public sealed class LanguageServiceHostTests
{
    private static LanguageServiceHost CreateHost(FakeAnalyzer analyzer, List<string>? log = null)
    {
        var host = new LanguageServiceHost(Path.GetTempPath(), (name, line) =>
        {
            if (log is not null)
                lock (log)
                    log.Add($"{name}: {line}");
        });
        host.Add(analyzer);
        return host;
    }

    [Fact]
    public async Task PassesDocumentsWithGrowingVersionsToTheAnalyzerOfTheirLanguage()
    {
        var analyzer = new FakeAnalyzer();
        await using var host = CreateHost(analyzer);
        var snapshot = TextSnapshot.Create("abc");
        host.Open("/a.fake", "fake", snapshot);
        host.Change("/a.fake", snapshot.Apply([new TextChange(new TextSpan(3, 0), "d")]), [new TextChange(new TextSpan(3, 0), "d")]);
        host.Open("/b.other", "other", snapshot);
        host.Close("/a.fake");

        Assert.Equal(["open /a.fake 1", "change /a.fake 2", "close /a.fake"], analyzer.Calls);
        Assert.Null(host.GetDocument("/a.fake"));
        Assert.Equal(1, host.GetDocument("/b.other")?.Version);
    }

    [Fact]
    public async Task AnAnalyzerWhoseProblemsFailIsNotAskedAgainUntilTheDocumentChanges()
    {
        var analyzer = new FakeAnalyzer { ThrowOnDiagnostics = true };
        var log = new List<string>();
        await using var host = CreateHost(analyzer, log);
        host.Open("/a.fake", "fake", TextSnapshot.Create("bad"));

        await Task.Delay(500, TestContext.Current.CancellationToken);

        Assert.Equal(1, analyzer.DiagnosticsRuns);
        Assert.Single(log, line => line.Contains("broken", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CompletesFilteredRankedAndResolvable()
    {
        var analyzer = new FakeAnalyzer("WriteLine", "Write", "Read", "ReadLine");
        await using var host = CreateHost(analyzer);
        var snapshot = TextSnapshot.Create("Console.Wri");
        host.Open("/a.fake", "fake", snapshot);

        var result = await host.CompleteAsync("/a.fake", snapshot, snapshot.Length, CompletionTriggerKind.Invoked, null, TestContext.Current.CancellationToken);

        Assert.Equal(["Write", "WriteLine"], result.Items.Select(i => i.Label));
        Assert.Equal(new TextSpan(8, 3), result.ReplaceSpan);
        Assert.Same(analyzer, result.Items[0].Analyzer);
        var details = await host.ResolveAsync(result.Items[0], TestContext.Current.CancellationToken);
        Assert.Equal("detail of Write", details?.Detail);
    }

    [Fact]
    public async Task AnswersForTheVersionOfTheGivenSnapshot()
    {
        var analyzer = new FakeAnalyzer();
        await using var host = CreateHost(analyzer);
        var first = TextSnapshot.Create("abc");
        host.Open("/a.fake", "fake", first);
        var second = first.Apply([new TextChange(new TextSpan(0, 0), "x")]);
        host.Change("/a.fake", second, [new TextChange(new TextSpan(0, 0), "x")]);

        var old = await host.HoverAsync("/a.fake", first, 1, TestContext.Current.CancellationToken);
        var current = await host.HoverAsync("/a.fake", second, 1, TestContext.Current.CancellationToken);

        Assert.Equal("hover at 1 in version 1", old?.Markdown);
        Assert.Equal("hover at 1 in version 2", current?.Markdown);
        Assert.Equal(new TextSpan(2, 1), host.MapToLatest("/a.fake", 1, new TextSpan(1, 1)));
    }

    [Fact]
    public async Task FindsProblemsAfterThePauseAndOnlyForTheLatestVersion()
    {
        var analyzer = new FakeAnalyzer();
        await using var host = CreateHost(analyzer);
        var found = new List<(int Version, int Count)>();
        host.ProblemsFound += (document, problems) =>
        {
            lock (found)
                found.Add((document.Version, problems.Count));
        };

        var snapshot = TextSnapshot.Create("bad");
        host.Open("/a.fake", "fake", snapshot);
        await WaitUntil(() => found.Count == 1);
        Assert.Equal((1, 1), found[0]);

        for (var i = 0; i < 5; i++)
        {
            var next = snapshot.Apply([new TextChange(new TextSpan(snapshot.Length, 0), " bad")]);
            host.Change("/a.fake", next, [new TextChange(new TextSpan(snapshot.Length, 0), " bad")]);
            snapshot = next;
        }

        await WaitUntil(() => found.Count == 2);
        await Task.Delay(LanguageServiceHost.DiagnosticsDelay * 2, TestContext.Current.CancellationToken);
        Assert.Equal(2, found.Count);
        Assert.Equal((6, 6), found[1]);
    }

    [Fact]
    public async Task AFailingAnalyzerAnswersNothingAndIsLogged()
    {
        var analyzer = new FakeAnalyzer("a") { ThrowOnComplete = true };
        var log = new List<string>();
        await using var host = CreateHost(analyzer, log);
        var snapshot = TextSnapshot.Create("");
        host.Open("/a.fake", "fake", snapshot);

        var result = await host.CompleteAsync("/a.fake", snapshot, 0, CompletionTriggerKind.Invoked, null, TestContext.Current.CancellationToken);

        Assert.Empty(result.Items);
        Assert.Contains(log, line => line.Contains("broken", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RequestsForUnknownDocumentsAnswerNothing()
    {
        await using var host = CreateHost(new FakeAnalyzer("a"));
        var result = await host.CompleteAsync("/missing", TextSnapshot.Empty, 0, CompletionTriggerKind.Invoked, null, TestContext.Current.CancellationToken);
        Assert.Empty(result.Items);
        Assert.Null(await host.HoverAsync("/missing", TextSnapshot.Empty, 0, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisposingDisposesTheAnalyzers()
    {
        var analyzer = new FakeAnalyzer();
        var host = CreateHost(analyzer);
        await host.DisposeAsync();
        Assert.Contains("dispose", analyzer.Calls);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }
}
