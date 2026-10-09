using System.Composition;
using Runesmith.Lsp;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Text;
using Protocol = Runesmith.Lsp.Protocol;

namespace Runesmith.Languages.Servers;

/// <summary>Finds the running server of a document, waiting until it has every change made so far.</summary>
internal static class ServerLookup
{
    public static async Task<(ServerSession Session, LanguageClient Client, string Uri)?> FindAsync(
        LanguageServerManager manager, IDocument document, TextSnapshot snapshot)
    {
        // A request about text that has changed since would point at the wrong place.
        if (document.FilePath is not { } path || snapshot != document.Buffer.Current || manager.FindSession(document) is not { } session)
            return null;

        await session.WhenSynced().ConfigureAwait(false);
        return session.Client is { } client ? (session, client, LspUri.FromPath(path)) : null;
    }
}

/// <summary>Completion from the document's language server.</summary>
[Export(typeof(ICompletionProvider))]
[Languages(LanguagesAttribute.Any)]
[Shared]
[method: ImportingConstructor]
public sealed class LspCompletionProvider(LanguageServerManager manager) : ICompletionProvider
{
    public bool IsTriggerCharacter(IDocument document, char character) =>
        manager.FindSession(document)?.Capabilities.CompletionProvider?.TriggerCharacters?.Contains(character.ToString()) == true;

    public async Task<CompletionList> GetCompletionsAsync(CompletionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await ServerLookup.FindAsync(manager, request.Document, request.Snapshot).ConfigureAwait(false) is not var (session, client, uri))
            return CompletionList.Empty;

        var context = request.Trigger switch
        {
            CompletionTrigger.Character when request.TriggerCharacter is { } c => new Protocol.CompletionContext(Protocol.CompletionTriggerKind.TriggerCharacter, c.ToString()),
            CompletionTrigger.Incomplete => new Protocol.CompletionContext(Protocol.CompletionTriggerKind.TriggerForIncompleteCompletions),
            _ => new Protocol.CompletionContext(Protocol.CompletionTriggerKind.Invoked),
        };
        var list = await client.CompletionAsync(uri, LspConvert.ToPosition(request.Snapshot, request.Offset), context, cancellationToken).ConfigureAwait(false);
        var commitCharacters = session.Capabilities.CompletionProvider?.AllCommitCharacters;
        return new CompletionList([.. list.Items.Select(item => ToItem(item, request.Snapshot, client, commitCharacters))], list.IsIncomplete);
    }

    public async Task<CompletionItem> ResolveAsync(CompletionItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Data is not ResolveData data || data.Client.State != LanguageServerState.Running)
            return item;

        var resolved = await data.Client.ResolveCompletionItemAsync(data.Item, cancellationToken).ConfigureAwait(false);
        return item with
        {
            Detail = item.Detail ?? resolved.Detail,
            Documentation = item.Documentation ?? LspConvert.ToMarkdown(resolved.Documentation),
            AdditionalChanges = item.AdditionalChanges.Count > 0 ? item.AdditionalChanges : ToChanges(resolved.AdditionalTextEdits, data.Snapshot),
        };
    }

    private CompletionItem ToItem(Protocol.CompletionItem item, TextSnapshot snapshot, LanguageClient client, IReadOnlyList<string>? commitCharacters)
    {
        var snippet = item.InsertTextFormat == Protocol.InsertTextFormat.Snippet;
        var text = item.Edit?.NewText ?? item.InsertText;
        if (snippet && text is not null)
            text = LspConvert.StripSnippet(text);
        var range = item.Edit?.Range ?? item.Edit?.Insert;

        return new CompletionItem(item.Label, LspConvert.ToKind(item.Kind))
        {
            Detail = item.Detail,
            Documentation = LspConvert.ToMarkdown(item.Documentation),
            InsertText = text,
            ReplaceSpan = range is null ? null : LspConvert.ToSpan(snapshot, range),
            FilterText = item.FilterText,
            SortText = item.SortText,
            CommitCharacters = [.. (item.CommitCharacters ?? commitCharacters ?? []).Where(c => c.Length == 1).Select(c => c[0])],
            AdditionalChanges = ToChanges(item.AdditionalTextEdits, snapshot),
            IsPreselected = item.Preselect == true,
            Data = new ResolveData(client, item, snapshot),
            Provider = this,
        };
    }

    private static IReadOnlyList<TextChange> ToChanges(IReadOnlyList<Protocol.TextEdit>? edits, TextSnapshot snapshot) =>
        edits is null ? [] : [.. edits.Select(edit => new TextChange(LspConvert.ToSpan(snapshot, edit.Range), edit.NewText.ReplaceLineEndings("\n")))];

    private sealed record ResolveData(LanguageClient Client, Protocol.CompletionItem Item, TextSnapshot Snapshot);
}

