using System.Composition;
using Runesmith.Languages.Features;
using Runesmith.LanguageServices;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Settings;
using Runesmith.Text;
using SdkCodeAction = Runesmith.Sdk.Languages.CodeAction;

namespace Runesmith.Languages.Analyzers;

/// <summary>Turns the analyzers' file edits into workspace edits of the editor's documents.</summary>
[Export]
[Shared]
[method: ImportingConstructor]
public sealed class AnalyzerEdits(AnalyzerBridge bridge, IDocumentService documents)
{
    /// <summary>Converts file edits; edits of an open document's older version move through the changes made since.</summary>
    public WorkspaceEdit ToWorkspaceEdit(IReadOnlyList<FileEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        var result = new List<DocumentEdit>();
        foreach (var edit in edits)
        {
            var positions = edit.Edits.Select(e => (e.Start, e.End, e.NewText));
            if (edit.Version is { } version && bridge.Host.GetDocument(edit.Path, version) is { } kept && bridge.Host.GetDocument(edit.Path) is { } latest)
            {
                var changes = WorkspaceEdits.ToChanges(kept.Snapshot, positions);
                if (version != latest.Version)
                {
                    changes =
                    [
                        .. changes.Select(change => bridge.Host.MapToLatest(edit.Path, version, change.Span) is { } span
                            ? change with { Span = span }
                            : throw new LanguageFeatureException($"{Path.GetFileName(edit.Path)} changed too much while the edit was computed.")),
                    ];
                }

                result.Add(new DocumentEdit(edit.Path, changes) { Snapshot = latest.Snapshot });
            }
            else if (WorkspaceEdits.ReadBasis(documents, edit.Path) is { } basis)
            {
                result.Add(new DocumentEdit(edit.Path, WorkspaceEdits.ToChanges(basis, positions)) { Snapshot = documents.Find(edit.Path) is null ? null : basis });
            }
            else
            {
                throw new LanguageFeatureException($"{Path.GetFileName(edit.Path)} could not be read.");
            }
        }

        return new WorkspaceEdit(result);
    }
}

/// <summary>Quick fixes and refactorings from the language analyzers.</summary>
[Export(typeof(ICodeActionProvider))]
[Languages(LanguagesAttribute.Any)]
[method: ImportingConstructor]
public sealed class AnalyzerCodeActionProvider(AnalyzerBridge bridge, AnalyzerEdits edits) : ICodeActionProvider
{
    public async Task<IReadOnlyList<SdkCodeAction>> GetCodeActionsAsync(CodeActionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (bridge.PathOf(request.Document) is not { } path || bridge.AnalyzerOf(request.Document) is not ICodeActionAnalyzer)
            return [];

        var entries = await bridge.Host.CodeActionsAsync(path, request.Snapshot, request.Span, request.Trigger == CodeActionTrigger.Invoked, cancellationToken)
            .ConfigureAwait(false);
        return
        [
            .. entries.Select(entry => new SdkCodeAction(entry.Title, entry.IsRefactoring ? CodeActionKind.Refactor : CodeActionKind.QuickFix)
            {
                IsPreferred = entry.IsPreferred,
                Data = entry,
                Provider = this,
            }),
        ];
    }

    public async Task<SdkCodeAction> ResolveAsync(SdkCodeAction action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action.Edit is not null || action.Data is not CodeActionEntry entry)
            return action;

        try
        {
            var fileEdits = await bridge.Host.ResolveCodeActionAsync(entry, cancellationToken).ConfigureAwait(false);
            return action with { Edit = edits.ToWorkspaceEdit(fileEdits) };
        }
        catch (AnalyzerRefusalException refusal)
        {
            throw new LanguageFeatureException(refusal.Message, refusal);
        }
    }
}

