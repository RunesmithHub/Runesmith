using System.Net;
using System.Net.Sockets;
using System.Text;
using HammerUI.Controls;
using Runesmith.Composition;
using Runesmith.Shell.Tests.Plugins;
using Runesmith.Shell.Web;

namespace Runesmith.Shell.Tests.Web;

public sealed class WebViewServiceTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("runesmith-webview-").FullName;
    private readonly List<string> log = [];
    private readonly List<WebViewService> services = [];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        foreach (var service in services)
            service.Dispose();
        TestFolders.Delete(folder);
    }

    [Fact]
    public void RunesmithItselfHasNoWebViews() => Assert.Throws<InvalidOperationException>(() => Service(null).Create());

    [Theory]
    [InlineData("../plugin.json")]
    [InlineData("pages/../../plugin.json")]
    [InlineData("%2e%2e/plugin.json")]
    [InlineData("a\\..\\..\\plugin.json")]
    public Task APageOutsideTheWebFolderIsRefused(string page) => HeadlessSession.Value.Dispatch(() =>
    {
        Assert.Throws<ArgumentException>(() => Service(Plugin("acme.todo")).Create(page));
    }, Token);

    [Fact]
    public Task APluginWithoutTheNetworkCapabilityCannotOpenAWebAddress() => HeadlessSession.Value.Dispatch(() =>
    {
        using var view = Service(WithHosts(Plugin("acme.todo", "process"), "api.example.com")).Create();

        var exception = Assert.Throws<UnauthorizedAccessException>(() => view.Navigate("https://api.example.com/"));

        Assert.Contains("network capability", exception.Message, StringComparison.Ordinal);
        Assert.Equal(exception.Message, Assert.Single(log));
    }, Token);

    [Fact]
    public Task APluginWithTheNetworkCapabilityOpensOnlyItsDeclaredHosts() => HeadlessSession.Value.Dispatch(() =>
    {
        using var view = Service(WithHosts(Plugin("acme.charts", "network"), "api.example.com")).Create();

        view.Navigate("https://api.example.com/dashboard");
        view.Navigate("settings.html");
        Assert.Throws<UnauthorizedAccessException>(() => view.Navigate("https://tracker.example.net/"));
        Assert.Throws<ArgumentException>(() => view.Navigate("http://api.example.com/"));
        Assert.Throws<ArgumentException>(() => view.Navigate("file:///etc/passwd"));
        Assert.Throws<ArgumentException>(() => view.Navigate("../plugin.json"));
        Assert.Equal(3, log.Count);
    }, Token);

    [Fact]
    public Task MessagesMustBeJson() => HeadlessSession.Value.Dispatch(() =>
    {
        using var view = Service(Plugin("acme.todo")).Create();

        view.PostMessage("""{"kind":"refresh"}""");
        Assert.Throws<ArgumentException>(() => view.PostMessage("refresh"));
    }, Token);

    [Fact]
    public Task WithoutABrowserEngineTheViewSaysWhy() => HeadlessSession.Value.Dispatch(() =>
    {
        using var view = Service(Plugin("acme.todo"), "Install WebKitGTK 4.1 (libwebkit2gtk-4.1) and restart Runesmith.").Create();

        Assert.False(view.IsAvailable);
        Assert.Contains("WebKitGTK", view.UnavailableReason, StringComparison.Ordinal);
        Assert.Contains("WebKitGTK", Assert.IsType<EmptyState>(view.Control).Hint, StringComparison.Ordinal);
    }, Token);

    [Fact]
    public async Task EachPluginGetsItsOwnOriginServingOnlyItsWebFolder()
    {
        var todo = Plugin("acme.todo");
        File.WriteAllText(Path.Combine(todo.Directory, "plugin.json"), "{}");
        File.WriteAllText(Path.Combine(todo.Directory, "web", "index.html"), "<h1>Todo</h1>");
        var service = Service(null);
        var policy = service.PolicyOf(todo);
        var other = service.PolicyOf(Plugin("acme.notes"));

        Assert.Same(policy, service.PolicyOf(todo));
        Assert.NotEqual(policy.Origin, other.Origin);
        Assert.Equal(IPAddress.Loopback.ToString(), policy.Origin.Host);

        using var client = new HttpClient();
        using var page = await client.GetAsync(new Uri(policy.Origin, "index.html"), Token);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("<h1>Todo</h1>", await page.Content.ReadAsStringAsync(Token));
        Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
        Assert.Equal(policy.ContentSecurityPolicy, Assert.Single(page.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("nosniff", Assert.Single(page.Headers.GetValues("X-Content-Type-Options")));

        using var bridge = await client.GetAsync(new Uri(policy.Origin, BundledFiles.BridgePath), Token);
        Assert.Contains("runesmith", await bridge.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);

        Assert.StartsWith("HTTP/1.1 404", await RawAsync(policy.Origin, "GET /../plugin.json HTTP/1.1"), StringComparison.Ordinal);
        Assert.StartsWith("HTTP/1.1 404", await RawAsync(policy.Origin, "GET /%2e%2e/plugin.json HTTP/1.1"), StringComparison.Ordinal);
        Assert.StartsWith("HTTP/1.1 405", await RawAsync(policy.Origin, "POST /index.html HTTP/1.1"), StringComparison.Ordinal);
        Assert.StartsWith("HTTP/1.1 421", await RawAsync(policy.Origin, "GET /index.html HTTP/1.1", host: "evil.example.com"), StringComparison.Ordinal);
        Assert.StartsWith("HTTP/1.1 400", await RawAsync(policy.Origin, "nonsense"), StringComparison.Ordinal);
    }

    private static async Task<string> RawAsync(Uri origin, string requestLine, string? host = null)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, origin.Port, Token);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"{requestLine}\r\nHost: {host ?? origin.Authority}\r\n\r\n"), Token);
        using var reader = new StreamReader(stream, Encoding.ASCII);
        return await reader.ReadToEndAsync(Token);
    }

    private WebViewService Service(PluginInfo? caller, string? unavailable = "No browser engine in the tests.")
    {
        var service = new WebViewService(() => caller, log.Add, () => unavailable);
        services.Add(service);
        return service;
    }

    private PluginInfo Plugin(string id, params string[] capabilities)
    {
        var plugin = TestCallers.Plugin(id, capabilities) with { Directory = Path.Combine(folder, id) };
        Directory.CreateDirectory(Path.Combine(plugin.Directory, "web"));
        return plugin;
    }

    private static PluginInfo WithHosts(PluginInfo plugin, params string[] hosts) => plugin with { Manifest = plugin.Manifest with { NetworkHosts = hosts } };
}
