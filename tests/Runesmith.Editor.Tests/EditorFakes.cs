using System.ComponentModel;
using System.Text;
using Runesmith.Editor.Input;
using Runesmith.Sdk.Build;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Text;

namespace Runesmith.Editor.Tests;

/// <summary>Editor services whose language features are scripted by the test.</summary>
internal sealed class FakeEditorServices
{
    public FakeLanguageFeatures Language { get; } = new();

    public FakeEditorFeatures Features { get; } = new();

    public FakeNavigationFeatures Navigation { get; } = new();

    public FakeSyntaxFeatures Syntax { get; } = new();

    public FakeWorkspaceEdits Edits { get; } = new();

    public FakeCommands Commands { get; } = new();

    public List<IEditorKeyHook> Hooks { get; } = [];

    public EditorServices Create() => new(
        Language,
        new NoHighlighting(),
        new NoDiagnostics(),
        new NoLanguages(),
        Features,
        Navigation,
        Syntax,
        new EditorKeyHooks(Hooks),
        new Lazy<IWorkspaceEditService>(() => Edits),
        new Lazy<ICommandService>(() => Commands));
}

internal sealed class FileTestDocument(string filePath, string text, string languageId = "fake") : IDocument
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string? FilePath => filePath;

    public string Name => Path.GetFileName(filePath);

    public TextBuffer Buffer { get; } = new(text);

    public UndoHistory History { get; } = new();

    public string LanguageId { get; set; } = languageId;

    public LineEnding LineEnding { get; set; }

    public Encoding Encoding { get; set; } = Encoding.UTF8;

    public bool IsModified => !History.IsAtSavePoint;

    public bool IsReadOnly => false;

    public void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal sealed class FakeEditorFeatures : IEditorFeatures
{
    public Func<TextSnapshot, IReadOnlyList<TextChange>?>? Format { get; set; }

    public RenameTarget? RenameTarget { get; set; }

    public Func<string, WorkspaceEdit>? Rename { get; set; }

    public List<CodeAction> Actions { get; } = [];

    public List<CodeActionRequest> ActionRequests { get; } = [];

    public List<IDecorationProvider> DecorationProviders { get; } = [];

    public Task<IReadOnlyList<CodeAction>> GetCodeActionsAsync(CodeActionRequest request, CancellationToken cancellationToken)
    {
        lock (ActionRequests)
            ActionRequests.Add(request);
        return Task.FromResult<IReadOnlyList<CodeAction>>([.. Actions]);
    }

    public Task<CodeAction> ResolveCodeActionAsync(CodeAction action, CancellationToken cancellationToken) => Task.FromResult(action);

    public Task<RenameTarget?> PrepareRenameAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken) =>
        Task.FromResult(RenameTarget);

    public Task<WorkspaceEdit?> RenameAsync(IDocument document, TextSnapshot snapshot, int offset, string newName, CancellationToken cancellationToken) =>
        Task.FromResult(Rename?.Invoke(newName));

    public Task<IReadOnlyList<TextChange>?> FormatDocumentAsync(IDocument document, TextSnapshot snapshot, FormattingOptions options, CancellationToken cancellationToken) =>
        Task.FromResult(Format?.Invoke(snapshot));

    public Task<IReadOnlyList<TextChange>?> FormatRangeAsync(IDocument document, TextSnapshot snapshot, TextSpan span, FormattingOptions options, CancellationToken cancellationToken) =>
        Task.FromResult(Format?.Invoke(snapshot));

    public IReadOnlyList<IDecorationProvider> GetDecorationProviders(IDocument document) => DecorationProviders;
}

internal sealed class FakeWorkspaceEdits : IWorkspaceEditService
{
    public List<WorkspaceEdit> Applied { get; } = [];

    public Task<bool> ApplyAsync(WorkspaceEdit edit, CancellationToken cancellationToken = default)
    {
        Applied.Add(edit);
        return Task.FromResult(true);
    }
}

internal sealed class FakeCommands : ICommandService
{
    public List<(string Id, object? Argument)> Ran { get; } = [];

    public IReadOnlyList<CommandDefinition> Commands => [];

    public event EventHandler? Changed;

    public CommandDefinition? Find(string commandId) => null;

    public bool CanExecute(string commandId, object? argument = null) => true;

    public Task<bool> ExecuteAsync(string commandId, object? argument = null)
    {
        Ran.Add((commandId, argument));
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(true);
    }

    public string? GetKeyBinding(string commandId) => null;
}

internal sealed class FakeLanguageFeatures : ILanguageFeatures
{
    public CompletionList Completions { get; set; } = CompletionList.Empty;

    public Func<CompletionItem, CompletionItem> Resolve { get; set; } = item => item;

    public bool IsCompletionTrigger(IDocument document, char character) => false;

    public Task<CompletionList> GetCompletionsAsync(CompletionRequest request, CancellationToken cancellationToken) => Task.FromResult(Completions);

    public int Resolves { get; private set; }

    public Task<CompletionItem> ResolveCompletionAsync(CompletionItem item, CancellationToken cancellationToken)
    {
        Resolves++;
        return Task.FromResult(Resolve(item));
    }

    public Task<HoverInfo?> GetHoverAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken) => Task.FromResult<HoverInfo?>(null);

    public Task<IReadOnlyList<DocumentLocation>> GetDefinitionsAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DocumentLocation>>([]);

    public bool IsSignatureHelpTrigger(IDocument document, char character) => false;

    public bool IsSignatureHelpRetrigger(IDocument document, char character) => false;

    public Task<SignatureHelpInfo?> GetSignatureHelpAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken) =>
        Task.FromResult<SignatureHelpInfo?>(null);
}

