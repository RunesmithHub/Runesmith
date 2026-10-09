using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Runesmith.Plugins.Gitea;

/// <summary>An OAuth2 access token, when it expires, and the refresh token that renews it.</summary>
internal sealed record OAuthTokens(string AccessToken, DateTimeOffset? ExpiresAt, string? RefreshToken);

/// <summary>Why an OAuth2 sign-in or refresh did not work.</summary>
internal enum OAuthFailure
{
    /// <summary>The user did not approve Runesmith on the server.</summary>
    Denied,

    /// <summary>The server does not know the client id, or the application's redirect URI does not match.</summary>
    UnknownClient,

    /// <summary>The server refused the code or the refresh token, so the user has to sign in again.</summary>
    Refused,

    /// <summary>The server could not be reached, or the local port for the answer could not be opened.</summary>
    Network,

    /// <summary>The server answered in a way the flow does not expect.</summary>
    Other,
}

/// <summary>A failed step of an OAuth2 sign-in, with a message for people; it never carries a code or token.</summary>
internal sealed class OAuthException(OAuthFailure failure, string message, Exception? inner = null) : Exception(message, inner)
{
    public OAuthFailure Failure { get; } = failure;
}

/// <summary>Signs in with OAuth2's authorization code flow for public clients: PKCE (S256) and a loopback redirect to a port of
/// <c>127.0.0.1</c> chosen for each sign-in (RFC 8252), so no client secret is needed. Also refreshes the tokens it gets.</summary>
/// <param name="http">The client to reach the token endpoint with.</param>
/// <param name="server">The server.</param>
/// <param name="serverName">The server's name in messages, such as "Codeberg".</param>
/// <param name="time">The clock tokens expire by.</param>
internal sealed class OAuthClient(HttpClient http, GiteaServer server, string serverName, TimeProvider? time = null)
{
    /// <summary>The path of the redirect URI; the server's application registers <c>http://127.0.0.1/</c>, and the server ignores
    /// the port of loopback redirect URIs for public clients.</summary>
    public const string CallbackPath = "/";

    private readonly TimeProvider time = time ?? TimeProvider.System;

    public Uri AuthorizeEndpoint => new(server.BaseUri, "login/oauth/authorize");

    public Uri TokenEndpoint => new(server.BaseUri, "login/oauth/access_token");

    /// <summary>Starts listening on a free loopback port for the server's answer, and builds the address that asks the user to approve.</summary>
    /// <exception cref="OAuthException">No loopback port could be opened.</exception>
    public LoopbackAuthorization Start(string clientId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        var verifier = RandomText(48);
        var state = RandomText(24);
        var listener = LoopbackListener.Open();
        var redirect = new Uri(listener.Prefix, CallbackPath.TrimStart('/'));
        var query = Query(
        [
            new("client_id", clientId),
            new("redirect_uri", redirect.AbsoluteUri),
            new("response_type", "code"),
            new("state", state),
            new("code_challenge", Challenge(verifier)),
            new("code_challenge_method", "S256"),
        ]);
        return new LoopbackAuthorization(listener, new Uri(AuthorizeEndpoint.AbsoluteUri + "?" + query), redirect, state, verifier, clientId, serverName);
    }

    /// <summary>Exchanges the code the server sent for tokens, proving with the verifier that this is the app that asked.</summary>
    public async Task<OAuthTokens> ExchangeAsync(LoopbackAuthorization authorization, string code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        var response = await PostAsync(
        [
            new("grant_type", "authorization_code"),
            new("client_id", authorization.ClientId),
            new("code", code),
            new("code_verifier", authorization.Verifier),
            new("redirect_uri", authorization.RedirectUri.AbsoluteUri),
        ], cancellationToken).ConfigureAwait(false);
        return response is { AccessToken.Length: > 0 } ? ToTokens(response)
            : response?.Error == "unauthorized_client" ? throw UnknownClient()
            : throw new OAuthException(OAuthFailure.Refused, response?.ErrorDescription is { Length: > 0 } description
                ? $"{serverName} did not finish the sign-in: {description.TrimEnd('.')}. Try again."
                : $"{serverName} did not finish the sign-in. Try again.");
    }

