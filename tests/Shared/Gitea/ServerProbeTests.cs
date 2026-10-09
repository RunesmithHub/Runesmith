using System.Net;

namespace Runesmith.Plugins.Gitea.Tests;

public sealed class ServerProbeTests : IDisposable
{
    private static readonly GiteaServer Server = GiteaServer.Of("git.example.com/forge");

    private readonly FakeServer server = new();
    private readonly HttpClient http;

    public ServerProbeTests() => http = new HttpClient(server);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => http.Dispose();

    [Fact]
    public async Task AServerThatAnswersForgejosEndpointIsForgejo()
    {
        server.On("/forge/api/forgejo/v1/version", Responses.Read("version-forgejo-codeberg.json"));
        server.On("/forge/api/v1/version", Responses.Read("version-codeberg.json"));

        var info = await ServerProbe.ProbeAsync(http, Server, Token);

        Assert.Equal(new ServerInfo(ServerKind.Forgejo, "16.0.0-dev-753-6bcc6da0+gitea-1.22.0"), info);
        Assert.Equal("Runesmith", server.Requests[0].UserAgent);
        Assert.Null(server.Requests[0].Authorization);
    }

    [Fact]
    public async Task AServerThatAnswersOnlyTheApiVersionIsGitea()
    {
        server.On("/forge/api/v1/version", Responses.Read("version-gitea.json"));

        var info = await ServerProbe.ProbeAsync(http, Server, Token);

        Assert.Equal(new ServerInfo(ServerKind.Gitea, "1.27.0+dev-1118-ge629c4fdc2"), info);
    }

    [Fact]
    public async Task AnOlderForgejoIsKnownByItsVersion()
    {
        server.On("/forge/api/v1/version", """{"version":"1.21.11-1+gitea-1.21.11"}""");

        Assert.Equal(ServerKind.Forgejo, (await ServerProbe.ProbeAsync(http, Server, Token)).Kind);
    }

    [Theory]
    [InlineData("<html>Welcome</html>", HttpStatusCode.OK)]
    [InlineData("""{"message":"not found"}""", HttpStatusCode.NotFound)]
    [InlineData("""{"version":""}""", HttpStatusCode.OK)]
    public async Task AnythingElseIsUnknown(string body, HttpStatusCode status)
    {
        server.On("/forge/api/v1/version", body, status);

        Assert.Equal(ServerKind.Unknown, (await ServerProbe.ProbeAsync(http, Server, Token)).Kind);
    }

    [Fact]
    public async Task AnUnreachableServerSaysSo()
    {
        server.Unreachable = true;

        var failure = await Assert.ThrowsAsync<GiteaException>(() => ServerProbe.ProbeAsync(http, Server, Token));

        Assert.Equal(GiteaFailure.Network, failure.Failure);
        Assert.Contains("git.example.com/forge", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TakesOnlyItsOwnKindOfServer()
    {
        var flavor = Subject.Flavor;

        Assert.Null(AddAccountDialog.Problem(flavor, Server, flavor.Kind));
        Assert.Contains("Add it in Settings", AddAccountDialog.Problem(flavor, Server, Subject.OtherKind), StringComparison.Ordinal);
        Assert.Equal($"No {flavor.Name} server answers at git.example.com/forge. Check the address.", AddAccountDialog.Problem(flavor, Server, ServerKind.Unknown));
    }
}
