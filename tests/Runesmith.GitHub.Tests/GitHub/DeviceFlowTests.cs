using Runesmith.GitHub.GitHub;

namespace Runesmith.GitHub.Tests.GitHub;

public sealed class DeviceFlowTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeServer server = new();
    private readonly HttpClient http;
    private readonly ManualClock clock = new(Start);
    private readonly List<TimeSpan> waits = [];
    private readonly DeviceFlow flow;

    public DeviceFlowTests()
    {
        http = new HttpClient(server);
        flow = new DeviceFlow(http, clock, (delay, _) =>
        {
            waits.Add(delay);
            clock.Now += delay;
            return Task.CompletedTask;
        });
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AsksForACodeWithTheClientIdOnly()
    {
        server.On("/login/device/code", Recorded.DeviceCode);

        var code = await flow.StartAsync("Iv23liExample", Token);

        Assert.Equal("WDJB-MJHT", code.UserCode);
        Assert.Equal(new Uri("https://github.com/login/device"), code.VerificationUri);
        Assert.Equal(TimeSpan.FromSeconds(5), code.Interval);
        Assert.Equal(Start.AddMinutes(15), code.ExpiresAt);
        var request = Assert.Single(server.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("client_id=Iv23liExample", request.Body);
        Assert.Equal("application/json", request.Accept);
        Assert.Equal("Runesmith", request.UserAgent);
    }

    [Fact]
    public async Task PollsThroughPendingAndSlowDownUntilApproved()
    {
        server.Sequence("/login/oauth/access_token", Recorded.Pending, Recorded.SlowDown, Recorded.Pending, Recorded.Tokens);
        var code = new DeviceCode("device", "WDJB-MJHT", DeviceFlow.DefaultVerificationUri, TimeSpan.FromSeconds(5), Start.AddMinutes(15));

        var tokens = await flow.WaitForTokenAsync("Iv23liExample", code, Token);

        Assert.StartsWith("ghu_", tokens.AccessToken, StringComparison.Ordinal);
        Assert.StartsWith("ghr_", tokens.RefreshToken, StringComparison.Ordinal);
        Assert.Equal([5, 5, 10, 10], waits.Select(wait => wait.TotalSeconds));
        Assert.Equal(Start.AddSeconds(30 + 28800), tokens.ExpiresAt);
        Assert.Equal(Start.AddSeconds(30 + 15897600), tokens.RefreshExpiresAt);
        Assert.All(server.Requests, request => Assert.Equal(
            "client_id=Iv23liExample&device_code=device&grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Adevice_code", request.Body));
    }

    [Fact]
    public async Task SlowDownWithoutAnIntervalAddsFiveSeconds()
    {
        server.Sequence("/login/oauth/access_token", """{"error":"slow_down"}""", Recorded.Tokens);
        var code = new DeviceCode("device", "WDJB-MJHT", DeviceFlow.DefaultVerificationUri, TimeSpan.FromSeconds(5), Start.AddMinutes(15));

        await flow.WaitForTokenAsync("Iv23liExample", code, Token);

        Assert.Equal([5, 10], waits.Select(wait => wait.TotalSeconds));
    }

    [Fact]
    public async Task ATokenThatDoesNotExpireHasNoRefreshToken()
    {
        server.Sequence("/login/oauth/access_token", """{"access_token":"ghu_forever","token_type":"bearer","scope":""}""");
        var code = new DeviceCode("device", "WDJB-MJHT", DeviceFlow.DefaultVerificationUri, TimeSpan.FromSeconds(5), Start.AddMinutes(15));

        var tokens = await flow.WaitForTokenAsync("Iv23liExample", code, Token);

        Assert.Equal(new GitHubTokens("ghu_forever", null, null, null), tokens);
    }

    [Theory]
    [InlineData(Recorded.ExpiredToken, nameof(DeviceFlowFailure.Expired))]
    [InlineData(Recorded.AccessDenied, nameof(DeviceFlowFailure.Denied))]
    [InlineData("""{"error":"device_flow_disabled"}""", nameof(DeviceFlowFailure.Disabled))]
    [InlineData("""{"error":"incorrect_client_credentials"}""", nameof(DeviceFlowFailure.UnknownClient))]
    public async Task StopsWhenGitHubSaysTheSignInCannotFinish(string answer, string failure)
    {
        server.Sequence("/login/oauth/access_token", answer);
        var code = new DeviceCode("device", "WDJB-MJHT", DeviceFlow.DefaultVerificationUri, TimeSpan.FromSeconds(5), Start.AddMinutes(15));

        var exception = await Assert.ThrowsAsync<DeviceFlowException>(() => flow.WaitForTokenAsync("Iv23liExample", code, Token));

        Assert.Equal(failure, exception.Failure.ToString());
    }

    [Fact]
    public async Task StopsPollingOnceTheCodeHasExpired()
    {
        server.Sequence("/login/oauth/access_token", Recorded.Pending);
        var code = new DeviceCode("device", "WDJB-MJHT", DeviceFlow.DefaultVerificationUri, TimeSpan.FromSeconds(5), Start.AddSeconds(12));

        var exception = await Assert.ThrowsAsync<DeviceFlowException>(() => flow.WaitForTokenAsync("Iv23liExample", code, Token));

        Assert.Equal(DeviceFlowFailure.Expired, exception.Failure);
        Assert.Equal(2, server.Requests.Count);
    }

    [Fact]
    public async Task RefreshesWithoutAClientSecret()
    {
        server.On("/login/oauth/access_token", Recorded.Tokens);

        var tokens = await flow.RefreshAsync("Iv23liExample", "ghr_old", Token);

        Assert.StartsWith("ghu_", tokens.AccessToken, StringComparison.Ordinal);
        var body = Assert.Single(server.Requests).Body;
        Assert.Equal("client_id=Iv23liExample&grant_type=refresh_token&refresh_token=ghr_old", body);
        Assert.DoesNotContain("client_secret", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARefusedRefreshTokenMeansSigningInAgain()
    {
        server.On("/login/oauth/access_token", Recorded.BadRefreshToken);

        var exception = await Assert.ThrowsAsync<DeviceFlowException>(() => flow.RefreshAsync("Iv23liExample", "ghr_old", Token));

        Assert.Equal(DeviceFlowFailure.RefreshRefused, exception.Failure);
        Assert.DoesNotContain("ghr_old", exception.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        http.Dispose();
        server.Dispose();
    }
}
