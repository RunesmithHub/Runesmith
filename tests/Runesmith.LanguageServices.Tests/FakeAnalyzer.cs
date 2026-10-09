using Runesmith.Text;

namespace Runesmith.LanguageServices.Tests;

/// <summary>An analyzer that suggests a fixed list of words and reports a problem for every <c>bad</c> in the text.</summary>
internal sealed class FakeAnalyzer : ILanguageAnalyzer
{
    private readonly string[] words;

    public FakeAnalyzer(params string[] words) => this.words = words;

    public List<string> Calls { get; } = [];

    public int DiagnosticsRuns;

    public bool ThrowOnComplete { get; set; }

    public bool ThrowOnDiagnostics { get; set; }

    public IReadOnlyList<string> LanguageIds => ["fake"];

    public IReadOnlyList<char> CompletionTriggerCharacters => ['.'];

    public IReadOnlyList<char> SignatureHelpTriggerCharacters => ['('];

    public IReadOnlyList<char> SignatureHelpRetriggerCharacters => [','];

    public AnalyzerContext? Context { get; private set; }

    public void Initialize(AnalyzerContext context) => Context = context;

    public Task OpenWorkspaceAsync(string? rootPath, CancellationToken cancellationToken)
    {
        Calls.Add($"workspace {rootPath}");
        return Task.CompletedTask;
    }

    public void Open(SourceDocument document) => Calls.Add($"open {document.Path} {document.Version}");

    public void Change(SourceDocument document, IReadOnlyList<TextChange> changes) => Calls.Add($"change {document.Path} {document.Version}");

    public void Close(string path) => Calls.Add($"close {path}");

    public ValueTask CompleteAsync(CompletionQuery query, CompletionSink sink, CancellationToken cancellationToken)
    {
        if (ThrowOnComplete)
            throw new InvalidOperationException("broken");

        foreach (var word in words)
            sink.Add(new CompletionCandidate(word, CompletionKind.Text));
        return ValueTask.CompletedTask;
    }

    public ValueTask<CompletionDetails?> ResolveAsync(CompletionEntry entry, CancellationToken cancellationToken) =>
        ValueTask.FromResult<CompletionDetails?>(new CompletionDetails($"detail of {entry.Label}", null, []));

    public ValueTask<HoverResult?> HoverAsync(DocumentPosition position, CancellationToken cancellationToken) =>
        ValueTask.FromResult<HoverResult?>(new HoverResult($"hover at {position.Offset} in version {position.Document.Version}"));

    public ValueTask<IReadOnlyList<SourceLocation>> DefinitionAsync(DocumentPosition position, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<SourceLocation>>([new SourceLocation(position.Document.Path, new TextPosition(0, 0))]);

    public ValueTask<SignatureHelpResult?> SignatureHelpAsync(DocumentPosition position, CancellationToken cancellationToken) =>
        ValueTask.FromResult<SignatureHelpResult?>(null);

    public ValueTask<IReadOnlyList<Problem>> DiagnosticsAsync(SourceDocument document, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref DiagnosticsRuns);
        if (ThrowOnDiagnostics)
            throw new InvalidOperationException("broken");
        var text = document.Snapshot.GetText();
        var problems = new List<Problem>();
        for (var at = text.IndexOf("bad", StringComparison.Ordinal); at >= 0; at = text.IndexOf("bad", at + 1, StringComparison.Ordinal))
            problems.Add(new Problem(new TextSpan(at, 3), ProblemSeverity.Error, "bad"));
        return ValueTask.FromResult<IReadOnlyList<Problem>>(problems);
    }

    public ValueTask DisposeAsync()
    {
        Calls.Add("dispose");
        return ValueTask.CompletedTask;
    }
}
