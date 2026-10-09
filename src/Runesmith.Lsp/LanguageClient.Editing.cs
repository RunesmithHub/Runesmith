using System.Text.Json;
using Runesmith.Lsp.Protocol;
using Range = Runesmith.Lsp.Protocol.Range;

namespace Runesmith.Lsp;

public sealed partial class LanguageClient
{
    /// <summary>Gets or sets what applies an edit the server asks for with <c>workspace/applyEdit</c>, such as after running a command; without
    /// it, such requests are refused.</summary>
    public Func<ApplyWorkspaceEditParams, Task<bool>>? ApplyEdit { get; set; }

    /// <summary>Raised when the server asks the client to request its inlay hints or code lenses again.</summary>
    public event EventHandler? DecorationsRefreshRequested;

    /// <summary>Gets the code actions for a range: actions with an edit or a command, and bare commands as actions with only a command;
    /// empty when the server offers none.</summary>
    public async Task<IReadOnlyList<CodeAction>> CodeActionsAsync(string uri, Range range, CodeActionContext context, CancellationToken cancellationToken)
    {
        if (Capabilities.CodeActionProvider is null)
            return [];

        var result = await RequestAsync<JsonElement>("textDocument/codeAction", new CodeActionParams(new TextDocumentIdentifier(uri), range, context), cancellationToken)
            .ConfigureAwait(false);
        if (result.ValueKind != JsonValueKind.Array)
            return [];

        var actions = new List<CodeAction>();
        foreach (var element in result.EnumerateArray())
        {
            if (element.TryGetProperty("command", out var command) && command.ValueKind == JsonValueKind.String)
            {
                if (element.Deserialize(LspJsonContext.Default.Command) is { } bare)
                    actions.Add(new CodeAction { Title = bare.Title, Command = bare });
            }
            else if (element.Deserialize(LspJsonContext.Default.CodeAction) is { } action)
            {
                actions.Add(action);
            }
        }

        return actions;
    }

    /// <summary>Fills in a code action's edit; returns the action unchanged when the server does not resolve actions.</summary>
    public async Task<CodeAction> ResolveCodeActionAsync(CodeAction action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (Capabilities.CodeActionProvider?.ResolveProvider != true)
            return action;
        return await RequestAsync<CodeAction>("codeAction/resolve", action, cancellationToken).ConfigureAwait(false) ?? action;
    }

    /// <summary>Checks that the symbol at a position can be renamed: null when it cannot, a result without a range when the server leaves the
    /// range to the client, and the range and placeholder otherwise.</summary>
    /// <exception cref="LspException">The server refused, usually with a message for the user.</exception>
    public async Task<PrepareRenameResult?> PrepareRenameAsync(string uri, Position position, CancellationToken cancellationToken)
    {
        if (Capabilities.RenameProvider is not { } rename)
            return null;
        if (!rename.PrepareProvider)
            return new PrepareRenameResult(null, null);

        var result = await RequestAsync<JsonElement>("textDocument/prepareRename", new TextDocumentPositionParams(new TextDocumentIdentifier(uri), position), cancellationToken)
            .ConfigureAwait(false);
        if (result.ValueKind != JsonValueKind.Object)
            return null;
        if (result.TryGetProperty("defaultBehavior", out var defaultBehavior))
            return defaultBehavior.ValueKind == JsonValueKind.True ? new PrepareRenameResult(null, null) : null;
        if (result.TryGetProperty("range", out var range))
        {
            var placeholder = result.TryGetProperty("placeholder", out var text) ? text.GetString() : null;
            return new PrepareRenameResult(range.Deserialize(LspJsonContext.Default.Range), placeholder);
        }

        return new PrepareRenameResult(result.Deserialize(LspJsonContext.Default.Range), null);
    }

