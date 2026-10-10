using System.Composition;
using Runesmith.Composition;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Shell;
using Runesmith.Text;

namespace Runesmith.Languages.Features;

/// <summary>Asks the code action, rename, formatting and decoration providers that serve a document's language, and merges or picks what they
/// answer.</summary>
/// <remarks>Providers for a named language come before providers for every language, and providers from plugins before Runesmith's own, so
/// for rename and formatting, where the first answer wins, the most specific provider answers. A provider that throws is logged with its plugin
/// and left out.</remarks>
[Export(typeof(IEditorFeatures))]
[Shared]
public sealed class EditorFeatures : IEditorFeatures
{
    private const string ChannelName = "Language features";

    private readonly Lazy<ICodeActionProvider, LanguageMetadata>[] codeActions;
    private readonly Lazy<IRenameProvider, LanguageMetadata>[] rename;
    private readonly Lazy<IDocumentFormattingProvider, LanguageMetadata>[] documentFormatting;
    private readonly Lazy<IRangeFormattingProvider, LanguageMetadata>[] rangeFormatting;
    private readonly Lazy<IDecorationProvider, LanguageMetadata>[] decorations;
    private readonly GuardedDecorationProvider?[] guardedDecorations;
    private readonly Lazy<IOutputService> output;

    [ImportingConstructor]
    public EditorFeatures(
        [ImportMany] IEnumerable<Lazy<ICodeActionProvider, LanguageMetadata>> codeActionProviders,
        [ImportMany] IEnumerable<Lazy<IRenameProvider, LanguageMetadata>> renameProviders,
        [ImportMany] IEnumerable<Lazy<IDocumentFormattingProvider, LanguageMetadata>> documentFormattingProviders,
        [ImportMany] IEnumerable<Lazy<IRangeFormattingProvider, LanguageMetadata>> rangeFormattingProviders,
        [ImportMany] IEnumerable<Lazy<IDecorationProvider, LanguageMetadata>> decorationProviders,
        Lazy<IOutputService> output)
    {
        codeActions = Order(codeActionProviders);
        rename = Order(renameProviders);
        documentFormatting = Order(documentFormattingProviders);
        rangeFormatting = Order(rangeFormattingProviders);
        decorations = Order(decorationProviders);
        guardedDecorations = new GuardedDecorationProvider?[decorations.Length];
        this.output = output;
    }

    public async Task<IReadOnlyList<CodeAction>> GetCodeActionsAsync(CodeActionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var lists = await Task.WhenAll(Serving(codeActions, request.Document).Select(async provider =>
        {
            var actions = await SafeAsync(provider, () => provider.GetCodeActionsAsync(request, cancellationToken), []).ConfigureAwait(false);
            var source = SourceOf(provider);
            return actions.Select(action => action with { Provider = action.Provider ?? provider, Source = action.Source ?? source });
        })).ConfigureAwait(false);

        return [.. lists.SelectMany(actions => actions).OrderByDescending(action => action.IsPreferred).ThenBy(action => action.Kind)];
    }

    public async Task<CodeAction> ResolveCodeActionAsync(CodeAction action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action.Provider is not { } provider)
            return action;

