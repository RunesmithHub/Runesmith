using System.Net.Http.Json;
using System.Text.Json;

namespace Runesmith.Plugins.Gitea;

/// <summary>What a server said about itself.</summary>
/// <param name="Kind">Whether it is a Forgejo or a Gitea server, or neither.</param>
/// <param name="Version">The version it reported, such as <c>13.0.2</c>.</param>
internal sealed record ServerInfo(ServerKind Kind, string? Version);

/// <summary>Tells Forgejo and Gitea servers apart through their version endpoints, which answer without a token.</summary>
internal static class ServerProbe
{
    /// <summary>Asks a server what it is: Forgejo answers <c>/api/forgejo/v1/version</c>, Gitea only <c>/api/v1/version</c>.</summary>
    /// <exception cref="GiteaException">The server could not be reached.</exception>
    public static async Task<ServerInfo> ProbeAsync(HttpClient http, GiteaServer server, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(server);
        if (await GetVersionAsync(http, server, new Uri(server.BaseUri, "api/forgejo/v1/version"), cancellationToken).ConfigureAwait(false) is { } forgejo)
            return new ServerInfo(ServerKind.Forgejo, forgejo);

        if (await GetVersionAsync(http, server, new Uri(server.BaseUri, "api/v1/version"), cancellationToken).ConfigureAwait(false) is not { } version)
            return new ServerInfo(ServerKind.Unknown, null);

        // Forgejo servers that predate their own endpoint report a version such as 1.21.11-0+gitea-1.21.11.
        return new ServerInfo(version.Contains("+gitea-", StringComparison.OrdinalIgnoreCase) ? ServerKind.Forgejo : ServerKind.Gitea, version);
    }

    private static async Task<string?> GetVersionAsync(HttpClient http, GiteaServer server, Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        GiteaClient.AddCommonHeaders(request);
        try
        {
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var answer = await response.Content.ReadFromJsonAsync(GiteaJson.Default.GiteaVersion, cancellationToken).ConfigureAwait(false);
            return answer?.Version is { Length: > 0 } version ? version : null;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return null;
        }
        catch (HttpRequestException exception)
        {
            throw new GiteaException(GiteaFailure.Network, $"{server.Key} could not be reached. Check the address and the connection.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GiteaException(GiteaFailure.Network, $"{server.Key} took too long to answer. Check the address and try again.", exception);
        }
    }
}
