using System.Composition;
using Runesmith.Sdk.Build;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Messaging;
using Runesmith.Sdk.Running;
using Runesmith.Sdk.Settings;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.Tasks;
using Runesmith.Sdk.Testing;
using Runesmith.Sdk.Workspace;
using Runesmith.Shell.Debugging;
using Runesmith.Shell.Running;
using Runesmith.Text;

namespace Runesmith.Shell.Testing;

/// <summary>Finds the tests of the open folder with the test providers plugins export, keeps them up to date as files change, runs and
/// debugs them, and reports failed assertions in the Problems panel.</summary>
[Export]
[Shared]
public sealed class TestService : IDisposable
{
    /// <summary>The source failed tests are reported under in the Problems panel.</summary>
    public const string DiagnosticSource = "tests";

    /// <summary>The Output channel providers' errors go to.</summary>
    public const string ChannelName = "Tests";

    private static readonly TimeSpan TypingDelay = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan FileDelay = TimeSpan.FromMilliseconds(300);

    private readonly IEnumerable<Lazy<ITestProvider>> providerExports;
    private readonly IWorkspace workspace;
    private readonly IDiagnosticService diagnostics;
    private readonly Func<bool> autoRunOnSave;
    private readonly Action<string> log;
    private readonly IBackgroundTasks? tasks;
    private readonly Func<DebugService?> debugger;
    private readonly Lock gate = new();
    private readonly Dictionary<string, CancellationTokenSource> pendingFiles = new(PathKey.Comparer);
    private readonly HashSet<string> reportedFiles = new(PathKey.Comparer);
    private readonly List<IDisposable> subscriptions = [];
    private IReadOnlyList<ITestProvider>? providers;
    private int failuresPending;
    private CancellationTokenSource discovery = new();
    private IReadOnlyList<(ITestProvider Provider, string Id)> lastRun = [];
    private RunMode lastMode;

    [ImportingConstructor]
    public TestService([ImportMany] IEnumerable<Lazy<ITestProvider>> providers, IWorkspace workspace, IDocumentService documents, IMessageBus messages,
        IDiagnosticService diagnostics, ISettingsService settings, IOutputService output, [Import(AllowDefault = true)] IBackgroundTasks? tasks,
        [Import(AllowDefault = true)] Lazy<DebugService>? debugger)
        : this(providers, workspace, diagnostics, () => settings.Get<bool>(TestSettings.AutoRunOnSave), line => output.GetChannel(ChannelName).AppendLine(line), tasks,
            () => debugger?.Value)
    {
        workspace.Changed += (_, _) => _ = DiscoverAllAsync();
        documents.Opened += (_, e) => Follow(e.Document);
        documents.Saved += (_, e) =>
        {
            if (e.Document.FilePath is { } path)
                _ = OnSavedAsync(path, e.Document.Buffer.Current);
        };
        documents.ChangedOnDisk += (_, e) => Schedule(e.Document.FilePath, e.Document.Buffer.Current, FileDelay);
        foreach (var document in documents.Documents)
            Follow(document);
        if (workspace.RootPath is not null)
            _ = DiscoverAllAsync();
        subscriptions.Add(messages.Subscribe<FilesChangedMessage>(m =>
        {
            foreach (var path in m.Paths)
                Schedule(path, null, FileDelay);
        }));
    }

    internal TestService(IEnumerable<Lazy<ITestProvider>> providers, IWorkspace workspace, IDiagnosticService diagnostics, Func<bool> autoRunOnSave,
        Action<string> log, IBackgroundTasks? tasks, Func<DebugService?> debugger)
    {
        providerExports = providers;
        this.workspace = workspace;
        this.diagnostics = diagnostics;
        this.autoRunOnSave = autoRunOnSave;
        this.log = log;
        this.tasks = tasks;
        this.debugger = debugger;
        Tree.ResultsChanged += (_, _) => ScheduleFailures();
        Tree.Changed += (_, _) => ScheduleFailures();
    }

    /// <summary>Gets the tests every provider found, with their results.</summary>
    public TestTree Tree { get; } = new();

