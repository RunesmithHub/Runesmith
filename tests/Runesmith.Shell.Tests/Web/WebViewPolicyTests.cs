using Runesmith.Composition;
using Runesmith.Shell.Tests.Plugins;
using Runesmith.Shell.Web;

namespace Runesmith.Shell.Tests.Web;

public sealed class WebViewPolicyTests
{
    private static readonly Uri Origin = new("http://127.0.0.1:41234/");

    [Theory]
    [InlineData("http://127.0.0.1:41234/index.html")]
    [InlineData("http://127.0.0.1:41234/pages/chart.html?range=7#top")]
    [InlineData("http://127.0.0.1:41234/")]
    public void ThePluginsOwnFilesAreAllowed(string address) =>
        Assert.Equal(WebAddressKind.Bundled, Policy(TestCallers.Plugin("acme.todo")).Check(new Uri(address)).Kind);

    [Theory]
    [InlineData("http://127.0.0.1:41235/index.html")]
    [InlineData("http://localhost:41234/index.html")]
    [InlineData("https://127.0.0.1:41234/index.html")]
    [InlineData("http://user@127.0.0.1:41234/index.html")]
    [InlineData("http://127.0.0.1:41234/a%5C..%5Csecret.txt")]
    [InlineData("file:///etc/passwd")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://example.com/")]
    public void EverythingElseIsBlockedWithoutTheNetworkCapability(string address)
    {
        var (kind, reason) = Policy(TestCallers.Plugin("acme.todo")).Check(new Uri(address));

        Assert.Equal(WebAddressKind.Blocked, kind);
        Assert.False(string.IsNullOrEmpty(reason));
    }

    [Fact]
    public void DeclaredHostsAreAllowedOverHttpsOnlyWithTheNetworkCapability()
    {
        var policy = Policy(WithHosts(TestCallers.Plugin("acme.charts", "network"), "api.example.com"));

        Assert.Equal(WebAddressKind.Network, policy.Check(new Uri("https://api.example.com/v1/chart")).Kind);
        Assert.Equal(WebAddressKind.Network, policy.Check(new Uri("https://API.Example.com/")).Kind);
        Assert.Equal(WebAddressKind.Blocked, policy.Check(new Uri("http://api.example.com/")).Kind);
        Assert.Equal(WebAddressKind.Blocked, policy.Check(new Uri("https://evil.example.com/")).Kind);
        Assert.Equal(WebAddressKind.Blocked, policy.Check(new Uri("https://api.example.com.evil.net/")).Kind);
        Assert.Contains("networkHosts", policy.Check(new Uri("https://other.org/")).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void HostsWithoutTheNetworkCapabilityAreNotEnough()
    {
        var policy = Policy(WithHosts(TestCallers.Plugin("acme.charts", "process"), "api.example.com"));

        var (kind, reason) = policy.Check(new Uri("https://api.example.com/"));

        Assert.Equal(WebAddressKind.Blocked, kind);
        Assert.Contains("network capability", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("api.example.com", policy.ContentSecurityPolicy, StringComparison.Ordinal);
    }

    [Fact]
    public void AnyHostIsAllowedWhenTheManifestSaysAnyHost()
    {
        var policy = Policy(WithHosts(TestCallers.Plugin("acme.browser", "network"), "*"));

        Assert.Equal(WebAddressKind.Network, policy.Check(new Uri("https://anything.example.org/")).Kind);
        Assert.Equal(WebAddressKind.Blocked, policy.Check(new Uri("http://anything.example.org/")).Kind);
        Assert.Contains("script-src 'self' https:;", policy.ContentSecurityPolicy, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOlderManifestDeclaresNoHostsSoItsWebViewsStayOnItsOwnFiles()
    {
        var policy = Policy(TestCallers.Legacy("acme.old"));

        Assert.False(policy.AllowsNetwork);
        Assert.Equal(WebAddressKind.Blocked, policy.Check(new Uri("https://example.com/")).Kind);
        Assert.Equal(WebAddressKind.Bundled, policy.Check(new Uri("http://127.0.0.1:41234/index.html")).Kind);
    }

    [Fact]
    public void TheContentSecurityPolicyAllowsOnlyOwnScriptsByDefault()
    {
        var policy = Policy(TestCallers.Plugin("acme.todo")).ContentSecurityPolicy;

        Assert.Contains("default-src 'none'", policy, StringComparison.Ordinal);
        Assert.Contains("script-src 'self';", policy, StringComparison.Ordinal);
        Assert.Contains("connect-src 'self';", policy, StringComparison.Ordinal);
        Assert.Contains("frame-src 'none'", policy, StringComparison.Ordinal);
        Assert.Contains("object-src 'none'", policy, StringComparison.Ordinal);
        Assert.Contains("sandbox allow-scripts allow-same-origin allow-forms allow-modals", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("allow-popups", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("allow-downloads", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-eval", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("https:", policy, StringComparison.Ordinal);
    }

    [Fact]
    public void DeclaredHostsMayServeScriptsAndConnections()
    {
        var policy = Policy(WithHosts(TestCallers.Plugin("acme.charts", "network"), "cdn.example.com")).ContentSecurityPolicy;

        Assert.Contains("script-src 'self' https://cdn.example.com;", policy, StringComparison.Ordinal);
        Assert.Contains("connect-src 'self' https://cdn.example.com wss://cdn.example.com;", policy, StringComparison.Ordinal);
    }

    private static WebViewPolicy Policy(PluginInfo plugin) => WebViewPolicy.For(plugin, Origin);

    private static PluginInfo WithHosts(PluginInfo plugin, params string[] hosts) => plugin with { Manifest = plugin.Manifest with { NetworkHosts = hosts } };
}
