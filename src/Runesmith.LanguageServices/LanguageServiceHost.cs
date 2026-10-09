using System.Collections.Concurrent;
using System.Diagnostics;
using Runesmith.Text;

namespace Runesmith.LanguageServices;

/// <summary>Runs the language analyzers: keeps the open documents and their versions, schedules every request in its lane, finds problems
/// in the background after edits, and measures it all.</summary>
/// <remarks>Document calls (<see cref="Open"/>, <see cref="Change"/>, <see cref="Close"/>) come from one thread, the editor's, in order.
/// Requests may come from any thread.</remarks>
public sealed partial class LanguageServiceHost : IAsyncDisposable
{
    /// <summary>How long after the last change the problems of a document are looked for.</summary>
    public static readonly TimeSpan DiagnosticsDelay = TimeSpan.FromMilliseconds(150);

    private readonly string cacheDirectory;
    private readonly Action<string, string> log;
    private readonly List<ILanguageAnalyzer> analyzers = [];
    private readonly ConcurrentDictionary<string, DocumentHistory> documents = new(PathComparer);
    private readonly Dictionary<string, CancellationTokenSource> pendingDiagnostics = new(PathComparer);
    private readonly RequestScheduler scheduler = new();
    private readonly CancellationTokenSource shutdown = new();

    /// <param name="cacheDirectory">Where analyzers keep caches between runs.</param>
    /// <param name="log">Receives log lines: the analyzer's name and the line.</param>
    public LanguageServiceHost(string cacheDirectory, Action<string, string> log)
    {
        this.cacheDirectory = cacheDirectory;
        this.log = log;
    }

    /// <summary>Raised, on a background thread, when the problems of a document version are known.</summary>
    public event Action<SourceDocument, IReadOnlyList<Problem>>? ProblemsFound;

    /// <summary>Raised, on a background thread, when an analyzer fails a request; the request returns nothing.</summary>
    public event Action<ILanguageAnalyzer, Exception>? AnalyzerFailed;

    private static StringComparer PathComparer => OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    /// <summary>Gets or sets what tracks named background work, such as loading projects: called, on the worker thread, when the work starts
    /// running, and the result is disposed when it ends. It must return quickly, as small work runs this way after key presses too.</summary>
    public Func<string, IDisposable?>? TrackBackgroundWork { get; set; }

    /// <summary>Gets the analyzers.</summary>
    public IReadOnlyList<ILanguageAnalyzer> Analyzers => analyzers;

    /// <summary>Adds an analyzer and initializes it.</summary>
    public void Add(ILanguageAnalyzer analyzer)
    {
        ArgumentNullException.ThrowIfNull(analyzer);
        analyzer.Initialize(new Context(this, analyzer));
        analyzers.Add(analyzer);
    }

    /// <summary>Gets the analyzer of a language, or null.</summary>
    public ILanguageAnalyzer? AnalyzerFor(string languageId) =>
        analyzers.FirstOrDefault(a => a.LanguageIds.Contains(languageId, StringComparer.OrdinalIgnoreCase));

