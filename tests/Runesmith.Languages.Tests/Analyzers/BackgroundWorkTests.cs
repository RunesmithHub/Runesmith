using Runesmith.Languages.Analyzers;
using Runesmith.Sdk.Tasks;

namespace Runesmith.Languages.Tests.Analyzers;

public sealed class BackgroundWorkTests
{
    [Theory]
    [InlineData("Load sample projects", "Loading sample projects")]
    [InlineData("Load other projects", "Loading other projects")]
    [InlineData("Warm up the analyzer", "Warming up the analyzer")]
    [InlineData("Reload a closed other file", "Reloading a closed other file")]
    [InlineData("Indexing", "Indexing")]
    public void DescribesWorkAsWhatItIsDoing(string name, string expected) => Assert.Equal(expected, AnalyzerBridge.Describe(name));

    [Theory]
    [InlineData("Load sample projects", true)]
    [InlineData("Load the library", true)]
    [InlineData("Prefetch sample completion", false)]
    [InlineData("Speculate sample member access", false)]
    [InlineData("Bring a document up to date", false)]
    public void ReportsAllButTheWorkThatFollowsKeyPresses(string name, bool expected) => Assert.Equal(expected, AnalyzerBridge.IsReported(name));

    [Fact]
    public void ShowsWorkOnlyOnceItHasRunForAWhile()
    {
        var tasks = new RecordingTasks();

        using (new DelayedTask(tasks, "Loading sample projects", TimeSpan.FromSeconds(30)))
        {
        }

        Assert.Equal(0, tasks.Started);
    }

    [Fact]
    public async Task ShowsLongWorkUntilItEnds()
    {
        var tasks = new RecordingTasks();

        var work = new DelayedTask(tasks, "Loading other projects", TimeSpan.FromMilliseconds(10));
        await tasks.FirstStart.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Single(tasks.Running);

        work.Dispose();
        Assert.Empty(tasks.Running);
    }

    private sealed class RecordingTasks : IBackgroundTasks
    {
        private readonly List<IBackgroundTask> running = [];

        public int Started { get; private set; }

        public TaskCompletionSource FirstStart { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<IBackgroundTask> Running
        {
            get
            {
                lock (running)
                    return [.. running];
            }
        }

        public event EventHandler? Changed;

        public IBackgroundTask Start(string title, Action? cancel = null)
        {
            var task = new Recorded(title, this);
            lock (running)
                running.Add(task);
            Started++;
            Changed?.Invoke(this, EventArgs.Empty);
            FirstStart.TrySetResult();
            return task;
        }

        private sealed class Recorded(string title, RecordingTasks owner) : IBackgroundTask
        {
            public string Title => title;

            public string? Detail => null;

            public double? Fraction => null;

            public void Report(string? detail, double? fraction = null)
            {
            }

            public void Dispose()
            {
                lock (owner.running)
                    owner.running.Remove(this);
            }
        }
    }
}
