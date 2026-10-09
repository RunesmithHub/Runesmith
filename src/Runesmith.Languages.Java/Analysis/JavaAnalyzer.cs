using Runesmith.Languages.Java.Jdk;
using Runesmith.Languages.Java.Semantics;
using Runesmith.Languages.Java.Syntax;
using Runesmith.LanguageServices;
using Runesmith.Text;

namespace Runesmith.Languages.Java.Analysis;

/// <summary>Understands Java, every release from 8 to 25: completion, hover, go to definition, signature help and problems, from Runesmith's
/// own parser, the JDK's API and the project's libraries, without a JVM.</summary>
/// <remarks>The folder's Maven or Gradle projects, or the folder itself, load in the background; until then, and for files outside every
/// project, documents belong to a project of their own with the newest release the JDK has.</remarks>
public sealed class JavaAnalyzer : ILanguageAnalyzer
{
    /// <summary>The language id of Java.</summary>
    public const string LanguageId = "java";

    private readonly JavaDocuments documents = new();
    private readonly JavaCompletion completion = new();
    private AnalyzerContext? context;
    private JavaWorkspace? workspace;
    private CancellationTokenSource loading = new();

    public IReadOnlyList<string> LanguageIds { get; } = [LanguageId];

    public IReadOnlyList<char> CompletionTriggerCharacters { get; } = ['.'];

    public IReadOnlyList<char> SignatureHelpTriggerCharacters { get; } = ['(', ','];

    public IReadOnlyList<char> SignatureHelpRetriggerCharacters { get; } = [','];

    /// <summary>Gets a task that completes when the projects of the open folder are loaded and their sources indexed.</summary>
    /// <summary>Gets where to look for JDKs and which one the user prefers, asked each time a JDK is needed; null searches this system.</summary>
    public Func<JdkSearchOptions>? JdkSearch { get; init; }

    internal Task Loaded { get; private set; } = Task.CompletedTask;

    /// <summary>Gets how many completion lists were computed, as opposed to filtered from a kept one.</summary>
    internal int ComputedCompletionLists => completion.Computed;

    internal JavaWorkspace Workspace => workspace ?? throw new InvalidOperationException("The analyzer has not been initialized.");

    private AnalyzerContext Context => context ?? throw new InvalidOperationException("The analyzer has not been initialized.");

    public void Initialize(AnalyzerContext context)
    {
        this.context = context;
        workspace = new JavaWorkspace(context.CacheDirectory, context.Log, jdkSearch: JdkSearch);
        Loaded = context.RunInBackground("Load the JDK", _ =>
        {
            completion.WarmUp(Workspace.Fallback);
            context.InvalidateDiagnostics();
            return Task.CompletedTask;
        });
    }

    public Task OpenWorkspaceAsync(string? rootPath, CancellationToken cancellationToken)
    {
        var context = Context;
        loading.Cancel();
        loading.Dispose();
        loading = new CancellationTokenSource();
        if (rootPath is null)
            return Task.CompletedTask;

        var token = loading.Token;
        var previous = Loaded;
        Loaded = context.RunInBackground("Load Java projects", async background =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, background);
            await previous.ConfigureAwait(false);
            await Workspace.LoadAsync(rootPath, context.YieldAsync, linked.Token).ConfigureAwait(false);
            if (linked.IsCancellationRequested)
                return;

            foreach (var project in Workspace.Projects)
                completion.WarmUp(project);
            context.InvalidateDiagnostics();
        });
        return Task.CompletedTask;
    }

    public void Open(SourceDocument document) => documents.Open(document);

    public void Change(SourceDocument document, IReadOnlyList<TextChange> changes)
    {
        completion.OnChanged(document.Path, document.Version, changes);
        documents.Change(document, changes);
    }

    public void Close(string path)
    {
        completion.Forget(path);
        if (documents.Close(path) is { } project)
            _ = Context.RunInBackground("Reload a closed Java file", _ =>
            {
                if (project.Owns(path))
                    JavaWorkspace.Reload(project, path);
                else
                    project.RemoveFile(path);
                return Task.CompletedTask;
            });
    }

    public ValueTask CompleteAsync(CompletionQuery query, CompletionSink sink, CancellationToken cancellationToken)
    {
        if (ModelFor(query.Document) is { } model)
            completion.Complete(model, query, sink, cancellationToken);
        return ValueTask.CompletedTask;
    }

    public ValueTask<CompletionDetails?> ResolveAsync(CompletionEntry entry, CancellationToken cancellationToken) =>
        ValueTask.FromResult(JavaCompletion.Resolve(entry));

    public ValueTask<HoverResult?> HoverAsync(DocumentPosition position, CancellationToken cancellationToken)
    {
        if (ModelFor(position.Document) is not { } model || JavaSymbols.Find(model, position.Offset) is not var (symbol, span))
            return ValueTask.FromResult<HoverResult?>(null);

        var markdown = "```java\n" + JavaSymbols.Describe(model, symbol) + "\n```";
        if (JavaSymbols.Documentation(model, symbol) is { Length: > 0 } documentation)
            markdown += "\n\n" + documentation;
        return ValueTask.FromResult<HoverResult?>(new HoverResult(markdown, new TextSpan(span.Start, span.Length)));
    }

    public ValueTask<IReadOnlyList<SourceLocation>> DefinitionAsync(DocumentPosition position, CancellationToken cancellationToken)
    {
        if (ModelFor(position.Document) is not { } model || JavaSymbols.Find(model, position.Offset) is not var (symbol, _)
            || JavaSymbols.Location(model, symbol, position.Document.Path, position.Document.Snapshot) is not { } location)
        {
            return ValueTask.FromResult<IReadOnlyList<SourceLocation>>([]);
        }

        return ValueTask.FromResult<IReadOnlyList<SourceLocation>>([location]);
    }

    public ValueTask<SignatureHelpResult?> SignatureHelpAsync(DocumentPosition position, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ModelFor(position.Document) is { } model ? JavaSignatureHelp.Get(model, position.Offset) : null);

    public async ValueTask<IReadOnlyList<Problem>> DiagnosticsAsync(SourceDocument document, CancellationToken cancellationToken)
    {
        await Context.YieldAsync(cancellationToken).ConfigureAwait(false);
        if (ModelFor(document) is not { } model)
            return [];

        var problems = new List<Problem>();
        foreach (var diagnostic in model.Tree.Diagnostics)
            problems.Add(new Problem(new TextSpan(diagnostic.Span.Start, diagnostic.Span.Length), ProblemSeverity.Error, diagnostic.Message, diagnostic.Code));

        await Context.YieldAsync(cancellationToken).ConfigureAwait(false);
        foreach (var problem in SemanticDiagnostics.Find(model, cancellationToken))
            problems.Add(new Problem(new TextSpan(problem.Span.Start, problem.Span.Length), ProblemSeverity.Error, problem.Message, problem.Code));
        return problems;
    }

    public ValueTask DisposeAsync()
    {
        loading.Cancel();
        loading.Dispose();
        return ValueTask.CompletedTask;
    }

    private SemanticModel? ModelFor(SourceDocument document) => documents.ModelFor(document, Workspace.ProjectFor(document.Path));
}