    /// <summary>Opens a folder in every analyzer, or no folder when null.</summary>
    public Task OpenWorkspaceAsync(string? rootPath, CancellationToken cancellationToken = default) =>
        Task.WhenAll(analyzers.Select(async analyzer =>
        {
            try
            {
                await analyzer.OpenWorkspaceAsync(rootPath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Fail(analyzer, exception);
            }
        }));

    /// <summary>A document was opened; it is version 1.</summary>
    public void Open(string path, string languageId, TextSnapshot snapshot)
    {
        var document = new SourceDocument(path, languageId, 1, snapshot);
        documents[path] = new DocumentHistory(document);
        if (AnalyzerFor(languageId) is { } analyzer)
        {
            Guard(analyzer, () => analyzer.Open(document));
            ScheduleDiagnostics(path, TimeSpan.Zero);
        }
    }

    /// <summary>A document changed; <paramref name="changes"/> turn its previous snapshot into <paramref name="snapshot"/>.</summary>
    public void Change(string path, TextSnapshot snapshot, IReadOnlyList<TextChange> changes)
    {
        if (!documents.TryGetValue(path, out var history))
            return;

        var document = history.Add(snapshot, changes);
        if (AnalyzerFor(document.LanguageId) is { } analyzer)
        {
            Guard(analyzer, () => analyzer.Change(document, changes));
            ScheduleDiagnostics(path, DiagnosticsDelay);
        }
    }

    /// <summary>A document's language changed: it closes in the old language's analyzer and opens in the new one's.</summary>
    public void ChangeLanguage(string path, string languageId)
    {
        if (!documents.TryGetValue(path, out var history) || string.Equals(history.Latest.LanguageId, languageId, StringComparison.OrdinalIgnoreCase))
            return;

        var snapshot = history.Latest.Snapshot;
        Close(path);
        Open(path, languageId, snapshot);
    }

    public void Close(string path)
    {
        if (!documents.TryRemove(path, out var history))
            return;

        lock (pendingDiagnostics)
        {
            if (pendingDiagnostics.Remove(path, out var pending))
                pending.Cancel();
        }

        if (AnalyzerFor(history.Latest.LanguageId) is { } analyzer)
            Guard(analyzer, () => analyzer.Close(path));
    }

    /// <summary>Gets the newest version of an open document, or null.</summary>
    public SourceDocument? GetDocument(string path) => documents.TryGetValue(path, out var history) ? history.Latest : null;

    /// <summary>Moves a span of one version of a document to its newest version; returns null when that is not possible.</summary>
    public TextSpan? MapToLatest(string path, int version, TextSpan span) =>
        documents.TryGetValue(path, out var history) ? history.MapSpan(version, history.Latest.Version, span) : null;

    public async Task<CompletionResult> CompleteAsync(string path, TextSnapshot snapshot, int offset, CompletionTriggerKind trigger, char? character,
        CancellationToken cancellationToken)
    {
        if (Resolve(path, snapshot) is not { } resolved)
            return CompletionResult.Empty(0, offset);

        var (document, analyzer) = resolved;
        var language = document.LanguageId;
        return await RunAsync(analyzer, document.LanguageId, "completion", RequestLane.Interactive, CompletionResult.Empty(document.Version, offset), async () =>
        {
            var sink = new CompletionSink(document.Snapshot, offset);
            await analyzer.CompleteAsync(new CompletionQuery(document, offset, trigger, character), sink, cancellationToken).ConfigureAwait(false);
            var rankStart = Stopwatch.GetTimestamp();
            var result = sink.ToResult(document.Version);
            var tag = new KeyValuePair<string, object?>("language", language);
            LanguageServiceMetrics.Rank.Record(LanguageServiceMetrics.Since(rankStart), tag);
            LanguageServiceMetrics.Candidates.Record(sink.Added, tag);
            LanguageServiceMetrics.Items.Record(result.Items.Count, tag);
            foreach (var item in result.Items)
                item.Analyzer = analyzer;
            return result;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CompletionDetails?> ResolveAsync(CompletionEntry entry, CancellationToken cancellationToken)
    {
        if (entry.Analyzer is not { } analyzer)
            return null;

        return await RunAsync(analyzer, analyzer.LanguageIds[0], "resolve", RequestLane.Interactive, null,
            () => analyzer.ResolveAsync(entry, cancellationToken).AsTask(), cancellationToken).ConfigureAwait(false);
    }

    public Task<HoverResult?> HoverAsync(string path, TextSnapshot snapshot, int offset, CancellationToken cancellationToken) =>
        Request<HoverResult?>(path, snapshot, "hover", RequestLane.Navigation, null,
            (analyzer, document) => analyzer.HoverAsync(new DocumentPosition(document, offset), cancellationToken).AsTask(), cancellationToken);

    public Task<IReadOnlyList<SourceLocation>> DefinitionAsync(string path, TextSnapshot snapshot, int offset, CancellationToken cancellationToken) =>
        Request<IReadOnlyList<SourceLocation>>(path, snapshot, "definition", RequestLane.Navigation, [],
            (analyzer, document) => analyzer.DefinitionAsync(new DocumentPosition(document, offset), cancellationToken).AsTask(), cancellationToken);

    public Task<SignatureHelpResult?> SignatureHelpAsync(string path, TextSnapshot snapshot, int offset, CancellationToken cancellationToken) =>
        Request<SignatureHelpResult?>(path, snapshot, "signatureHelp", RequestLane.Interactive, null,
            (analyzer, document) => analyzer.SignatureHelpAsync(new DocumentPosition(document, offset), cancellationToken).AsTask(), cancellationToken);

    /// <summary>Looks for the problems of every open document again.</summary>
    public void InvalidateDiagnostics()
    {
        foreach (var path in documents.Keys)
            ScheduleDiagnostics(path, TimeSpan.Zero);
    }

    public async ValueTask DisposeAsync()
    {
        await shutdown.CancelAsync().ConfigureAwait(false);
        lock (pendingDiagnostics)
        {
            foreach (var pending in pendingDiagnostics.Values)
                pending.Cancel();
        }
        foreach (var analyzer in analyzers)
        {
            try
            {
                await analyzer.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Fail(analyzer, exception);
            }
        }

        shutdown.Dispose();
        scheduler.Dispose();
    }

    private (SourceDocument Document, ILanguageAnalyzer Analyzer)? Resolve(string path, TextSnapshot snapshot)
    {
        if (!documents.TryGetValue(path, out var history))
            return null;

        var document = history.Find(snapshot) ?? history.Latest;
        return AnalyzerFor(document.LanguageId) is { } analyzer ? (document, analyzer) : null;
    }

    private async Task<T> Request<T>(string path, TextSnapshot snapshot, string kind, RequestLane lane, T empty,
        Func<ILanguageAnalyzer, SourceDocument, Task<T>> work, CancellationToken cancellationToken)
    {
        if (Resolve(path, snapshot) is not { } resolved)
            return empty;

        var (document, analyzer) = resolved;
        return await RunAsync(analyzer, document.LanguageId, kind, lane, empty, () => work(analyzer, document), cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> RunAsync<T>(ILanguageAnalyzer analyzer, string language, string kind, RequestLane lane, T empty, Func<Task<T>> work,
        CancellationToken cancellationToken)
    {
        var tags = LanguageServiceMetrics.Tags(language, kind);
        var received = Stopwatch.GetTimestamp();
        try
        {
            return await scheduler.RunAsync(lane, async () =>
            {
                LanguageServiceMetrics.Queue.Record(LanguageServiceMetrics.Since(received), tags);
                return await work().ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not AnalyzerRefusalException)
        {
            Fail(analyzer, exception);
            return empty;
        }
        finally
        {
            LanguageServiceMetrics.Request.Record(LanguageServiceMetrics.Since(received), tags);
        }
    }

    private void ScheduleDiagnostics(string path, TimeSpan delay)
    {
        // The run that finishes removes and disposes its own source, so replacing and cancelling it happen under the same lock.
        var source = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
        lock (pendingDiagnostics)
        {
            if (pendingDiagnostics.TryGetValue(path, out var previous))
                previous.Cancel();
            pendingDiagnostics[path] = source;
        }

        var token = source.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, token).ConfigureAwait(false);
                if (GetDocument(path) is not { } document || AnalyzerFor(document.LanguageId) is not { } analyzer)
                    return;

                // Finding problems can take one long compiler call; typing preempts it, and it runs again once typing pauses. An analyzer that
                // fails is not asked again until the document changes.
                IReadOnlyList<Problem>? problems = null;
                while (!token.IsCancellationRequested)
                {
                    using var run = CancellationTokenSource.CreateLinkedTokenSource(token, scheduler.PreemptionToken);
                    try
                    {
                        problems = await RunAsync<IReadOnlyList<Problem>?>(analyzer, document.LanguageId, "diagnostics", RequestLane.Background, null,
                            async () => await analyzer.DiagnosticsAsync(document, run.Token).ConfigureAwait(false), run.Token).ConfigureAwait(false);
                        break;
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        await Task.Delay(DiagnosticsDelay, token).ConfigureAwait(false);
                    }
                }

                if (problems is not null && !token.IsCancellationRequested && GetDocument(path)?.Version == document.Version)
                    ProblemsFound?.Invoke(document, problems);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                lock (pendingDiagnostics)
                {
                    if (pendingDiagnostics.TryGetValue(path, out var current) && ReferenceEquals(current, source))
                        pendingDiagnostics.Remove(path);
                    source.Dispose();
                }
            }
        }, CancellationToken.None);
    }

    private void Guard(ILanguageAnalyzer analyzer, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            Fail(analyzer, exception);
        }
    }

    private void Fail(ILanguageAnalyzer analyzer, Exception exception)
    {
        log(AnalyzerName(analyzer), $"Failed: {exception}");
        AnalyzerFailed?.Invoke(analyzer, exception);
    }

    private static string AnalyzerName(ILanguageAnalyzer analyzer) => analyzer.GetType().Name;

    private sealed class Context(LanguageServiceHost host, ILanguageAnalyzer analyzer) : AnalyzerContext
    {
        public override string CacheDirectory => Path.Combine(host.cacheDirectory, AnalyzerName(analyzer));

        public override IReadOnlyList<SourceDocument> OpenDocuments =>
            [.. host.documents.Values.Select(h => h.Latest).Where(d => analyzer.LanguageIds.Contains(d.LanguageId, StringComparer.OrdinalIgnoreCase))];

        public override void Log(string message) => host.log(AnalyzerName(analyzer), message);

        public override SourceDocument? GetOpenDocument(string path) => host.GetDocument(path);

        public override Task RunInBackground(string name, Func<CancellationToken, Task> work) =>
            host.RunAsync<bool>(analyzer, analyzer.LanguageIds[0], name, RequestLane.Background, false, async () =>
            {
                using var tracked = host.TrackBackgroundWork?.Invoke(name);
                await work(host.shutdown.Token).ConfigureAwait(false);
                return true;
            }, host.shutdown.Token);

        public override ValueTask YieldAsync(CancellationToken cancellationToken) => host.scheduler.YieldAsync(cancellationToken);

        public override void InvalidateDiagnostics() => host.InvalidateDiagnostics();
    }
}
