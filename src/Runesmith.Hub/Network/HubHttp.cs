using System.Net;
using System.Net.Http.Headers;

namespace Runesmith.Hub.Network;

/// <summary>The HTTP client the hub client uses: plain requests for public files, a user agent naming Runesmith and its version, at most five
/// redirects, and nothing that identifies the user.</summary>
public static class HubHttp
{
    /// <summary>Creates the client; the caller keeps it for the life of the hub client.</summary>
    public static HttpClient Create(string runesmithVersion)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(15),
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(2) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Runesmith", runesmithVersion));
        return client;
    }

    /// <summary>Gets whether Runesmith may download from an address: HTTPS, or plain HTTP to this computer for development.</summary>
    public static bool IsAllowed(Uri url) =>
        url is { IsAbsoluteUri: true } && (url.Scheme == Uri.UriSchemeHttps || (url.Scheme == Uri.UriSchemeHttp && url.IsLoopback));
}