    /// <summary>Renames the symbol at a position; null when the server does not rename.</summary>
    public Task<WorkspaceEdit?> RenameAsync(string uri, Position position, string newName, CancellationToken cancellationToken) =>
        Capabilities.RenameProvider is not null
            ? RequestAsync<WorkspaceEdit>("textDocument/rename", new RenameParams(new TextDocumentIdentifier(uri), position, newName), cancellationToken)
            : Task.FromResult<WorkspaceEdit?>(null);

    /// <summary>Formats a document; null when the server does not format.</summary>
    public async Task<IReadOnlyList<TextEdit>?> FormattingAsync(string uri, FormattingOptions options, CancellationToken cancellationToken)
    {
        if (!Capabilities.DocumentFormattingProvider)
            return null;
        return await RequestAsync<TextEdit[]>("textDocument/formatting", new DocumentFormattingParams(new TextDocumentIdentifier(uri), options), cancellationToken)
            .ConfigureAwait(false) ?? [];
    }

    /// <summary>Formats a range of a document; null when the server does not format ranges.</summary>
    public async Task<IReadOnlyList<TextEdit>?> RangeFormattingAsync(string uri, Range range, FormattingOptions options, CancellationToken cancellationToken)
    {
        if (!Capabilities.DocumentRangeFormattingProvider)
            return null;
        return await RequestAsync<TextEdit[]>("textDocument/rangeFormatting", new DocumentRangeFormattingParams(new TextDocumentIdentifier(uri), range, options),
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Gets the inlay hints of a range; empty when the server offers none.</summary>
    public async Task<IReadOnlyList<InlayHint>> InlayHintsAsync(string uri, Range range, CancellationToken cancellationToken)
    {
        if (Capabilities.InlayHintProvider is null)
            return [];
        return await RequestAsync<InlayHint[]>("textDocument/inlayHint", new InlayHintParams(new TextDocumentIdentifier(uri), range), cancellationToken)
            .ConfigureAwait(false) ?? [];
    }

    /// <summary>Gets the code lenses of a document, resolving each that arrives without a command; empty when the server offers none.</summary>
    public async Task<IReadOnlyList<CodeLens>> CodeLensAsync(string uri, CancellationToken cancellationToken)
    {
        if (Capabilities.CodeLensProvider is not { } options)
            return [];

        var lenses = await RequestAsync<CodeLens[]>("textDocument/codeLens", new CodeLensParams(new TextDocumentIdentifier(uri)), cancellationToken)
            .ConfigureAwait(false) ?? [];
        if (!options.ResolveProvider || lenses.All(lens => lens.Command is not null))
            return lenses;

        return await Task.WhenAll(lenses.Select(async lens => lens.Command is not null
            ? lens
            : await RequestAsync<CodeLens>("codeLens/resolve", lens, cancellationToken).ConfigureAwait(false) ?? lens)).ConfigureAwait(false);
    }

    /// <summary>Asks the server to run one of its commands, such as one a code action or code lens names.</summary>
    public async Task ExecuteCommandAsync(Command command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        await RequestAsync<JsonElement>("workspace/executeCommand", new ExecuteCommandParams(command.Name, command.Arguments), cancellationToken).ConfigureAwait(false);
    }

    private void RegisterEditing(JsonRpcConnection connection)
    {
        connection.OnRequest("workspace/applyEdit", async (parameters, _) =>
        {
            if (parameters?.Deserialize(LspJsonContext.Default.ApplyWorkspaceEditParams) is not { } request || ApplyEdit is not { } apply)
                return new ApplyWorkspaceEditResult(false, "The client cannot apply this edit.");
            return await apply(request).ConfigureAwait(false) ? new ApplyWorkspaceEditResult(true) : new ApplyWorkspaceEditResult(false, "The edit was not applied.");
        });
        connection.OnRequest("workspace/inlayHint/refresh", (_, _) => Refresh());
        connection.OnRequest("workspace/codeLens/refresh", (_, _) => Refresh());
    }

    private Task<object?> Refresh()
    {
        DecorationsRefreshRequested?.Invoke(this, EventArgs.Empty);
        return Task.FromResult<object?>(null);
    }
}
