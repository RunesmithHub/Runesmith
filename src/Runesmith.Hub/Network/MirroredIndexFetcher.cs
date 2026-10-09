using RunesmithHub.Protocol.Updating;

namespace Runesmith.Hub.Network;

/// <summary>Gets index files from the first host that answers; every file is verified the same whichever host served it.</summary>
public sealed class MirroredIndexFetcher(IReadOnlyList<IIndexFetcher> hosts) : IIndexFetcher
{
    private int preferred;

    public async Task<FetchResult> FetchAsync(string path, long maxBytes, CancellationToken cancellationToken)
    {
        if (hosts.Count == 0)
            throw new IndexUnavailableException("No index address is configured.");

        IndexUnavailableException? last = null;
        for (var attempt = 0; attempt < hosts.Count; attempt++)
        {
            var index = (preferred + attempt) % hosts.Count;
            try
            {
                var result = await hosts[index].FetchAsync(path, maxBytes, cancellationToken).ConfigureAwait(false);
                preferred = index;
                return result;
            }
            catch (IndexUnavailableException exception)
            {
                last = exception;
            }
        }

        throw last!;
    }
}
