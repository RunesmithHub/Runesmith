using System.ComponentModel;
using System.Composition;
using Runesmith.LanguageServices;
using Runesmith.Sdk;
using Runesmith.Sdk.Build;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.Tasks;
using Runesmith.Sdk.Workspace;
using Runesmith.Text;

namespace Runesmith.Languages.Analyzers;

/// <summary>Runs the language analyzers plugins export, in Runesmith's own process: gives them the editor's documents as they change,
/// and their problems to the Problems panel.</summary>
/// <remarks>Documents reach the analyzers as the editor's own immutable snapshots and the changes between them, so nothing is copied or
/// serialized on the way.</remarks>
[Export]
[Shared]
public sealed class AnalyzerBridge : IDisposable
{
    private const string UntitledPrefix = "untitled:";

    // Work with these prefixes runs after key presses; the indicator only shows what loads or builds.
    private static readonly string[] PerKeyWork = ["Prefetch", "Speculate", "Bring"];

    /// <summary>How long background work runs before the task indicator shows it, so short work never flickers there.</summary>
    private static readonly TimeSpan ReportAfter = TimeSpan.FromMilliseconds(300);

    private readonly IDocumentService documents;
    private readonly IWorkspace workspace;
    private readonly IDiagnosticService diagnostics;
    private readonly IOutputService output;
    private readonly Dictionary<IDocument, string> paths = [];
    private readonly Dictionary<TextBuffer, IDocument> byBuffer = [];

    [ImportingConstructor]
    public AnalyzerBridge(
        [ImportMany] IEnumerable<ILanguageAnalyzer> analyzers,
        IDocumentService documents,
        IWorkspace workspace,
        IDiagnosticService diagnostics,
        IOutputService output,
        [Import(AllowDefault = true)] IBackgroundTasks? tasks = null)
    {
        this.documents = documents;
        this.workspace = workspace;
        this.diagnostics = diagnostics;
        this.output = output;
        Host = new LanguageServiceHost(Path.Combine(RunesmithPaths.Cache, "analyzers"), Log);
        if (tasks is not null)
            Host.TrackBackgroundWork = name => IsReported(name) ? new DelayedTask(tasks, Describe(name), ReportAfter) : null;
        foreach (var analyzer in analyzers)
            Host.Add(analyzer);

        Host.ProblemsFound += OnProblemsFound;
        documents.Opened += (_, e) => Open(e.Document);
        documents.Closed += (_, e) => Close(e.Document);
        documents.Saved += (_, e) => Rename(e.Document);
        workspace.Changed += (_, _) => _ = Host.OpenWorkspaceAsync(workspace.RootPath);
        foreach (var document in documents.Documents)
            Open(document);
        if (workspace.RootPath is not null)
            _ = Host.OpenWorkspaceAsync(workspace.RootPath);
    }

    /// <summary>Gets the host the analyzers run in.</summary>
    public LanguageServiceHost Host { get; }

    /// <summary>Gets whether background work with this name shows in the task indicator: all but the small work that follows key presses.</summary>
    internal static bool IsReported(string name) => !PerKeyWork.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>Turns the name of background work into what the indicator says, such as "Load C# projects" into "Loading C# projects".</summary>
    internal static string Describe(string name)
    {
        var space = name.IndexOf(' ', StringComparison.Ordinal);
        var verb = space < 0 ? name : name[..space];
        if (verb.Length < 3 || verb.EndsWith("ing", StringComparison.Ordinal))
            return name;

        var stem = verb.EndsWith('e') && !verb.EndsWith("ee", StringComparison.Ordinal) ? verb[..^1] : verb;
        return stem + "ing" + name[verb.Length..];
    }

    /// <summary>Gets the path the analyzers know a document by, or null when no analyzer has it open.</summary>
    public string? PathOf(IDocument document)
    {
        lock (paths)
            return paths.GetValueOrDefault(document);
    }

