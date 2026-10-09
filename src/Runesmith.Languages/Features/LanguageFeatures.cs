using System.Composition;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Shell;
using Runesmith.Text;

namespace Runesmith.Languages.Features;

/// <summary>Asks every provider that serves a document's language and merges what they answer.</summary>
/// <remarks>A provider that throws is logged to the "Language features" output channel and left out, so one broken plugin does not take
/// completion or hover away from the others. Cancellation passes through to the caller.</remarks>
[Export(typeof(ILanguageFeatures))]
[Shared]
[method: ImportingConstructor]
public sealed class LanguageFeatures(
    [ImportMany] IEnumerable<Lazy<ICompletionProvider, LanguageMetadata>> completionProviders,
    [ImportMany] IEnumerable<Lazy<IHoverProvider, LanguageMetadata>> hoverProviders,
    [ImportMany] IEnumerable<Lazy<IDefinitionProvider, LanguageMetadata>> definitionProviders,
    [ImportMany] IEnumerable<Lazy<ISignatureHelpProvider, LanguageMetadata>> signatureHelpProviders,
    Lazy<IOutputService> output) : ILanguageFeatures
{
    private const string ChannelName = "Language features";

    private readonly Lazy<ICompletionProvider, LanguageMetadata>[] completion = [.. completionProviders];
    private readonly Lazy<IHoverProvider, LanguageMetadata>[] hover = [.. hoverProviders];
    private readonly Lazy<IDefinitionProvider, LanguageMetadata>[] definition = [.. definitionProviders];
    private readonly Lazy<ISignatureHelpProvider, LanguageMetadata>[] signatureHelp = [.. signatureHelpProviders];

    public bool IsCompletionTrigger(IDocument document, char character) =>
        Serving(completion, document).Any(provider => Safe(() => provider.IsTriggerCharacter(document, character), false));

    public async Task<CompletionList> GetCompletionsAsync(CompletionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var lists = await Task.WhenAll(Serving(completion, request.Document).Select(async provider =>
        {
            var list = await SafeAsync(() => provider.GetCompletionsAsync(request, cancellationToken), CompletionList.Empty).ConfigureAwait(false);
            return list with { Items = [.. list.Items.Select(item => item.Provider is null ? item with { Provider = provider } : item)] };
        })).ConfigureAwait(false);

        return lists.Length switch
        {
            0 => CompletionList.Empty,
            1 => lists[0],
            _ => new CompletionList([.. lists.SelectMany(list => list.Items)], lists.Any(list => list.IsIncomplete)),
        };
    }

    public Task<CompletionItem> ResolveCompletionAsync(CompletionItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Provider is { } provider ? SafeAsync(() => provider.ResolveAsync(item, cancellationToken), item) : Task.FromResult(item);
    }

    public async Task<HoverInfo?> GetHoverAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken)
    {
        foreach (var provider in Serving(hover, document))
        {
            if (await SafeAsync(() => provider.GetHoverAsync(document, snapshot, offset, cancellationToken), null).ConfigureAwait(false) is { } info)
                return info;
        }

        return null;
    }

    public async Task<IReadOnlyList<DocumentLocation>> GetDefinitionsAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken)
    {
        var results = await Task.WhenAll(Serving(definition, document).Select(provider =>
            SafeAsync(() => provider.GetDefinitionsAsync(document, snapshot, offset, cancellationToken), []))).ConfigureAwait(false);
        return [.. results.SelectMany(locations => locations).Distinct()];
    }

    public bool IsSignatureHelpTrigger(IDocument document, char character) =>
        Serving(signatureHelp, document).Any(provider => Safe(() => provider.IsTriggerCharacter(document, character), false));

    public bool IsSignatureHelpRetrigger(IDocument document, char character) =>
        Serving(signatureHelp, document).Any(provider => Safe(() => provider.IsRetriggerCharacter(document, character), false));

    public async Task<SignatureHelpInfo?> GetSignatureHelpAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken)
    {
        foreach (var provider in Serving(signatureHelp, document))
        {
            if (await SafeAsync(() => provider.GetSignatureHelpAsync(document, snapshot, offset, cancellationToken), null).ConfigureAwait(false) is { } help)
                return help;
        }

        return null;
    }

    private IEnumerable<T> Serving<T>(Lazy<T, LanguageMetadata>[] providers, IDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        foreach (var provider in providers)
        {
            if (!provider.Metadata.Serves(document.LanguageId))
                continue;

            T? value;
            try
            {
                value = provider.Value;
            }
            catch (Exception exception)
            {
                Log(exception);
                continue;
            }

            yield return value;
        }
    }

    private T Safe<T>(Func<T> action, T fallback)
    {
        try
        {
            return action();
        }
        catch (Exception exception)
        {
            Log(exception);
            return fallback;
        }
    }

    private async Task<T> SafeAsync<T>(Func<Task<T>> action, T fallback)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Log(exception);
            return fallback;
        }
    }

    private void Log(Exception exception) => output.Value.GetChannel(ChannelName).AppendLine($"[{DateTime.Now:HH:mm:ss}] {exception}");
}
