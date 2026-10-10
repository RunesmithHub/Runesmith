using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Shell;

namespace Runesmith.Languages.Features;

/// <summary>Writes the failures of language feature providers to the "Language features" output channel, with the provider's plugin.</summary>
internal sealed class FeatureLog(Lazy<IOutputService> output)
{
    private const string ChannelName = "Language features";

    public void Write(object? provider, Exception exception)
    {
        var who = provider is null ? "" : $"{provider.GetType().Name}{(EditorFeatures.SourceOf(provider) is { } plugin ? $" ({plugin})" : "")}: ";
        output.Value.GetChannel(ChannelName).AppendLine($"[{DateTime.Now:HH:mm:ss}] {who}{exception}");
    }

    /// <summary>Runs a provider's request; a failure is logged and answers the fallback, while cancellation and refusals pass through.</summary>
    public async Task<TResult> SafeAsync<TResult>(object provider, Func<Task<TResult>> action, TResult fallback)
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
            Write(provider, exception);
            return fallback;
        }
    }
}

/// <summary>The exports of one kind of provider, in the order they are asked: named languages before every language, then plugins before
/// Runesmith's own, then load order.</summary>
/// <param name="created">Runs once for each provider when it is first created, such as to follow its events.</param>
internal sealed class ProviderList<T>(IEnumerable<Lazy<T, LanguageMetadata>> exports, FeatureLog log, Action<T>? created = null)
    where T : class
{
    private readonly Lazy<T, LanguageMetadata>[] providers =
    [
        .. exports
            .Select((provider, index) => (provider, index))
            .OrderBy(entry => entry.provider.Metadata.LanguageIds.Contains(LanguagesAttribute.Any) ? 1 : 0)
            .ThenBy(entry => entry.index)
            .Select(entry => entry.provider),
    ];

    private readonly HashSet<T> seen = new(ReferenceEqualityComparer.Instance);

    /// <summary>Gets whether a provider serves the document's language, without creating any.</summary>
    public bool Serves(IDocument document) => providers.Any(provider => provider.Metadata.Serves(document.LanguageId));

    /// <summary>Gets the providers of the document's language, creating them; one that cannot be created is logged and left out.</summary>
    public IReadOnlyList<T> Serving(IDocument document)
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
                log.Write(null, exception);
                continue;
            }

            Created(value);
            (EditorFeatures.SourceOf(value) is not null ? fromPlugins : own).Add(value);
        }

        return [.. fromPlugins, .. own];
    }

    /// <summary>Asks the providers of a document in turn and returns the first answer that is not null.</summary>
    public async Task<TResult?> FirstAsync<TResult>(IDocument document, Func<T, Task<TResult?>> ask)
        where TResult : class
    {
        foreach (var provider in Serving(document))
        {
            if (await log.SafeAsync(provider, () => ask(provider), null).ConfigureAwait(false) is { } answer)
                return answer;
        }

        return null;
    }

    private void Created(T provider)
    {
        if (created is null)
            return;

        lock (seen)
        {
            if (!seen.Add(provider))
                return;
        }

        created(provider);
    }
}
