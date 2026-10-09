using Runesmith.Text;

namespace Runesmith.LanguageServices;

/// <summary>Understands one or more languages: answers completion, hover, definition and signature help requests, and finds problems.</summary>
/// <remarks>
/// The host calls <see cref="Initialize"/> once, then opens the workspace and documents, and passes every change with the edits that made it,
/// so an analyzer can update incrementally. Requests come from any thread, possibly at the same time, each for one document version; an
/// analyzer must not block them on background work. Plugins export analyzers with <c>[Export(typeof(ILanguageAnalyzer))]</c>.
/// </remarks>
public interface ILanguageAnalyzer : IAsyncDisposable
{
    /// <summary>Gets the languages the analyzer understands, such as <c>csharp</c>.</summary>
    IReadOnlyList<string> LanguageIds { get; }

    /// <summary>Gets the characters that start completion, such as <c>.</c>.</summary>
    IReadOnlyList<char> CompletionTriggerCharacters { get; }

    /// <summary>Gets the characters that open signature help, such as <c>(</c>.</summary>
    IReadOnlyList<char> SignatureHelpTriggerCharacters { get; }

    /// <summary>Gets the characters that update open signature help, such as <c>,</c>.</summary>
    IReadOnlyList<char> SignatureHelpRetriggerCharacters { get; }

    /// <summary>Gives the analyzer the host's services; called once, before anything else.</summary>
    void Initialize(AnalyzerContext context);

    /// <summary>Opens the workspace in a folder, or no folder when null; called again when the folder changes. Loading may continue in the
    /// background after the task completes, through <see cref="AnalyzerContext.RunInBackground"/>.</summary>
    Task OpenWorkspaceAsync(string? rootPath, CancellationToken cancellationToken);

    /// <summary>A document was opened in the editor.</summary>
    void Open(SourceDocument document);

    /// <summary>A document changed; <paramref name="changes"/> turn the previous version into this one.</summary>
    void Change(SourceDocument document, IReadOnlyList<TextChange> changes);

    void Close(string path);

    /// <summary>Adds the suggestions at a position to the sink, cheapest sources first.</summary>
    ValueTask CompleteAsync(CompletionQuery query, CompletionSink sink, CancellationToken cancellationToken);

    /// <summary>Computes the details of a suggestion this analyzer made: its documentation and the edits accepting it makes.</summary>
    ValueTask<CompletionDetails?> ResolveAsync(CompletionEntry entry, CancellationToken cancellationToken);

    ValueTask<HoverResult?> HoverAsync(DocumentPosition position, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<SourceLocation>> DefinitionAsync(DocumentPosition position, CancellationToken cancellationToken);

    ValueTask<SignatureHelpResult?> SignatureHelpAsync(DocumentPosition position, CancellationToken cancellationToken);

    /// <summary>Finds the problems in a document version; runs in the background lane, which may pause it for interactive requests.</summary>
    ValueTask<IReadOnlyList<Problem>> DiagnosticsAsync(SourceDocument document, CancellationToken cancellationToken);
}

/// <summary>What the host gives an analyzer.</summary>
public abstract class AnalyzerContext
{
    /// <summary>Gets a folder the analyzer may keep caches in between runs.</summary>
    public abstract string CacheDirectory { get; }

    /// <summary>Writes a line to the analyzer's log, shown in the Output panel.</summary>
    public abstract void Log(string message);

    /// <summary>Gets the newest version of an open document, or null when it is not open.</summary>
    public abstract SourceDocument? GetOpenDocument(string path);

    /// <summary>Gets the open documents.</summary>
    public abstract IReadOnlyList<SourceDocument> OpenDocuments { get; }

    /// <summary>Runs work in the background lane, after interactive and navigation requests; the work should call
    /// <see cref="YieldAsync"/> between units, so it pauses while the user waits for something else.</summary>
    public abstract Task RunInBackground(string name, Func<CancellationToken, Task> work);

    /// <summary>Waits while interactive requests run; background work calls it between units of work.</summary>
    public abstract ValueTask YieldAsync(CancellationToken cancellationToken);

    /// <summary>Asks the host to find the problems of the open documents again, such as after a project finished loading.</summary>
    public abstract void InvalidateDiagnostics();
}