    /// <summary>Exchanges a refresh token for new tokens; the server may replace the refresh token too.</summary>
    public async Task<OAuthTokens> RefreshAsync(string clientId, string refreshToken, CancellationToken cancellationToken)
    {
        var response = await PostAsync(
        [
            new("grant_type", "refresh_token"),
            new("client_id", clientId),
            new("refresh_token", refreshToken),
        ], cancellationToken).ConfigureAwait(false);
        return response is { AccessToken.Length: > 0 }
            ? ToTokens(response)
            : throw new OAuthException(OAuthFailure.Refused, $"The {serverName} sign-in expired. Sign in to {serverName} again.");
    }

    /// <summary>Gets the S256 code challenge of a verifier: its SHA-256 hash in base64url without padding.</summary>
    internal static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private OAuthException UnknownClient() => new(OAuthFailure.UnknownClient,
        $"{serverName} does not know the OAuth2 application in the client ID setting, or its redirect URI is not http://127.0.0.1{CallbackPath}.");

    private OAuthTokens ToTokens(OAuthTokenResponse response) => new(response.AccessToken!,
        response.ExpiresIn is > 0 ? time.GetUtcNow().AddSeconds(response.ExpiresIn.Value) : null,
        response.RefreshToken is { Length: > 0 } ? response.RefreshToken : null);

    private async Task<OAuthTokenResponse?> PostAsync(IEnumerable<KeyValuePair<string, string>> form, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint) { Content = new FormUrlEncodedContent(form) };
        GiteaClient.AddCommonHeaders(request);
        try
        {
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode >= 500)
                throw new OAuthException(OAuthFailure.Network, $"{serverName} could not finish the sign-in. Try again in a moment.");

            // The token endpoint answers its errors with 400 and a JSON body that names them.
            return await response.Content.ReadFromJsonAsync(GiteaJson.Default.OAuthTokenResponse, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new OAuthException(OAuthFailure.Network, $"{serverName} could not be reached. Check the connection and try again.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OAuthException(OAuthFailure.Network, $"{serverName} took too long to answer. Try again in a moment.", exception);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new OAuthException(OAuthFailure.Other, $"{serverName} sent an answer Runesmith does not understand.", exception);
        }
    }

    private static string Query(IEnumerable<KeyValuePair<string, string>> parameters) =>
        string.Join('&', parameters.Select(parameter => Uri.EscapeDataString(parameter.Key) + "=" + Uri.EscapeDataString(parameter.Value)));

    private static string RandomText(int bytes) => Base64Url(RandomNumberGenerator.GetBytes(bytes));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>A sign-in waiting for the user to approve Runesmith in the browser, and the loopback listener that receives the server's answer.</summary>
internal sealed class LoopbackAuthorization : IDisposable
{
    private readonly LoopbackListener listener;
    private readonly string state;
    private readonly string serverName;

    internal LoopbackAuthorization(LoopbackListener listener, Uri authorizeUri, Uri redirectUri, string state, string verifier, string clientId, string serverName)
    {
        this.listener = listener;
        this.state = state;
        this.serverName = serverName;
        AuthorizeUri = authorizeUri;
        RedirectUri = redirectUri;
        Verifier = verifier;
        ClientId = clientId;
    }

    /// <summary>Gets the page that asks the user to approve Runesmith; open it in the browser.</summary>
    public Uri AuthorizeUri { get; }

    /// <summary>Gets the loopback address the server sends the user back to.</summary>
    public Uri RedirectUri { get; }

    internal string Verifier { get; }

    internal string ClientId { get; }

    /// <summary>Waits until the browser comes back with the server's answer, and returns the code; answers that do not carry this sign-in's
    /// state are ignored.</summary>
    /// <exception cref="OAuthException">The user did not approve, or the server reported an error.</exception>
    public async Task<string> WaitForCodeAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var context = await listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
            var request = context.Request;
            if (!string.Equals(request.Url?.AbsolutePath, OAuthClient.CallbackPath, StringComparison.Ordinal))
            {
                await RespondAsync(context, HttpStatusCode.NotFound, "Not found", "This address is only for signing in.").ConfigureAwait(false);
                continue;
            }

