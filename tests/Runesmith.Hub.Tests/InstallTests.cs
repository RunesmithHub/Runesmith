using System.Net;
using System.Net.Sockets;
using Runesmith.Hub.Enforcement;
using Runesmith.Hub.Installing;
using Runesmith.Hub.Network;
using Runesmith.Tests.Hub;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Resolution;
using RunesmithHub.Protocol.Updating;

namespace Runesmith.Hub.Tests;

public sealed class InstallTests : IDisposable
{
    private readonly HubFixture fixture = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => fixture.Dispose();

    [Fact]
    public async Task InstallingAPluginInstallsWhatItNeedsAndTakesEffectAtTheNextStart()
    {
        var client = fixture.CreateClient();
        await client.RefreshAsync(Token);

        var result = client.Resolve(ResolutionRequest.Install("lumen.todo"), HubPreferences.Default);

        Assert.True(result.Succeeded);
        var changes = result.Plan.Changes.ToList();
        Assert.Equal(
            [("harbor.issues", "1.3.2", PluginTier.Verified), ("runesmith.git", "0.3.4", PluginTier.Official), ("lumen.todo", "1.0.0", PluginTier.Unverified)],
            changes.OrderBy(op => op.PluginId == "lumen.todo").ThenBy(op => op.PluginId, StringComparer.Ordinal).Select(op => (op.PluginId, op.To!.ToString(), op.Tier!.Value)));
        Assert.All(changes, op => Assert.Equal(OperationKind.Install, op.Kind));
        Assert.Equal(OperationReason.Requested, result.Plan.Find("lumen.todo")!.Reason);
        Assert.Equal(OperationReason.Dependency, result.Plan.Find("harbor.issues")!.Reason);

        var steps = new List<InstallStep>();
        await client.ApplyAsync(result.Plan, new SyncProgress(p => steps.Add(p.Step)), Token);

        Assert.Equal(InstallStep.Done, steps[^1]);
        Assert.Equal(["harbor.issues", "lumen.todo", "runesmith.git"], client.State.Plugins.Select(p => p.Id).Order(StringComparer.Ordinal));
        Assert.True(client.State.Find("lumen.todo")!.Requested);
        Assert.False(client.State.Find("harbor.issues")!.Requested);
        Assert.True(client.State.IsPending("lumen.todo"));
        Assert.False(Directory.Exists(fixture.Paths.PluginFolder("lumen.todo")));

        var report = HubStartup.Run(fixture.Configuration, fixture.Paths, hubEnabled: true, fixture.Time);

        Assert.Empty(report.Problems);
        Assert.Empty(report.Withheld);
        Assert.Equal(["harbor.issues", "lumen.todo", "runesmith.git"], report.HubPlugins.Order(StringComparer.Ordinal));
        Assert.True(File.Exists(Path.Combine(fixture.Paths.PluginFolder("lumen.todo"), "plugin.json")));
        Assert.True(File.Exists(Path.Combine(fixture.Paths.PluginFolder("lumen.todo"), "lib", "Lumen.Todo.dll")));
        Assert.False(Directory.Exists(fixture.Paths.Staging));
        Assert.Null(fixture.CreateClient().State.Pending);
    }

    [Fact]
    public async Task ATamperedPackageIsRejectedAndNothingChanges()
    {
        var index = HubFixture.CopyIndex();
        using var tampered = new HubFixture(index);
        var package = Path.Combine(index, "files", "ember.themes", "3.0.1", "ember.themes-3.0.1.rsplugin");
        var bytes = File.ReadAllBytes(package);
        bytes[bytes.Length / 2] ^= 0x5A;
        File.WriteAllBytes(package, bytes);
        var client = tampered.CreateClient();
        await client.RefreshAsync(Token);
        var plan = client.Resolve(ResolutionRequest.Install("ember.themes"), HubPreferences.Default).Plan!;

        var failure = await Assert.ThrowsAsync<InstallException>(() => client.ApplyAsync(plan, cancellationToken: Token));

        Assert.Contains("did not match what the hub published", failure.Message, StringComparison.Ordinal);
        Assert.Empty(client.State.Plugins);
        Assert.False(File.Exists(tampered.Paths.StateFile));
        Assert.Empty(Directory.Exists(tampered.Paths.Staging) ? Directory.EnumerateFileSystemEntries(tampered.Paths.Staging) : []);
    }

    [Fact]
    public async Task ADownloadThatFailsIsRetriedOnceAndThenReported()
    {
        var downloader = new FailingDownloader();
        var client = fixture.CreateClient(downloader);
        await client.RefreshAsync(Token);
        var plan = client.Resolve(ResolutionRequest.Install("ember.themes"), HubPreferences.Default).Plan!;

        var failure = await Assert.ThrowsAsync<InstallException>(() => client.ApplyAsync(plan, cancellationToken: Token));

        Assert.Equal(2, downloader.Attempts);
        Assert.Contains("Ember Themes 3.0.1 could not be downloaded", failure.Message, StringComparison.Ordinal);
        Assert.Empty(client.State.Plugins);
    }