    /// <summary>Gets the test providers plugins export; one that fails to load is left out.</summary>
    public IReadOnlyList<ITestProvider> Providers => providers ??= LoadProviders();

    /// <summary>Gets the run that goes on, or the last one, or null before the first.</summary>
    public TestRunSession? CurrentRun { get; private set; }

    /// <summary>Gets whether tests run now.</summary>
    public bool IsRunning => CurrentRun is { State: TestRunState.Running };

    /// <summary>Gets whether a discovery goes on.</summary>
    public bool IsDiscovering { get; private set; }

    /// <summary>Gets whether a run can be repeated.</summary>
    public bool HasLastRun => lastRun.Count > 0;

    /// <summary>Raised when a run starts or ends, or a discovery starts or ends.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Raised when a run starts.</summary>
    public event EventHandler<TestRunSession>? RunStarted;

    /// <summary>Finds the tests of the open folder again with every provider.</summary>
    public async Task DiscoverAllAsync()
    {
        CancellationTokenSource cancel;
        lock (gate)
        {
            discovery.Cancel();
            discovery.Dispose();
            discovery = cancel = new CancellationTokenSource();
        }

        if (workspace.RootPath is not { } root)
        {
            Tree.ClearAll();
            return;
        }

        IsDiscovering = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
        using var task = Providers.Count > 0 ? tasks?.Start("Discovering tests", cancel.Cancel) : null;
        try
        {
            await Task.WhenAll(Providers.Select(provider => DiscoverAsync(provider, root, cancel.Token))).ConfigureAwait(false);
        }
        finally
        {
            if (!cancel.IsCancellationRequested)
            {
                IsDiscovering = false;
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Finds the tests of one file again, from <paramref name="snapshot"/> or the file on disk.</summary>
    public async Task DiscoverFileAsync(string filePath, TextSnapshot? snapshot = null, CancellationToken cancellationToken = default)
    {
        if (workspace.RootPath is not { } root)
            return;

        if (snapshot is null)
        {
            try
            {
                snapshot = File.Exists(filePath) ? TextSnapshot.Create(await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false)) : TextSnapshot.Empty;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return;
            }
        }

        var whole = new List<ITestProvider>();
        foreach (var provider in Providers)
        {
            try
            {
                var context = new TestDocumentContext(root, filePath, snapshot);
                var items = await Task.Run(() => provider.DiscoverDocumentAsync(context, cancellationToken), cancellationToken).ConfigureAwait(false);
                if (items is null)
                    whole.Add(provider);
                else
                    Tree.ReplaceFile(provider, filePath, items);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                log($"{provider.Name} could not find the tests of {filePath}: {exception.Message}");
            }
        }

        foreach (var provider in whole)
            await DiscoverAsync(provider, root, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs or debugs tests, stopping the run that goes on first.</summary>
    /// <returns>The run, which goes on until its <see cref="TestRunSession.Completion"/> ends, or null when there was nothing to run.</returns>
    public async Task<TestRunSession?> RunAsync(IReadOnlyList<TestNode> nodes, RunMode mode = RunMode.Run)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        if (workspace.RootPath is not { } root || nodes.Count == 0)
            return null;

        if (CurrentRun is { State: TestRunState.Running } running)
        {
            running.Stop();
            await running.Completion.ConfigureAwait(false);
        }

        IReadOnlyList<TestNode> top;
        lock (Tree.Gate)
            top = [.. nodes.Distinct().Where(n => !nodes.Any(other => other != n && IsUnder(n, other)))];
        if (mode == RunMode.Debug)
            top = [.. top.Where(n => n.Provider.CanDebug)];
        if (top.Count == 0)
            return null;

        lastRun = [.. top.Select(n => (n.Provider, n.Id))];
        lastMode = mode;
        var run = new TestRunSession(Tree, top, mode, (plan, session, token) => DebugAsync(plan, session, token));
        CurrentRun?.Dispose();
        CurrentRun = run;
        RunStarted?.Invoke(this, run);
        StateChanged?.Invoke(this, EventArgs.Empty);
        _ = ExecuteAsync(run, top, root);
        return run;
    }

    /// <summary>Runs or debugs every test.</summary>
    public Task<TestRunSession?> RunAllAsync(RunMode mode = RunMode.Run) => RunAsync(Tree.AllRoots, mode);

    /// <summary>Runs the tests that failed or errored in their last run again.</summary>
    public Task<TestRunSession?> RerunFailedAsync()
    {
        IReadOnlyList<TestNode> failed;
        lock (Tree.Gate)
            failed = [.. Tree.AllNodes.Where(n => n.IsTest && n.State is TestState.Failed or TestState.Errored)];
        return RunAsync(failed);
    }

    /// <summary>Runs the last run's tests again, in the same mode.</summary>
    public Task<TestRunSession?> RerunLastAsync()
    {
        var nodes = lastRun.Select(r => Tree.Find(r.Provider, r.Id)).OfType<TestNode>().ToList();
        return RunAsync(nodes, lastMode);
    }

    /// <summary>Stops the run that goes on.</summary>
    public void Stop() => CurrentRun?.Stop();

    /// <summary>Gets the innermost test or group declared around a line of a file, counted from 0, or null.</summary>
    public TestNode? FindAt(string filePath, int line)
    {
        lock (Tree.Gate)
        {
            return Tree.InFile(filePath)
                .Where(n => n.Item.Start is { } start && start.Line <= line && (n.Item.End?.Line ?? start.Line) >= line)
                .OrderByDescending(n => n.Item.Start!.Value.Line)
                .ThenBy(n => Depth(n) * -1)
                .FirstOrDefault();
        }
    }

    public void Dispose()
    {
        foreach (var subscription in subscriptions)
            subscription.Dispose();
        lock (gate)
        {
            discovery.Cancel();
            discovery.Dispose();
        }
    }

    private static int Depth(TestNode node)
    {
        var depth = 0;
        for (var at = node.Parent; at is not null; at = at.Parent)
            depth++;
        return depth;
    }

    private static bool IsUnder(TestNode node, TestNode ancestor)
    {
        for (var at = node.Parent; at is not null; at = at.Parent)
        {
            if (at == ancestor)
                return true;
        }

        return false;
    }

    private async Task DiscoverAsync(ITestProvider provider, string root, CancellationToken token)
    {
        try
        {
            var items = await Task.Run(() => provider.DiscoverAsync(new TestDiscoveryContext(root), token), token).ConfigureAwait(false);
            if (!token.IsCancellationRequested)
                Tree.ReplaceAll(provider, items);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            log($"{provider.Name} could not find the tests: {exception.Message}");
        }
    }

    private async Task ExecuteAsync(TestRunSession run, IReadOnlyList<TestNode> nodes, string root)
    {
        var count = 0;
        lock (Tree.Gate)
            count = nodes.Sum(n => n.Tests().Count());
        using var task = tasks?.Start(run.Mode == RunMode.Debug ? "Debugging tests" : $"Running {Count(count, "test")}", run.Stop);
        try
        {
            foreach (var group in nodes.GroupBy(n => n.Provider))
            {
                if (run.Token.IsCancellationRequested)
                    break;

                IReadOnlyList<TestItem> items;
                lock (Tree.Gate)
                    items = [.. group.Select(n => n.ToItem())];
                var request = new TestRunRequest(items, run.Mode, root);
                try
                {
                    await Task.Run(() => group.Key.RunAsync(request, run.For(group.Key), run.Token), run.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (run.Token.IsCancellationRequested)
                {
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    run.Output.AppendLine($"{group.Key.Name} could not run the tests: {exception.Message}", ConsoleSource.Error);
                    log($"{group.Key.Name} could not run the tests: {exception.Message}");
                }
            }
        }
        finally
        {
            run.Finish();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task<int?> DebugAsync(LaunchPlan plan, TestRunSession run, CancellationToken token)
    {
        if (debugger() is not { } debug)
        {
            run.Output.AppendLine("No installed plugin provides a debugger, so the tests cannot be debugged.", ConsoleSource.Error);
            return null;
        }

        var session = await debug.StartAsync("Tests", plan, run.Output, null, token).ConfigureAwait(false);
        if (session is null)
            return null;

        session.ProgramOutput += (_, e) => run.Output.Append(e.Output, e.Category == "stderr" ? ConsoleSource.Error : ConsoleSource.Output);
        await using var stop = token.Register(() => _ = session.StopAsync()).ConfigureAwait(false);
        return await session.Completion.ConfigureAwait(false);
    }

    private void Follow(IDocument document)
    {
        document.Buffer.Changed += (_, _) => Schedule(document.FilePath, null, TypingDelay, document);
        if (document.FilePath is { } path && Tree.InFile(path).Count == 0)
            Schedule(path, document.Buffer.Current, FileDelay);
    }

    private void Schedule(string? filePath, TextSnapshot? snapshot, TimeSpan delay, IDocument? document = null)
    {
        if (filePath is null || workspace.RootPath is null || Providers.Count == 0)
            return;

        CancellationTokenSource cancel;
        lock (gate)
        {
            if (pendingFiles.TryGetValue(filePath, out var pending))
                pending.Cancel();
            pendingFiles[filePath] = cancel = new CancellationTokenSource();
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cancel.Token).ConfigureAwait(false);
                await DiscoverFileAsync(filePath, document?.Buffer.Current ?? snapshot, cancel.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                lock (gate)
                {
                    if (pendingFiles.TryGetValue(filePath, out var current) && current == cancel)
                        pendingFiles.Remove(filePath);
                }

                cancel.Dispose();
            }
        });
    }

    private async Task OnSavedAsync(string path, TextSnapshot snapshot)
    {
        if (Providers.Count == 0)
            return;

        await DiscoverFileAsync(path, snapshot).ConfigureAwait(false);
        if (!autoRunOnSave() || IsRunning)
            return;

        IReadOnlyList<TestNode> tests;
        lock (Tree.Gate)
            tests = [.. Tree.InFile(path).Where(n => n.Parent is null || !PathKey.Equals(n.Parent.Item.FilePath, path))];
        if (tests.Count > 0)
            await RunAsync(tests).ConfigureAwait(false);
        else if (HasLastRun)
            await RerunLastAsync().ConfigureAwait(false);
    }

    private void ScheduleFailures()
    {
        if (Interlocked.Exchange(ref failuresPending, 1) == 0)
            _ = Task.Delay(100).ContinueWith(_ => ReportFailures(), TaskScheduler.Default);
    }

    // Puts each failed test's message at the line it failed on, in the Problems panel and the editor.
    private void ReportFailures()
    {
        Interlocked.Exchange(ref failuresPending, 0);
        var root = workspace.RootPath;
        var found = new Dictionary<string, List<Diagnostic>>(PathKey.Comparer);
        lock (Tree.Gate)
        {
            foreach (var node in Tree.AllNodes)
            {
                if (node.Result is not { State: TestState.Failed or TestState.Errored, Failure: { } failure } || TestFailures.Location(failure, root) is not { } location)
                    continue;

                if (!found.TryGetValue(location.FilePath, out var list))
                    found[location.FilePath] = list = [];
                list.Add(new Diagnostic(location.FilePath, location.Position, null, DiagnosticSeverity.Error, $"{node.Item.Label}: {TestFailures.Summary(failure)}",
                    DiagnosticSource));
            }
        }

        lock (gate)
        {
            foreach (var gone in reportedFiles.Where(f => !found.ContainsKey(f)).ToList())
            {
                diagnostics.Set(DiagnosticSource, gone, []);
                reportedFiles.Remove(gone);
            }

            foreach (var (file, list) in found)
            {
                diagnostics.Set(DiagnosticSource, file, list);
                reportedFiles.Add(file);
            }
        }
    }

    private List<ITestProvider> LoadProviders()
    {
        var loaded = new List<ITestProvider>();
        foreach (var export in providerExports)
        {
            try
            {
                var provider = export.Value;
                provider.TestsChanged += (_, e) =>
                {
                    if (e.FilePath is { } path)
                        Schedule(path, null, FileDelay);
                    else if (workspace.RootPath is { } root)
                        _ = DiscoverAsync(provider, root, CancellationToken.None);
                };
                loaded.Add(provider);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                log($"A test provider could not be loaded: {exception.Message}");
            }
        }

        return loaded;
    }

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