            var query = request.QueryString;
            if (!string.Equals(query["state"], state, StringComparison.Ordinal))
            {
                await RespondAsync(context, HttpStatusCode.BadRequest, "Not this sign-in",
                    "This answer belongs to another sign-in. Use the newest browser tab Runesmith opened.").ConfigureAwait(false);
                continue;
            }

            if (query["code"] is { Length: > 0 } code)
            {
                await RespondAsync(context, HttpStatusCode.OK, "Signed in",
                    "Runesmith received the approval. You can close this tab and go back to Runesmith.").ConfigureAwait(false);
                return code;
            }

            var error = query["error"];
            await RespondAsync(context, HttpStatusCode.OK, "Not signed in", "Runesmith was not approved. You can close this tab.").ConfigureAwait(false);
            throw error switch
            {
                "access_denied" => new OAuthException(OAuthFailure.Denied, $"The sign-in was cancelled on {serverName}."),
                "unauthorized_client" or "invalid_client" or "invalid_request" => new OAuthException(OAuthFailure.UnknownClient,
                    $"{serverName} refused the OAuth2 application: check its client ID and that its redirect URI is http://127.0.0.1{OAuthClient.CallbackPath}."),
                _ => new OAuthException(OAuthFailure.Other, query["error_description"] is { Length: > 0 } description
                    ? $"{serverName} did not finish the sign-in: {description.TrimEnd('.')}."
                    : $"{serverName} did not finish the sign-in. Try again."),
            };
        }
    }

    public void Dispose() => listener.Dispose();

    private static async Task RespondAsync(HttpListenerContext context, HttpStatusCode status, string title, string message)
    {
        var html = $$"""
            <!doctype html><html><head><meta charset="utf-8"><title>{{title}} - Runesmith</title>
            <style>body{font-family:system-ui,sans-serif;margin:15vh auto;max-width:28rem;color:#333;text-align:center}h1{font-size:1.4rem}</style>
            </head><body><h1>{{title}}</h1><p>{{message}}</p></body></html>
            """;
        var bytes = Encoding.UTF8.GetBytes(html);
        try
        {
            context.Response.StatusCode = (int)status;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.Headers["Cache-Control"] = "no-store";
            await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            context.Response.Close();
        }
        catch (Exception exception) when (exception is HttpListenerException or IOException or ObjectDisposedException)
        {
        }
    }
}

/// <summary>An <see cref="HttpListener"/> on a free port of <c>127.0.0.1</c>, never on other addresses.</summary>
internal sealed class LoopbackListener : IDisposable
{
    private const int Attempts = 5;

    private readonly HttpListener listener;

    private LoopbackListener(HttpListener listener, Uri prefix)
    {
        this.listener = listener;
        Prefix = prefix;
    }

    /// <summary>Gets the listener's address, such as <c>http://127.0.0.1:53124/</c>.</summary>
    public Uri Prefix { get; }

    /// <summary>Opens a listener on a free loopback port.</summary>
    /// <exception cref="OAuthException">No port could be opened.</exception>
    public static LoopbackListener Open()
    {
        for (var attempt = 1; ; attempt++)
        {
            var prefix = new Uri($"http://127.0.0.1:{FreePort()}/");
            var listener = new HttpListener();
            listener.Prefixes.Add(prefix.AbsoluteUri);
            try
            {
                listener.Start();
                return new LoopbackListener(listener, prefix);
            }
            catch (HttpListenerException exception)
            {
                listener.Close();
                // Another program can take the port between finding it free and listening on it.
                if (attempt == Attempts)
                    throw new OAuthException(OAuthFailure.Network, "Runesmith could not open a local port for the browser's answer.", exception);
            }
        }
    }

    /// <summary>Waits for the next request.</summary>
    public async Task<HttpListenerContext> AcceptAsync(CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(listener.Stop);
        try
        {
            return await listener.GetContextAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException
            && cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    public void Dispose() => listener.Close();

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }
}