    [Fact]
    public async Task RemovingAPluginTakesItsFolderAwayAtTheNextStart()
    {
        var client = fixture.CreateClient();
        await client.RefreshAsync(Token);
        await client.ApplyAsync(client.Resolve(ResolutionRequest.Install("ember.themes"), HubPreferences.Default).Plan!, cancellationToken: Token);
        HubStartup.Run(fixture.Configuration, fixture.Paths, hubEnabled: true, fixture.Time);
        client = fixture.CreateClient();
        await client.RefreshAsync(Token);

        await client.ApplyAsync(client.Resolve(ResolutionRequest.Remove("ember.themes"), HubPreferences.Default).Plan!, cancellationToken: Token);

        Assert.Empty(client.State.Plugins);
        Assert.True(Directory.Exists(fixture.Paths.PluginFolder("ember.themes")));
        HubStartup.Run(fixture.Configuration, fixture.Paths, hubEnabled: true, fixture.Time);
        Assert.False(Directory.Exists(fixture.Paths.PluginFolder("ember.themes")));
    }

    [Fact]
    public async Task TurningTheHubOffKeepsItsPluginsFromLoading()
    {
        var client = fixture.CreateClient();
        await client.RefreshAsync(Token);
        await client.ApplyAsync(client.Resolve(ResolutionRequest.Install("ember.themes"), HubPreferences.Default).Plan!, cancellationToken: Token);

        var report = HubStartup.Run(fixture.Configuration, fixture.Paths, hubEnabled: false, fixture.Time);

        Assert.Contains("turned off", report.Withheld["ember.themes"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheIndexAndPackagesComeOverHttpWithinTheirSizes()
    {
        using var server = new FixtureServer(fixture.Index);
        using var http = HubHttp.Create("0.1.0");
        var downloader = new RedirectingDownloader(new HttpFileDownloader(http), HubFixture.BaseUrl, server.BaseUrl);
        var client = fixture.CreateClient(downloader, url => new HttpIndexFetcher(http, url == HubFixture.BaseUrl ? server.BaseUrl : url));

        Assert.Equal(HubStatusKind.Verified, (await client.RefreshAsync(Token)).Kind);
        await client.ApplyAsync(client.Resolve(ResolutionRequest.Install("tide.formatter"), HubPreferences.Default).Plan!, cancellationToken: Token);

        Assert.Equal("1.8.0", client.State.Find("tide.formatter")!.Version);
        Assert.All(server.UserAgents, agent => Assert.StartsWith("Runesmith/0.1.0", agent, StringComparison.Ordinal));
        await Assert.ThrowsAsync<DownloadException>(() => new HttpFileDownloader(http).DownloadAsync(new Uri(server.BaseUrl, "timestamp.json"), 10, Stream.Null, Token));
        await Assert.ThrowsAsync<DownloadException>(() => new HttpFileDownloader(http).DownloadAsync(new Uri("http://example.com/x.rsplugin"), 10, Stream.Null, Token));
    }

    private sealed class SyncProgress(Action<InstallProgress> report) : IProgress<InstallProgress>
    {
        public void Report(InstallProgress value) => report(value);
    }

    private sealed class FailingDownloader : IFileDownloader
    {
        public int Attempts { get; private set; }

        public Task<long> DownloadAsync(Uri url, long maxBytes, Stream destination, CancellationToken cancellationToken)
        {
            Attempts++;
            throw new DownloadException($"{url} could not be reached.");
        }
    }

    private sealed class RedirectingDownloader(IFileDownloader inner, Uri from, Uri to) : IFileDownloader
    {
        public Task<long> DownloadAsync(Uri url, long maxBytes, Stream destination, CancellationToken cancellationToken) =>
            inner.DownloadAsync(new Uri(to, from.MakeRelativeUri(url)), maxBytes, destination, cancellationToken);
    }

    /// <summary>Serves a folder on a free loopback port.</summary>
    private sealed class FixtureServer : IDisposable
    {
        private readonly HttpListener listener = new();
        private readonly string folder;

        public FixtureServer(string folder)
        {
            this.folder = folder;
            using (var probe = new TcpListener(IPAddress.Loopback, 0))
            {
                probe.Start();
                BaseUrl = new Uri($"http://127.0.0.1:{((IPEndPoint)probe.LocalEndpoint).Port}/");
                probe.Stop();
            }

            listener.Prefixes.Add(BaseUrl.AbsoluteUri);
            listener.Start();
            _ = Task.Run(ServeAsync);
        }

        public Uri BaseUrl { get; }

        public List<string> UserAgents { get; } = [];

        public void Dispose() => listener.Close();

        private async Task ServeAsync()
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException)
                {
                    return;
                }

                lock (UserAgents)
                    UserAgents.Add(context.Request.UserAgent ?? "");
                var path = Path.Combine(folder, context.Request.Url!.AbsolutePath.TrimStart('/'));
                if (File.Exists(path))
                {
                    var bytes = await File.ReadAllBytesAsync(path);
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes);
                }
                else
                {
                    context.Response.StatusCode = 404;
                }

                context.Response.Close();
            }
        }
    }
}
