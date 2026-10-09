using System.Composition;
using Runesmith.LanguageServices;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Text;
using SdkCompletionItem = Runesmith.Sdk.Languages.CompletionItem;
using SdkCompletionItemKind = Runesmith.Sdk.Languages.CompletionItemKind;
using SdkCompletionList = Runesmith.Sdk.Languages.CompletionList;

namespace Runesmith.Languages.Analyzers;

/// <summary>Completion from the language analyzers.</summary>
[Export(typeof(ICompletionProvider))]
[Languages(LanguagesAttribute.Any)]
[method: ImportingConstructor]
public sealed class AnalyzerCompletionProvider(AnalyzerBridge bridge) : ICompletionProvider
{
    public bool IsTriggerCharacter(IDocument document, char character) => bridge.AnalyzerOf(document)?.CompletionTriggerCharacters.Contains(character) == true;

    public async Task<SdkCompletionList> GetCompletionsAsync(CompletionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (bridge.PathOf(request.Document) is not { } path || bridge.AnalyzerOf(request.Document) is null)
            return SdkCompletionList.Empty;

        var trigger = request.Trigger switch
        {
            CompletionTrigger.Character => CompletionTriggerKind.Character,
            CompletionTrigger.Incomplete => CompletionTriggerKind.Incomplete,
            _ => CompletionTriggerKind.Invoked,
        };
        var result = await bridge.Host.CompleteAsync(path, request.Snapshot, request.Offset, trigger, request.TriggerCharacter, cancellationToken).ConfigureAwait(false);
        var items = new SdkCompletionItem[result.Items.Count];
        for (var i = 0; i < items.Length; i++)
        {
            var entry = result.Items[i];
            items[i] = new SdkCompletionItem(entry.Label, (SdkCompletionItemKind)(int)entry.Kind)
            {
                Detail = entry.Detail,
                InsertText = entry.InsertText,
                FilterText = entry.FilterText,
                ReplaceSpan = result.ReplaceSpan,
                SortText = i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture),
                Data = entry,
                Provider = this,
            };
        }

        return new SdkCompletionList(items, result.IsIncomplete);
    }

    public async Task<SdkCompletionItem> ResolveAsync(SdkCompletionItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Data is not CompletionEntry entry || await bridge.Host.ResolveAsync(entry, cancellationToken).ConfigureAwait(false) is not { } details)
            return item;

        return item with
        {
            Detail = details.Detail ?? item.Detail,
            Documentation = details.Documentation ?? item.Documentation,
            AdditionalChanges = details.AdditionalChanges.Count > 0 ? details.AdditionalChanges : item.AdditionalChanges,
        };
    }
}

/// <summary>Hover from the language analyzers.</summary>
[Export(typeof(IHoverProvider))]
[Languages(LanguagesAttribute.Any)]
[method: ImportingConstructor]
public sealed class AnalyzerHoverProvider(AnalyzerBridge bridge) : IHoverProvider
{
    public async Task<HoverInfo?> GetHoverAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken)
    {
        if (bridge.PathOf(document) is not { } path || bridge.AnalyzerOf(document) is null)
            return null;

        var hover = await bridge.Host.HoverAsync(path, snapshot, offset, cancellationToken).ConfigureAwait(false);
        return hover is null ? null : new HoverInfo(hover.Markdown, hover.Span);
    }
}

/// <summary>Go to definition from the language analyzers.</summary>
[Export(typeof(IDefinitionProvider))]
[Languages(LanguagesAttribute.Any)]
[method: ImportingConstructor]
public sealed class AnalyzerDefinitionProvider(AnalyzerBridge bridge) : IDefinitionProvider
{
    public async Task<IReadOnlyList<DocumentLocation>> GetDefinitionsAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken)
    {
        if (bridge.PathOf(document) is not { } path || bridge.AnalyzerOf(document) is null)
            return [];

        var locations = await bridge.Host.DefinitionAsync(path, snapshot, offset, cancellationToken).ConfigureAwait(false);
        return [.. locations.Select(l => new DocumentLocation(l.Path, l.Start, l.End))];
    }
}

/// <summary>Signature help from the language analyzers.</summary>
[Export(typeof(ISignatureHelpProvider))]
[Languages(LanguagesAttribute.Any)]
[method: ImportingConstructor]
public sealed class AnalyzerSignatureHelpProvider(AnalyzerBridge bridge) : ISignatureHelpProvider
{
    public bool IsTriggerCharacter(IDocument document, char character) => bridge.AnalyzerOf(document)?.SignatureHelpTriggerCharacters.Contains(character) == true;

    public bool IsRetriggerCharacter(IDocument document, char character) => bridge.AnalyzerOf(document)?.SignatureHelpRetriggerCharacters.Contains(character) == true;

    public async Task<SignatureHelpInfo?> GetSignatureHelpAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken)
    {
        if (bridge.PathOf(document) is not { } path || bridge.AnalyzerOf(document) is null)
            return null;

        var help = await bridge.Host.SignatureHelpAsync(path, snapshot, offset, cancellationToken).ConfigureAwait(false);
        return help is null
            ? null
            : new SignatureHelpInfo([.. help.Signatures.Select(s => new SignatureInfo(s.Label, s.Parameters, s.Documentation))], help.ActiveSignature, help.ActiveParameter);
    }
}