/// <summary>Hover information from the document's language server.</summary>
[Export(typeof(IHoverProvider))]
[Languages(LanguagesAttribute.Any)]
[Shared]
[method: ImportingConstructor]
public sealed class LspHoverProvider(LanguageServerManager manager) : IHoverProvider
{
    public async Task<HoverInfo?> GetHoverAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken)
    {
        if (await ServerLookup.FindAsync(manager, document, snapshot).ConfigureAwait(false) is not var (_, client, uri))
            return null;

        var hover = await client.HoverAsync(uri, LspConvert.ToPosition(snapshot, offset), cancellationToken).ConfigureAwait(false);
        return hover is not null && LspConvert.ToMarkdown(hover.Contents) is { } markdown
            ? new HoverInfo(markdown, hover.Range is { } range ? LspConvert.ToSpan(snapshot, range) : null)
            : null;
    }
}

/// <summary>Go to definition through the document's language server.</summary>
[Export(typeof(IDefinitionProvider))]
[Languages(LanguagesAttribute.Any)]
[Shared]
[method: ImportingConstructor]
public sealed class LspDefinitionProvider(LanguageServerManager manager) : IDefinitionProvider
{
    public async Task<IReadOnlyList<DocumentLocation>> GetDefinitionsAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken)
    {
        if (await ServerLookup.FindAsync(manager, document, snapshot).ConfigureAwait(false) is not var (_, client, uri))
            return [];

        var locations = await client.DefinitionAsync(uri, LspConvert.ToPosition(snapshot, offset), cancellationToken).ConfigureAwait(false);
        return [.. locations.Select(LspConvert.ToLocation).OfType<DocumentLocation>()];
    }
}

/// <summary>Signature help from the document's language server, working out the current argument itself when the server does not say.</summary>
[Export(typeof(ISignatureHelpProvider))]
[Languages(LanguagesAttribute.Any)]
[Shared]
[method: ImportingConstructor]
public sealed class LspSignatureHelpProvider(LanguageServerManager manager) : ISignatureHelpProvider
{
    public bool IsTriggerCharacter(IDocument document, char character) =>
        manager.FindSession(document)?.Capabilities.SignatureHelpProvider?.TriggerCharacters?.Contains(character.ToString()) == true;

    public bool IsRetriggerCharacter(IDocument document, char character) =>
        manager.FindSession(document)?.Capabilities.SignatureHelpProvider?.RetriggerCharacters?.Contains(character.ToString()) == true
        || IsTriggerCharacter(document, character);

    public async Task<SignatureHelpInfo?> GetSignatureHelpAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken)
    {
        if (await ServerLookup.FindAsync(manager, document, snapshot).ConfigureAwait(false) is not var (_, client, uri))
            return null;

        var help = await client.SignatureHelpAsync(uri, LspConvert.ToPosition(snapshot, offset), cancellationToken).ConfigureAwait(false);
        if (help is null || help.Signatures.Count == 0)
            return null;

        var activeSignature = Math.Clamp(help.ActiveSignature ?? 0, 0, help.Signatures.Count - 1);
        var activeParameter = help.Signatures[activeSignature].ActiveParameter ?? help.ActiveParameter ?? ActiveParameter.Find(snapshot, offset);
        var signatures = help.Signatures.Select(signature =>
            new SignatureInfo(signature.Label, ParameterSpans(signature), LspConvert.ToMarkdown(signature.Documentation))).ToList();
        return new SignatureHelpInfo(signatures, activeSignature, activeParameter);
    }

    private static List<TextSpan> ParameterSpans(Protocol.SignatureInformation signature)
    {
        var spans = new List<TextSpan>();
        var searchFrom = signature.Label.IndexOf('(', StringComparison.Ordinal) + 1;
        foreach (var parameter in signature.Parameters ?? [])
        {
            var label = parameter.Label;
            if (label.Text is null)
            {
                var start = Math.Clamp(label.Start, 0, signature.Label.Length);
                spans.Add(TextSpan.FromBounds(start, Math.Clamp(label.End, start, signature.Label.Length)));
                continue;
            }

            var index = signature.Label.IndexOf(label.Text, searchFrom, StringComparison.Ordinal);
            if (index < 0)
                index = signature.Label.IndexOf(label.Text, StringComparison.Ordinal);
            spans.Add(index < 0 ? new TextSpan(0, 0) : new TextSpan(index, label.Text.Length));
            if (index >= 0)
                searchFrom = index + label.Text.Length;
        }

        return spans;
    }
}