/// <summary>Rename from the language analyzers.</summary>
[Export(typeof(IRenameProvider))]
[Languages(LanguagesAttribute.Any)]
[method: ImportingConstructor]
public sealed class AnalyzerRenameProvider(AnalyzerBridge bridge, AnalyzerEdits edits) : IRenameProvider
{
    public async Task<RenameTarget?> PrepareRenameAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken)
    {
        if (bridge.PathOf(document) is not { } path || bridge.AnalyzerOf(document) is not IRenameAnalyzer)
            return null;

        try
        {
            var site = await bridge.Host.PrepareRenameAsync(path, snapshot, offset, cancellationToken).ConfigureAwait(false);
            return site is null ? null : new RenameTarget(site.Span, site.Name);
        }
        catch (AnalyzerRefusalException refusal)
        {
            throw new LanguageFeatureException(refusal.Message, refusal);
        }
    }

    public async Task<WorkspaceEdit?> RenameAsync(IDocument document, TextSnapshot snapshot, int offset, string newName, CancellationToken cancellationToken)
    {
        if (bridge.PathOf(document) is not { } path || bridge.AnalyzerOf(document) is not IRenameAnalyzer)
            return null;

        try
        {
            var fileEdits = await bridge.Host.RenameAsync(path, snapshot, offset, newName, cancellationToken).ConfigureAwait(false);
            return fileEdits is null ? null : edits.ToWorkspaceEdit(fileEdits);
        }
        catch (AnalyzerRefusalException refusal)
        {
            throw new LanguageFeatureException(refusal.Message, refusal);
        }
    }
}

/// <summary>Document and range formatting from the language analyzers.</summary>
[Export(typeof(IDocumentFormattingProvider))]
[Export(typeof(IRangeFormattingProvider))]
[Languages(LanguagesAttribute.Any)]
[method: ImportingConstructor]
public sealed class AnalyzerFormattingProvider(AnalyzerBridge bridge) : IDocumentFormattingProvider, IRangeFormattingProvider
{
    public Task<IReadOnlyList<TextChange>?> FormatDocumentAsync(IDocument document, TextSnapshot snapshot, FormattingOptions options, CancellationToken cancellationToken) =>
        FormatAsync(document, snapshot, null, options, cancellationToken);

    public Task<IReadOnlyList<TextChange>?> FormatRangeAsync(IDocument document, TextSnapshot snapshot, TextSpan span, FormattingOptions options,
        CancellationToken cancellationToken) =>
        FormatAsync(document, snapshot, span, options, cancellationToken);

    private async Task<IReadOnlyList<TextChange>?> FormatAsync(IDocument document, TextSnapshot snapshot, TextSpan? span, FormattingOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (bridge.PathOf(document) is not { } path || bridge.AnalyzerOf(document) is not IFormattingAnalyzer)
            return null;

        var changes = await bridge.Host.FormatAsync(path, snapshot, span, options.TabSize, options.InsertSpaces, cancellationToken).ConfigureAwait(false);
        return changes is null ? null : [.. changes.Select(change => change with { NewText = LineEndings.Normalize(change.NewText) })];
    }
}

/// <summary>Inlay hints from the language analyzers, unless the <see cref="SettingKeys.InlayHints"/> setting turns them off; asked again each
/// time an analyzer finishes checking a document, such as once its project has loaded.</summary>
[Export(typeof(IDecorationProvider))]
[Languages(LanguagesAttribute.Any)]
[Shared]
public sealed class AnalyzerDecorationProvider : IDecorationProvider
{
    private readonly AnalyzerBridge bridge;
    private readonly ISettingsService settings;

    [ImportingConstructor]
    public AnalyzerDecorationProvider(AnalyzerBridge bridge, ISettingsService settings)
    {
        this.bridge = bridge;
        this.settings = settings;
        bridge.Host.ProblemsFound += (document, _) =>
        {
            if (bridge.Host.AnalyzerFor(document.LanguageId) is IInlayHintAnalyzer && settings.Get<bool>(SettingKeys.InlayHints))
                Changed?.Invoke(this, new DecorationsChangedEventArgs(document.Path));
        };
        settings.Changed += (_, e) =>
        {
            if (e.Key == SettingKeys.InlayHints)
                Changed?.Invoke(this, new DecorationsChangedEventArgs(null));
        };
    }

    public event EventHandler<DecorationsChangedEventArgs>? Changed;

    public async Task<IReadOnlyList<Decoration>> GetDecorationsAsync(DecorationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!settings.Get<bool>(SettingKeys.InlayHints) || bridge.PathOf(request.Document) is not { } path || bridge.AnalyzerOf(request.Document) is not IInlayHintAnalyzer)
            return [];

        var hints = await bridge.Host.InlayHintsAsync(path, request.Snapshot, cancellationToken).ConfigureAwait(false);
        return
        [
            .. hints.Select(hint => new InlayHint(hint.Offset, hint.Text)
            {
                Kind = hint.IsType ? InlayHintKind.Type : InlayHintKind.Parameter,
                Side = hint.IsType ? InlayHintSide.After : InlayHintSide.Before,
            }),
        ];
    }
}