    /// <summary>Gets the analyzer of a document's language, or null.</summary>
    public ILanguageAnalyzer? AnalyzerOf(IDocument document) => Host.AnalyzerFor(document.LanguageId);

    public void Dispose() => Host.DisposeAsync().AsTask().GetAwaiter().GetResult();

    private void Open(IDocument document)
    {
        var path = document.FilePath ?? UntitledPrefix + document.Name;
        lock (paths)
        {
            paths[document] = path;
            byBuffer[document.Buffer] = document;
        }

        Host.Open(path, document.LanguageId, document.Buffer.Current);
        document.Buffer.Changed += OnBufferChanged;
        document.PropertyChanged += OnDocumentPropertyChanged;
    }

    private void Close(IDocument document)
    {
        document.Buffer.Changed -= OnBufferChanged;
        document.PropertyChanged -= OnDocumentPropertyChanged;
        string? path;
        lock (paths)
        {
            paths.Remove(document, out path);
            byBuffer.Remove(document.Buffer);
        }

        if (path is null)
            return;

        Host.Close(path);
        diagnostics.Set(SourceOf(document.LanguageId), path, []);
    }

    // Save As gives an untitled document a path, or a document a new one.
    private void Rename(IDocument document)
    {
        if (document.FilePath is not { } filePath || PathOf(document) is not { } path || string.Equals(path, filePath, StringComparison.Ordinal))
            return;

        Close(document);
        Open(document);
    }

    private void OnBufferChanged(object? sender, TextChangedEventArgs e)
    {
        string? path = null;
        lock (paths)
        {
            if (sender is TextBuffer buffer && byBuffer.TryGetValue(buffer, out var document))
                path = paths.GetValueOrDefault(document);
        }

        if (path is not null)
            Host.Change(path, e.ChangeSet.After, e.ChangeSet.Changes);
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IDocument.LanguageId) && sender is IDocument document && PathOf(document) is { } path)
            Host.ChangeLanguage(path, document.LanguageId);
    }

    private void OnProblemsFound(SourceDocument document, IReadOnlyList<Problem> problems)
    {
        if (document.Path.StartsWith(UntitledPrefix, StringComparison.Ordinal))
            return;

        var snapshot = document.Snapshot;
        diagnostics.Set(SourceOf(document.LanguageId), document.Path,
        [
            .. problems.Select(problem => new Diagnostic(
                document.Path,
                snapshot.GetPosition(Math.Min(problem.Span.Start, snapshot.Length)),
                snapshot.GetPosition(Math.Min(problem.Span.End, snapshot.Length)),
                problem.Severity switch
                {
                    ProblemSeverity.Error => DiagnosticSeverity.Error,
                    ProblemSeverity.Warning => DiagnosticSeverity.Warning,
                    ProblemSeverity.Information => DiagnosticSeverity.Information,
                    _ => DiagnosticSeverity.Hint,
                },
                problem.Message,
                SourceOf(document.LanguageId))
            {
                Code = problem.Code,
            }),
        ]);
    }

    private void Log(string analyzer, string message) => output.GetChannel("Language analyzers").AppendLine($"[{DateTime.Now:HH:mm:ss}] {analyzer}: {message}");

    private static string SourceOf(string languageId) => languageId;
}

/// <summary>Shows background work in the task indicator once it has run for a while, until it ends.</summary>
internal sealed class DelayedTask : IDisposable
{
    private readonly Lock gate = new();
    private readonly IBackgroundTasks tasks;
    private readonly string title;
    private readonly Timer timer;
    private IBackgroundTask? task;
    private bool ended;

    public DelayedTask(IBackgroundTasks tasks, string title, TimeSpan delay)
    {
        this.tasks = tasks;
        this.title = title;
        timer = new Timer(_ => Start(), null, delay, Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        lock (gate)
        {
            ended = true;
            timer.Dispose();
            task?.Dispose();
            task = null;
        }
    }

    private void Start()
    {
        lock (gate)
        {
            if (!ended)
                task = tasks.Start(title);
        }
    }
}
