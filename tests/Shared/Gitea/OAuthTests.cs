using System.Net;
using System.Web;

namespace Runesmith.Plugins.Gitea.Tests;

/// <summary>The PKCE sign-in against a fake OAuth2 server: the browser is a local HTTP client that follows the authorize page's redirect to
/// the loopback listener, and the token endpoint checks the verifier against the challenge the authorize page received.</summary>
public sealed class OAuthTests : IDisposable
{
    private const string ClientId = "4c1d0e2f-runesmith-test";

    private readonly Harness harness = new();
    private readonly HttpClient browser = new(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false });
    private readonly FakeAuthorizationServer authorization = new();

    public OAuthTests() => authorization.Install(harness.Server);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => browser.Dispose();

    [Fact]
    public async Task SignsInWithPkceThroughTheLoopbackRedirect()
    {
        harness.Server.On("/api/v1/user", Responses.User);
        var host = harness.Host(login: null);
        using var flow = host.OAuth.Start(ClientId);
        var waiting = flow.WaitForCodeAsync(Token);

        var landing = await ApproveAsync(flow.AuthorizeUri);
        var code = await waiting;
        var user = await host.SignInWithCodeAsync(flow, code, Token);

        Assert.Equal(HttpStatusCode.OK, landing.StatusCode);
        Assert.Contains("Signed in", await landing.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
        Assert.Equal("alice", user.Login);
        Assert.Equal(SignInKind.OAuth, host.Kind);
        Assert.True(authorization.VerifierMatched);
        var exchange = harness.Server.Requests.Single(request => request.Uri.AbsolutePath == "/login/oauth/access_token");
        Assert.Equal("application/x-www-form-urlencoded", exchange.ContentType);
        var form = HttpUtility.ParseQueryString(exchange.Body);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal(ClientId, form["client_id"]);
        Assert.Null(form["client_secret"]);
        Assert.Equal(flow.RedirectUri.AbsoluteUri, form["redirect_uri"]);
        Assert.StartsWith("Bearer ", harness.Server.Requests.Last().Authorization, StringComparison.Ordinal);
        Assert.Contains("refresh_token", harness.Secrets.Secrets[harness.SecretKey("alice")], StringComparison.Ordinal);
    }

    [Fact]
    public void AsksForTheCodeWithAnS256ChallengeAndALoopbackAddress()
    {
        using var flow = harness.Host().OAuth.Start(ClientId);
        var query = HttpUtility.ParseQueryString(flow.AuthorizeUri.Query);

        Assert.Equal("https://" + Subject.Flavor.DefaultServer.Key + "/login/oauth/authorize", flow.AuthorizeUri.GetLeftPart(UriPartial.Path));
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal(OAuthClient.Challenge(flow.Verifier), query["code_challenge"]);
        Assert.InRange(flow.Verifier.Length, 43, 128);
        Assert.Equal("127.0.0.1", flow.RedirectUri.Host);
        Assert.NotEqual(80, flow.RedirectUri.Port);
        Assert.Equal(flow.RedirectUri.AbsoluteUri, query["redirect_uri"]);
        Assert.False(string.IsNullOrEmpty(query["state"]));
    }

    [Fact]
    public void ComputesTheChallengeOfRfc7636sExample() =>
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", OAuthClient.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));

    [Fact]
    public async Task IgnoresAnswersOfAnotherSignIn()
    {
        using var flow = harness.Host().OAuth.Start(ClientId);
        var waiting = flow.WaitForCodeAsync(Token);

        using var stray = await browser.GetAsync(new Uri(flow.RedirectUri, "?code=stolen&state=wrong"), Token);
        using var lost = await browser.GetAsync(new Uri(flow.RedirectUri, "favicon.ico"), Token);
        await ApproveAsync(flow.AuthorizeUri);

        Assert.Equal(HttpStatusCode.BadRequest, stray.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, lost.StatusCode);
        Assert.Equal(authorization.IssuedCode, await waiting);
    }

    [Fact]
    public async Task ADeniedApprovalSaysTheSignInWasCancelled()
    {
        using var flow = harness.Host().OAuth.Start(ClientId);
        var waiting = flow.WaitForCodeAsync(Token);
        var state = HttpUtility.ParseQueryString(flow.AuthorizeUri.Query)["state"];

        using var _ = await browser.GetAsync(new Uri(flow.RedirectUri, $"?error=access_denied&state={state}"), Token);

        var failure = await Assert.ThrowsAsync<OAuthException>(() => waiting);
        Assert.Equal(OAuthFailure.Denied, failure.Failure);
    }

    [Fact]
    public async Task CancellingStopsWaitingForTheBrowser()
    {
        using var flow = harness.Host().OAuth.Start(ClientId);
        using var cancel = new CancellationTokenSource();
        var waiting = flow.WaitForCodeAsync(cancel.Token);

        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }

    [Fact]
    public async Task AWrongVerifierIsRefused()
    {
        var host = harness.Host();
        using var flow = host.OAuth.Start(ClientId);
        var waiting = flow.WaitForCodeAsync(Token);
        await ApproveAsync(flow.AuthorizeUri);
        await waiting;

        var failure = await Assert.ThrowsAsync<OAuthException>(() => host.OAuth.ExchangeAsync(flow, "a-code-it-never-issued", Token));

        Assert.Equal(OAuthFailure.Refused, failure.Failure);
        Assert.False(host.IsSignedIn);
    }

    [Fact]
    public async Task RefreshesATokenAboutToExpireAndKeepsTheNewRefreshToken()
    {
        var settings = new Dictionary<string, object> { [Subject.Flavor.ClientIdsSetting] = Subject.Flavor.DefaultServer.Key + "=" + ClientId };
        var refreshing = new Harness(settings);
        refreshing.Server.On("/api/v1/user", Responses.User);
        var host = refreshing.Host();
        await refreshing.Secrets.SetAsync(refreshing.SecretKey("alice"),
            GiteaHost.Serialize(new StoredTokens(SignInKind.OAuth, "old-access", Harness.Start.AddMinutes(1), "old-refresh")), Token);
        refreshing.Server.On("/login/oauth/access_token", """{"access_token":"new-access","token_type":"bearer","expires_in":3600,"refresh_token":"new-refresh"}""");

        var token = await host.GetTokenAsync(Token);

        Assert.Equal("new-access", token?.Value);
        var form = HttpUtility.ParseQueryString(refreshing.Server.Requests.Single().Body);
        Assert.Equal("refresh_token", form["grant_type"]);
        Assert.Equal("old-refresh", form["refresh_token"]);
        Assert.Equal(ClientId, form["client_id"]);
        Assert.Contains("new-refresh", refreshing.Secrets.Secrets[refreshing.SecretKey("alice")], StringComparison.Ordinal);
        Assert.Equal("new-access", (await host.GetTokenAsync(Token))?.Value);
        Assert.Single(refreshing.Server.Requests);
    }

    [Fact]
    public async Task ARefusedRefreshSignsOutAndSaysSo()
    {
        var settings = new Dictionary<string, object> { [Subject.Flavor.ClientIdsSetting] = ClientId };
        var refreshing = new Harness(settings);
        var host = refreshing.Host();
        await refreshing.Secrets.SetAsync(refreshing.SecretKey("alice"),
            GiteaHost.Serialize(new StoredTokens(SignInKind.OAuth, "old-access", Harness.Start.AddMinutes(-5), "old-refresh")), Token);
        refreshing.Server.On("/login/oauth/access_token", Responses.InvalidGrant, HttpStatusCode.BadRequest);

        Assert.Null(await host.GetTokenAsync(Token));

        Assert.False(host.IsSignedIn);
        Assert.Empty(refreshing.Secrets.Secrets);
        var (kind, title, _) = Assert.Single(refreshing.Notifications.Shown);
        Assert.Equal(Runesmith.Sdk.Shell.NotificationKind.Warning, kind);
        Assert.StartsWith("Signed out of ", title, StringComparison.Ordinal);
    }

    private async Task<HttpResponseMessage> ApproveAsync(Uri authorizeUri)
    {
        var redirect = authorization.Authorize(authorizeUri);
        return await browser.GetAsync(redirect, Token);
    }

    /// <summary>Plays the server's side: the authorize page approves at once and remembers the challenge; the token endpoint issues tokens
    /// only for that code with a verifier that hashes to it.</summary>
    private sealed class FakeAuthorizationServer
    {
        private string? challenge;
        private string? redirectUri;

        public string IssuedCode { get; } = "code-" + Guid.NewGuid().ToString("N");

        public bool VerifierMatched { get; private set; }

        public Uri Authorize(Uri authorizeUri)
        {
            var query = HttpUtility.ParseQueryString(authorizeUri.Query);
            Assert.Equal("S256", query["code_challenge_method"]);
            challenge = query["code_challenge"];
            redirectUri = query["redirect_uri"];
            return new Uri($"{redirectUri}?code={IssuedCode}&state={Uri.EscapeDataString(query["state"]!)}");
        }

        public void Install(FakeServer server) => server.On(
            request => request.RequestUri!.AbsolutePath == "/login/oauth/access_token",
            (_, body) =>
            {
                var form = HttpUtility.ParseQueryString(body);
                var valid = form["grant_type"] == "authorization_code" && form["code"] == IssuedCode && form["redirect_uri"] == redirectUri
                    && form["code_verifier"] is { } verifier && OAuthClient.Challenge(verifier) == challenge;
                VerifierMatched |= valid;
                return valid
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Responses.Tokens, System.Text.Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent("""{"error":"invalid_grant","error_description":"failed PKCE code challenge"}""", System.Text.Encoding.UTF8, "application/json"),
                    };
            });
    }
}
