using System.Diagnostics;
using System.Text;
using Runesmith.Sdk.Running;
using Runesmith.Sdk.Testing;
using Runesmith.Shell.Running;

namespace Runesmith.Shell.Testing;

/// <summary>Where a run of tests is.</summary>
public enum TestRunState
{
    /// <summary>The providers run the tests.</summary>
    Running,

    /// <summary>Every provider finished.</summary>
    Finished,

    /// <summary>The user stopped the run.</summary>
    Stopped,
}

/// <summary>One run of tests: the tests it runs, the results providers report, its output and its state.</summary>
/// <remarks>A test of the run is queued until a provider starts it, running until it reports a result, and keeps that result. Reports about
/// tests outside the run, and reports once the run ended, are ignored. When the run ends, tests that got no result go back to the result
/// they had before. Its members can be called from any thread.</remarks>
public sealed class TestRunSession : IDisposable
{
    private readonly TestTree tree;
    private readonly Func<LaunchPlan, TestRunSession, CancellationToken, Task<int?>>? debug;
    private readonly Dictionary<TestNode, TestResult?> before = [];
    private readonly HashSet<TestNode> inRun = [];
    private readonly Dictionary<TestNode, StringBuilder> outputs = [];
    private readonly CancellationTokenSource cancel = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock gate = new();

    internal TestRunSession(TestTree tree, IReadOnlyList<TestNode> nodes, RunMode mode, Func<LaunchPlan, TestRunSession, CancellationToken, Task<int?>>? debug)
    {
        this.tree = tree;
        this.debug = debug;
        Mode = mode;
        Nodes = nodes;
        var queued = new List<(TestNode, TestResult?)>();
        lock (tree.Gate)
        {
            foreach (var test in nodes.SelectMany(n => n.Tests()).Distinct())
            {
                inRun.Add(test);
                before[test] = test.Result;
                queued.Add((test, new TestResult(TestState.Queued)));
            }

            foreach (var group in nodes.SelectMany(n => n.Descendants()).Where(n => !n.IsTest))
                inRun.Add(group);
        }

        tree.SetResults(queued);
    }

    /// <summary>Gets the nodes the user asked to run.</summary>
    public IReadOnlyList<TestNode> Nodes { get; }

    public RunMode Mode { get; }

    public TestRunState State { get; private set; }

    /// <summary>Gets the run's output: what providers wrote, with each test's output.</summary>
    public ConsoleBuffer Output { get; } = new();

    /// <summary>Gets how long the run has taken, or took.</summary>
    public TimeSpan Elapsed => clock.Elapsed;

    /// <summary>Gets a token canceled when the run is stopped.</summary>
    public CancellationToken Token => cancel.Token;

    /// <summary>Gets a task that ends when the run does.</summary>
    public Task Completion => completion.Task;

    /// <summary>Raised when a test of the run starts or ends, and when the run ends.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the tests of the run by state.</summary>
    public IReadOnlyDictionary<TestState, int> Count()
    {
        lock (tree.Gate)
            return inRun.Where(n => n.IsTest).GroupBy(n => n.State).ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>Gets what a test of the run wrote so far.</summary>
    public string OutputOf(TestNode node)
    {
        lock (outputs)
            return outputs.TryGetValue(node, out var text) ? text.ToString() : "";
    }

    /// <summary>Stops the run: its providers are canceled.</summary>
    public void Stop()
    {
        if (State != TestRunState.Running)
            return;

        try
        {
            cancel.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose() => cancel.Dispose();

    /// <summary>Gets a reporter for one provider's part of the run.</summary>
    internal ITestRun For(ITestProvider provider) => new Reporter(this, provider);

    /// <summary>Ends the run; tests without a result get back the one they had.</summary>
    internal void Finish()
    {
        lock (gate)
        {
            var restored = new List<(TestNode, TestResult?)>();
            lock (tree.Gate)
            {
                if (State != TestRunState.Running)
                    return;

                State = cancel.IsCancellationRequested ? TestRunState.Stopped : TestRunState.Finished;
                clock.Stop();
                foreach (var test in inRun.Where(n => n.IsTest && n.Result?.State is TestState.Queued or TestState.Running))
                    restored.Add((test, before.GetValueOrDefault(test)));
            }

            tree.SetResults(restored);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        completion.TrySetResult();
    }

    private TestNode? Find(ITestProvider provider, string id)
    {
        if (State != TestRunState.Running || tree.Find(provider, id) is not { } node)
            return null;

        lock (tree.Gate)
            return inRun.Contains(node) ? node : null;
    }

    private void Report(ITestProvider provider, string id, Func<TestNode, TestResult> result)
    {
        lock (gate)
        {
            if (Find(provider, id) is not { } node)
                return;

            var value = result(node);
            TestState? current;
            lock (tree.Gate)
                current = node.Result?.State;
            if (value.State == TestState.Running && current is not (TestState.Queued or TestState.None or null))
                return;

            tree.SetResults([(node, value with { Output = OutputOf(node) })]);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Append(ITestProvider provider, string text, string? id)
    {
        if (State != TestRunState.Running)
            return;

        Output.Append(text, ConsoleSource.Output);
        if (id is null || Find(provider, id) is not { } node)
            return;

        lock (outputs)
        {
            if (!outputs.TryGetValue(node, out var builder))
                outputs[node] = builder = new StringBuilder();
            builder.Append(text);
        }
    }

    private void Add(ITestProvider provider, string parentId, TestItem item)
    {
        if (State != TestRunState.Running)
            return;

        lock (gate)
        {
            if (Find(provider, parentId) is null || tree.AddChild(provider, parentId, item) is not { } node)
                return;

            lock (tree.Gate)
            {
                foreach (var added in node.Descendants())
                    inRun.Add(added);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    // What one provider reports to; it tells the run which provider's ids the reports name.
    private sealed class Reporter(TestRunSession run, ITestProvider provider) : ITestRun
    {
        public void Started(string testId) => run.Report(provider, testId, _ => new TestResult(TestState.Running));

        public void Passed(string testId, TimeSpan? duration = null) => run.Report(provider, testId, _ => new TestResult(TestState.Passed) { Duration = duration });

        public void Failed(string testId, TestFailure failure, TimeSpan? duration = null)
        {
            ArgumentNullException.ThrowIfNull(failure);
            run.Report(provider, testId, _ => new TestResult(TestState.Failed) { Duration = duration, Failure = failure });
        }

        public void Errored(string testId, TestFailure failure, TimeSpan? duration = null)
        {
            ArgumentNullException.ThrowIfNull(failure);
            run.Report(provider, testId, _ => new TestResult(TestState.Errored) { Duration = duration, Failure = failure });
        }

        public void Skipped(string testId, string? reason = null) => run.Report(provider, testId, _ => new TestResult(TestState.Skipped) { SkipReason = reason });

        public void AppendOutput(string text, string? testId = null)
        {
            ArgumentNullException.ThrowIfNull(text);
            run.Append(provider, text, testId);
        }

        public void AddItem(string parentId, TestItem item)
        {
            ArgumentNullException.ThrowIfNull(item);
            run.Add(provider, parentId, item);
        }

        public Task<int?> DebugAsync(LaunchPlan plan, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(plan);
            if (run.debug is null)
            {
                run.Output.AppendLine("No installed plugin provides a debugger, so the tests cannot be debugged.", ConsoleSource.Error);
                return Task.FromResult<int?>(null);
            }

            return run.debug(plan, run, cancellationToken);
        }
    }
}
