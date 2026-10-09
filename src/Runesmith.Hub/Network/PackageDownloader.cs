using System.Net;

namespace Runesmith.Hub.Network;

/// <summary>Downloads a file, reading no more than its expected size.</summary>
public interface IFileDownloader
{
    /// <summary>Writes the file at an address to a stream.</summary>
    /// <returns>The number of bytes written.</returns>
    /// <exception cref="DownloadException">The file cannot be downloaded or is larger than <paramref name="maxBytes"/>.</exception>
    Task<long> DownloadAsync(Uri url, long maxBytes, Stream destination, CancellationToken cancellationToken);
}

/// <summary>A download failed: the host did not answer, refused, or sent more than expected.</summary>
public sealed class DownloadException : Exception
{
    public DownloadException()
    {
    }

    public DownloadException(string message)
        : base(message)
    {
    }

    public DownloadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Downloads over HTTPS, or plain HTTP to this computer for development.</summary>
public sealed class HttpFileDownloader(HttpClient client) : IFileDownloader
{
    public async Task<long> DownloadAsync(Uri url, long maxBytes, Stream destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(destination);
        if (!HubHttp.IsAllowed(url))
            throw new DownloadException($"{url} does not use HTTPS.");

        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
                throw new DownloadException($"{url} answered {(int)response.StatusCode}.");
            if (response.Content.Headers.ContentLength > maxBytes)
                throw new DownloadException($"{url} is larger than expected.");

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > maxBytes)
                    throw new DownloadException($"{url} is larger than expected.");
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            return total;
        }
        catch (HttpRequestException exception)
        {
            throw new DownloadException($"{url} could not be reached: {exception.Message}", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DownloadException($"{url} did not answer in time.", exception);
        }
    }
}
