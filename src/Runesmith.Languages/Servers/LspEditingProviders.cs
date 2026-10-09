using System.Composition;
using Runesmith.Lsp;
using Runesmith.Sdk.Build;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Settings;
using Runesmith.Text;
using Protocol = Runesmith.Lsp.Protocol;
using SdkCodeAction = Runesmith.Sdk.Languages.CodeAction;
using SdkCodeLens = Runesmith.Sdk.Documents.CodeLens;
using SdkInlayHint = Runesmith.Sdk.Documents.InlayHint;

namespace Runesmith.Languages.Servers;

/// <summary>A command of a language server, run through <see cref="LanguageServerCommands.RunServerCommand"/>.</summary>
internal sealed record ServerCommandInvocation(LanguageClient Client, Protocol.Command Command);

/// <summary>Quick fixes and refactorings from the document's language server.</summary>
[Export(typeof(ICodeActionProvider))]
[Languages(LanguagesAttribute.Any)]
[Shared]
[method: ImportingConstructor]
public sealed class LspCodeActionProvider(LanguageServerManager manager, IDocumentService documents) : ICodeActionProvider
{
    public async Task<IReadOnlyList<SdkCodeAction>> GetCodeActionsAsync(CodeActionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await ServerLookup.FindAsync(manager, request.Document, request.Snapshot).ConfigureAwait(false) is not var (session, client, uri)
            || session.Capabilities.CodeActionProvider is null)
        {
            return [];
        }

        var range = LspConvert.ToRange(request.Snapshot, request.Span);
        var diagnostics = session.PublishedDiagnostics(request.Document.FilePath!).Where(d => Overlaps(d.Range, range)).ToList();
        var automatic = request.Trigger == CodeActionTrigger.Automatic;
        var context = new Protocol.CodeActionContext(diagnostics, automatic ? [Protocol.CodeActionKinds.QuickFix] : null,
            automatic ? Protocol.CodeActionTriggerKind.Automatic : Protocol.CodeActionTriggerKind.Invoked);
        var actions = await client.CodeActionsAsync(uri, range, context, cancellationToken).ConfigureAwait(false);
        return
        [
            .. actions.Where(action => action.Disabled is null).Select(action => new SdkCodeAction(action.Title, ToKind(action.Kind))
            {
                Edit = action.Edit is { } edit ? LspConvert.ToWorkspaceEdit(edit, documents) : null,
                CommandId = action.Command is null ? null : LanguageServerCommands.RunServerCommand,
                CommandArgument = action.Command is { } command ? new ServerCommandInvocation(client, command) : null,
                IsPreferred = action.IsPreferred == true,
                Data = new ResolveData(client, action),
                Provider = this,
            }),
        ];
    }

    public async Task<SdkCodeAction> ResolveAsync(SdkCodeAction action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action.Edit is not null || action.Data is not ResolveData data || data.Client.State != LanguageServerState.Running)
            return action;

        var resolved = await data.Client.ResolveCodeActionAsync(data.Action, cancellationToken).ConfigureAwait(false);
        return action with
        {
            Edit = resolved.Edit is { } edit ? LspConvert.ToWorkspaceEdit(edit, documents) : null,
            CommandId = resolved.Command is null ? action.CommandId : LanguageServerCommands.RunServerCommand,
            CommandArgument = resolved.Command is { } command ? new ServerCommandInvocation(data.Client, command) : action.CommandArgument,
        };
    }

    internal static CodeActionKind ToKind(string? kind) =>
        kind is null || kind.StartsWith(Protocol.CodeActionKinds.QuickFix, StringComparison.Ordinal) ? CodeActionKind.QuickFix
        : kind.StartsWith(Protocol.CodeActionKinds.Source, StringComparison.Ordinal) ? CodeActionKind.Source
        : CodeActionKind.Refactor;

    private static bool Overlaps(Protocol.Range a, Protocol.Range b) => Compare(a.Start, b.End) <= 0 && Compare(b.Start, a.End) <= 0;

    private static int Compare(Protocol.Position a, Protocol.Position b) => a.Line != b.Line ? a.Line.CompareTo(b.Line) : a.Character.CompareTo(b.Character);

    private sealed record ResolveData(LanguageClient Client, Protocol.CodeAction Action);
}

