using System.Composition;
using Runesmith.Composition;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.Web;
using Runesmith.Shell.Services;

namespace Runesmith.Shell.Web;

/// <summary>Creates plugins' web views, each plugin's held to the rules its manifest sets, and serves each plugin's web folder on an origin
/// of its own.</summary>
[Export(typeof(IWebViewService))]
[Shared]
public sealed class WebViewService : IWebViewService, IDisposable
{
    private readonly Func<PluginInfo?> caller;
    private readonly Action<string> log;
    private readonly Func<string?> unavailableReason;
    private readonly Dictionary<string, (PluginWebServer Server, WebViewPolicy Policy)> origins = new(StringComparer.Ordinal);

    [ImportingConstructor]
    public WebViewService(IOutputService output)
        : this(PluginCallers.Current, line => output.GetChannel(PluginAccess.ChannelName).AppendLine(line), () => WebViewSupport.UnavailableReason)
    {
    }

    internal WebViewService(Func<PluginInfo?> caller, Action<string> log, Func<string?> unavailableReason)
    {
        this.caller = caller;
        this.log = log;
        this.unavailableReason = unavailableReason;
    }

    public IWebView Create(string page = "index.html")
    {
        ArgumentException.ThrowIfNullOrEmpty(page);
        var plugin = caller() ?? throw new InvalidOperationException("Only plugins have web views; their pages come from their own web folder.");
        var policy = PolicyOf(plugin);
        var view = new PluginWebView(policy, unavailableReason(), log);
        try
        {
            var start = view.Resolve(page);
            if (!policy.IsOwnOrigin(start) || policy.Check(start).Kind != WebAddressKind.Bundled)
                throw new ArgumentException($"{page} is not a page in the plugin's {IWebViewService.WebFolder} folder.", nameof(page));
            view.Start(start);
        }
        catch (ArgumentException)
        {
            view.Dispose();
            throw;
        }

        return view;
    }

    public void Dispose()
    {
        foreach (var (server, _) in origins.Values)
            server.Dispose();
        origins.Clear();
    }

    /// <summary>Gets the rules of a plugin's web views, starting its local origin the first time.</summary>
    internal WebViewPolicy PolicyOf(PluginInfo plugin)
    {
        lock (origins)
        {
            if (origins.TryGetValue(plugin.Manifest.Id, out var known))
                return known.Policy;

            WebViewPolicy? policy = null;
            var server = new PluginWebServer(Path.Combine(plugin.Directory, IWebViewService.WebFolder), () => policy!.ContentSecurityPolicy);
            policy = WebViewPolicy.For(plugin, server.Origin);
            origins[plugin.Manifest.Id] = (server, policy);
            return policy;
        }
    }
}