        var resolved = await SafeAsync(provider, () => provider.ResolveAsync(action, cancellationToken), action).ConfigureAwait(false);
        return resolved with { Provider = provider, Source = action.Source };
    }

    public Task<RenameTarget?> PrepareRenameAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken) =>
        FirstAsync(rename, document, provider => provider.PrepareRenameAsync(document, snapshot, offset, cancellationToken));

    public Task<WorkspaceEdit?> RenameAsync(IDocument document, TextSnapshot snapshot, int offset, string newName, CancellationToken cancellationToken) =>
        FirstAsync(rename, document, provider => provider.RenameAsync(document, snapshot, offset, newName, cancellationToken));

    public Task<IReadOnlyList<TextChange>?> FormatDocumentAsync(IDocument document, TextSnapshot snapshot, FormattingOptions options, CancellationToken cancellationToken) =>
        FirstAsync(documentFormatting, document, provider => provider.FormatDocumentAsync(document, snapshot, options, cancellationToken));

    public Task<IReadOnlyList<TextChange>?> FormatRangeAsync(IDocument document, TextSnapshot snapshot, TextSpan span, FormattingOptions options,
        CancellationToken cancellationToken) =>
        FirstAsync(rangeFormatting, document, provider => provider.FormatRangeAsync(document, snapshot, span, options, cancellationToken));

    public IReadOnlyList<IDecorationProvider> GetDecorationProviders(IDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var serving = new List<IDecorationProvider>();
        for (var i = 0; i < decorations.Length; i++)
        {
            if (!decorations[i].Metadata.Serves(document.LanguageId))
                continue;

            if (guardedDecorations[i] is null)
            {
                IDecorationProvider provider;
                try
                {
                    provider = decorations[i].Value;
                }
                catch (Exception exception)
                {
                    Log(null, exception);
                    continue;
                }

                guardedDecorations[i] = new GuardedDecorationProvider(provider, this);
            }

            serving.Add(guardedDecorations[i]!);
        }

        return serving;
    }

    /// <summary>Gets the name of the plugin a provider comes from, or null for Runesmith's own.</summary>
    internal static string? SourceOf(object provider) => PluginCallers.Of(provider.GetType().Assembly)?.Manifest.Name;

    // Named languages first, then plugins before Runesmith; the sort is stable, so load order breaks ties.
    private static Lazy<T, LanguageMetadata>[] Order<T>(IEnumerable<Lazy<T, LanguageMetadata>> providers) =>
    [
        .. providers
            .Select((provider, index) => (provider, index))
            .OrderBy(entry => entry.provider.Metadata.LanguageIds.Contains(LanguagesAttribute.Any) ? 1 : 0)
            .ThenBy(entry => entry.index)
            .Select(entry => entry.provider),
    ];

    private async Task<TResult?> FirstAsync<T, TResult>(Lazy<T, LanguageMetadata>[] providers, IDocument document, Func<T, Task<TResult?>> ask)
        where T : class
        where TResult : class
    {
        foreach (var provider in Serving(providers, document))
        {
            if (await SafeAsync(provider, () => ask(provider), null).ConfigureAwait(false) is { } answer)
                return answer;
        }

        return null;
    }

    private IEnumerable<T> Serving<T>(Lazy<T, LanguageMetadata>[] providers, IDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var fromPlugins = new List<T>();
        var own = new List<T>();
        foreach (var provider in providers)
        {
            if (!provider.Metadata.Serves(document.LanguageId))
                continue;

            T value;
            try
            {
                value = provider.Value;
            }
            catch (Exception exception)
            {
                Log(null, exception);
                continue;
            }

            (value is not null && SourceOf(value) is not null ? fromPlugins : own).Add(value);
        }

        return fromPlugins.Concat(own);
    }

    private async Task<TResult> SafeAsync<TResult>(object provider, Func<Task<TResult>> action, TResult fallback)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or LanguageFeatureException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Log(provider, exception);
            return fallback;
        }
    }

    internal void Log(object? provider, Exception exception)
    {
        var who = provider is null ? "" : $"{provider.GetType().Name}{(SourceOf(provider) is { } plugin ? $" ({plugin})" : "")}: ";
        output.Value.GetChannel(ChannelName).AppendLine($"[{DateTime.Now:HH:mm:ss}] {who}{exception}");
    }

    /// <summary>A decoration provider whose failures are logged and answer nothing.</summary>
    private sealed class GuardedDecorationProvider : IDecorationProvider
    {
        private readonly IDecorationProvider provider;
        private readonly EditorFeatures owner;
        private EventHandler<DecorationsChangedEventArgs>? changed;

        public GuardedDecorationProvider(IDecorationProvider provider, EditorFeatures owner)
        {
            this.provider = provider;
            this.owner = owner;
            Source = SourceOf(provider);
        }

        public string? Source { get; }

        // Raised as the wrapper, since editors match the sender against the providers they were given.
        public event EventHandler<DecorationsChangedEventArgs>? Changed
        {
            add
            {
                if (changed is null)
                    provider.Changed += Forward;
                changed += value;
            }

            remove
            {
                changed -= value;
                if (changed is null)
                    provider.Changed -= Forward;
            }
        }

        public async Task<IReadOnlyList<Decoration>> GetDecorationsAsync(DecorationRequest request, CancellationToken cancellationToken)
        {
            try
            {
                return await provider.GetDecorationsAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                owner.Log(provider, exception);
                return [];
            }
        }

        private void Forward(object? sender, DecorationsChangedEventArgs e) => changed?.Invoke(this, e);

        public override string ToString() => provider.GetType().Name;
    }
}