/// <summary>Rename through the document's language server.</summary>
[Export(typeof(IRenameProvider))]
[Languages(LanguagesAttribute.Any)]
[Shared]
[method: ImportingConstructor]
public sealed class LspRenameProvider(LanguageServerManager manager, IDocumentService documents) : IRenameProvider
{
    public async Task<RenameTarget?> PrepareRenameAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken)
    {
        if (await ServerLookup.FindAsync(manager, document, snapshot).ConfigureAwait(false) is not var (session, client, uri) || session.Capabilities.RenameProvider is null)
            return null;

        Protocol.PrepareRenameResult? result;
        try
        {
            result = await client.PrepareRenameAsync(uri, LspConvert.ToPosition(snapshot, offset), cancellationToken).ConfigureAwait(false);
        }
        catch (LspException exception) when (exception.Code != LspException.RequestCancelled)
        {
            throw new LanguageFeatureException(exception.Message, exception);
        }

        if (result is null)
            throw new LanguageFeatureException("The language server cannot rename this.");

        var span = result.Range is { } range ? LspConvert.ToSpan(snapshot, range) : WordBoundaries.GetWordAt(snapshot, offset);
        if (span.IsEmpty)
            return null;
        return new RenameTarget(span, result.Placeholder ?? snapshot.GetText(span));
    }

    public async Task<WorkspaceEdit?> RenameAsync(IDocument document, TextSnapshot snapshot, int offset, string newName, CancellationToken cancellationToken)
    {
        if (await ServerLookup.FindAsync(manager, document, snapshot).ConfigureAwait(false) is not var (session, client, uri) || session.Capabilities.RenameProvider is null)
            return null;

        try
        {
            var edit = await client.RenameAsync(uri, LspConvert.ToPosition(snapshot, offset), newName, cancellationToken).ConfigureAwait(false);
            return edit is null ? WorkspaceEdit.Empty : LspConvert.ToWorkspaceEdit(edit, documents);
        }
        catch (LspException exception) when (exception.Code != LspException.RequestCancelled)
        {
            throw new LanguageFeatureException(exception.Message, exception);
        }
    }
}

/// <summary>Document and range formatting through the document's language server.</summary>
[Export(typeof(IDocumentFormattingProvider))]
[Export(typeof(IRangeFormattingProvider))]
[Languages(LanguagesAttribute.Any)]
[Shared]
[method: ImportingConstructor]
public sealed class LspFormattingProvider(LanguageServerManager manager) : IDocumentFormattingProvider, IRangeFormattingProvider
{
    public async Task<IReadOnlyList<TextChange>?> FormatDocumentAsync(IDocument document, TextSnapshot snapshot, FormattingOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (await ServerLookup.FindAsync(manager, document, snapshot).ConfigureAwait(false) is not var (_, client, uri))
            return null;

        var edits = await client.FormattingAsync(uri, new Protocol.FormattingOptions(options.TabSize, options.InsertSpaces), cancellationToken).ConfigureAwait(false);
        return edits is null ? null : LspConvert.ToChanges(snapshot, edits);
    }

    public async Task<IReadOnlyList<TextChange>?> FormatRangeAsync(IDocument document, TextSnapshot snapshot, TextSpan span, FormattingOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (await ServerLookup.FindAsync(manager, document, snapshot).ConfigureAwait(false) is not var (_, client, uri))
            return null;

        var edits = await client.RangeFormattingAsync(uri, LspConvert.ToRange(snapshot, span), new Protocol.FormattingOptions(options.TabSize, options.InsertSpaces),
            cancellationToken).ConfigureAwait(false);
        return edits is null ? null : LspConvert.ToChanges(snapshot, edits);
    }
}