internal sealed class NoHighlighting : ISyntaxHighlighterProvider
{
    public ISyntaxHighlighter? Create(IDocument document) => null;
}

internal sealed class NoDiagnostics : IDiagnosticService
{
    public IReadOnlyList<Diagnostic> All => [];

    public event EventHandler<IReadOnlyCollection<string>>? Changed;

    public void Set(string source, string filePath, IReadOnlyList<Diagnostic> diagnostics) => Changed?.Invoke(this, [filePath]);

    public void Clear(string source)
    {
    }

    public IReadOnlyList<Diagnostic> Get(string filePath) => [];
}

internal sealed class NoLanguages : ILanguageRegistry
{
    public IReadOnlyList<LanguageDefinition> Languages => [];

    public LanguageDefinition? Find(string languageId) => null;

    public LanguageDefinition GetLanguageForFile(string filePath) => throw new NotSupportedException();
}

/// <summary>A decoration provider that answers what the test sets and can say its decorations changed.</summary>
internal sealed class ScriptedDecorations : IDecorationProvider
{
    public IReadOnlyList<Decoration> Decorations { get; set; } = [];

    public int Requests { get; private set; }

    public event EventHandler<DecorationsChangedEventArgs>? Changed;

    public Task<IReadOnlyList<Decoration>> GetDecorationsAsync(DecorationRequest request, CancellationToken cancellationToken)
    {
        Requests++;
        return Task.FromResult(Decorations);
    }

    public void Raise() => Changed?.Invoke(this, new DecorationsChangedEventArgs(null));
}

internal sealed class FakeNavigationFeatures : INavigationFeatures
{
    public List<DocumentLocation> References { get; } = [];

    public List<DocumentLocation> Implementations { get; } = [];

    public Func<int, IReadOnlyList<DocumentHighlight>> Highlights { get; set; } = _ => [];

    public int HighlightRequests { get; private set; }

    public IReadOnlyList<DocumentSymbol>? Symbols { get; set; }

    public event EventHandler<LanguageFeatureChangedEventArgs>? Changed;

    public Task<IReadOnlyList<DocumentLocation>> GetReferencesAsync(IDocument document, TextSnapshot snapshot, int offset, bool includeDeclaration, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DocumentLocation>>([.. References]);

    public Task<IReadOnlyList<DocumentLocation>> GetImplementationsAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DocumentLocation>>([.. Implementations]);

    public Task<IReadOnlyList<DocumentHighlight>> GetDocumentHighlightsAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken)
    {
        HighlightRequests++;
        return Task.FromResult(Highlights(offset));
    }

    public Task<IReadOnlyList<DocumentSymbol>?> GetDocumentSymbolsAsync(IDocument document, TextSnapshot snapshot, CancellationToken cancellationToken) => Task.FromResult(Symbols);

    public Task<IReadOnlyList<WorkspaceSymbol>> GetWorkspaceSymbolsAsync(string query, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WorkspaceSymbol>>([]);

    public Task<IReadOnlyList<HierarchyItem>> PrepareCallHierarchyAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<HierarchyItem>>([]);

    public Task<IReadOnlyList<HierarchyCall>> GetIncomingCallsAsync(HierarchyItem item, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<HierarchyCall>>([]);

    public Task<IReadOnlyList<HierarchyCall>> GetOutgoingCallsAsync(HierarchyItem item, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<HierarchyCall>>([]);

    public Task<IReadOnlyList<HierarchyItem>> PrepareTypeHierarchyAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<HierarchyItem>>([]);

    public Task<IReadOnlyList<HierarchyItem>> GetSupertypesAsync(HierarchyItem item, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<HierarchyItem>>([]);

    public Task<IReadOnlyList<HierarchyItem>> GetSubtypesAsync(HierarchyItem item, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<HierarchyItem>>([]);

    public void Raise(string? path) => Changed?.Invoke(this, new LanguageFeatureChangedEventArgs(path));
}

internal sealed class FakeSyntaxFeatures : ISyntaxFeatures
{
    public Func<TextSnapshot, IReadOnlyList<FoldingRange>?> Folding { get; set; } = _ => null;

    public Func<TextSnapshot, int, IReadOnlyList<TextSpan>?> SelectionRanges { get; set; } = (_, _) => null;

    public int FoldingRequests { get; private set; }

    public event EventHandler<LanguageFeatureChangedEventArgs>? Changed;

    public Task<IReadOnlyList<FoldingRange>?> GetFoldingRangesAsync(IDocument document, TextSnapshot snapshot, CancellationToken cancellationToken)
    {
        FoldingRequests++;
        return Task.FromResult(Folding(snapshot));
    }

    public Task<IReadOnlyList<TextSpan>?> GetSelectionRangesAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken) =>
        Task.FromResult(SelectionRanges(snapshot, offset));

    public bool HasSemanticTokens(IDocument document) => false;

    public Task<IReadOnlyList<SemanticToken>?> GetSemanticTokensAsync(IDocument document, TextSnapshot snapshot, TextSpan? span, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SemanticToken>?>(null);

    public void Raise(string? path) => Changed?.Invoke(this, new LanguageFeatureChangedEventArgs(path));
}