/// <summary>Inlay hints and code lenses from the document's language server, unless the settings turn them off.</summary>
[Export(typeof(IDecorationProvider))]
[Languages(LanguagesAttribute.Any)]
[Shared]
public sealed class LspDecorationProvider : IDecorationProvider
{
    private readonly LanguageServerManager manager;
    private readonly ISettingsService settings;

    [ImportingConstructor]
    public LspDecorationProvider(LanguageServerManager manager, ISettingsService settings)
    {
        this.manager = manager;
        this.settings = settings;
        manager.DecorationsChanged += (_, _) => Changed?.Invoke(this, new DecorationsChangedEventArgs(null));
        settings.Changed += (_, e) =>
        {
            if (e.Key is SettingKeys.InlayHints or SettingKeys.CodeLens)
                Changed?.Invoke(this, new DecorationsChangedEventArgs(null));
        };
    }

    public event EventHandler<DecorationsChangedEventArgs>? Changed;

    public async Task<IReadOnlyList<Decoration>> GetDecorationsAsync(DecorationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var snapshot = request.Snapshot;
        if (await ServerLookup.FindAsync(manager, request.Document, snapshot).ConfigureAwait(false) is not var (session, client, uri))
            return [];

        var inlayHints = session.Capabilities.InlayHintProvider is not null && settings.Get<bool>(SettingKeys.InlayHints)
            ? client.InlayHintsAsync(uri, LspConvert.ToRange(snapshot, new TextSpan(0, snapshot.Length)), cancellationToken)
            : Task.FromResult<IReadOnlyList<Protocol.InlayHint>>([]);
        var codeLenses = session.Capabilities.CodeLensProvider is not null && settings.Get<bool>(SettingKeys.CodeLens)
            ? client.CodeLensAsync(uri, cancellationToken)
            : Task.FromResult<IReadOnlyList<Protocol.CodeLens>>([]);
        return ToDecorations(snapshot, client, await inlayHints.ConfigureAwait(false), await codeLenses.ConfigureAwait(false));
    }

    internal static IReadOnlyList<Decoration> ToDecorations(TextSnapshot snapshot, LanguageClient? client, IReadOnlyList<Protocol.InlayHint> inlayHints,
        IReadOnlyList<Protocol.CodeLens> codeLenses)
    {
        var decorations = new List<Decoration>(inlayHints.Count + codeLenses.Count);
        foreach (var hint in inlayHints)
        {
            var text = (hint.PaddingLeft == true ? " " : "") + hint.Text + (hint.PaddingRight == true ? " " : "");
            if (string.IsNullOrWhiteSpace(text))
                continue;

            decorations.Add(new SdkInlayHint(LspConvert.ToOffset(snapshot, hint.Position), text)
            {
                Kind = hint.Kind switch
                {
                    Protocol.InlayHintKind.Type => InlayHintKind.Type,
                    Protocol.InlayHintKind.Parameter => InlayHintKind.Parameter,
                    _ => InlayHintKind.Other,
                },
                Side = hint.Kind == Protocol.InlayHintKind.Type ? InlayHintSide.After : InlayHintSide.Before,
                ToolTip = LspConvert.ToMarkdown(hint.Tooltip),
            });
        }

        foreach (var lens in codeLenses)
        {
            if (lens.Command is not { } command || string.IsNullOrWhiteSpace(command.Title))
                continue;

            decorations.Add(new SdkCodeLens(LspConvert.ToSpan(snapshot, lens.Range), command.Title)
            {
                CommandId = client is null || string.IsNullOrEmpty(command.Name) ? null : LanguageServerCommands.RunServerCommand,
                CommandArgument = client is null ? null : new ServerCommandInvocation(client, command),
            });
        }

        return decorations;
    }
}
