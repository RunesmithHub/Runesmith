using Runesmith.Composition;
using RunesmithHub.Protocol;

namespace Runesmith.Shell.Web;

/// <summary>What a web view may do with an address.</summary>
internal enum WebAddressKind
{
    /// <summary>A file of the plugin's own web folder, on its local origin.</summary>
    Bundled,

    /// <summary>A page on a host the plugin declared, with the network capability.</summary>
    Network,

    /// <summary>The empty page web engines start with.</summary>
    Blank,

    Blocked,
}

/// <summary>The rules of a plugin's web views: its own files on its own local origin, and https pages on the hosts its manifest declares
/// when it has the network capability; nothing else.</summary>
/// <param name="Origin">The plugin's local origin, such as <c>http://127.0.0.1:41234/</c>.</param>
/// <param name="AllowsNetwork">Whether the plugin's manifest declares the network capability.</param>
/// <param name="Hosts">The manifest's <c>networkHosts</c>; <c>*</c> stands for any host.</param>
internal sealed record WebViewPolicy(string PluginId, Uri Origin, bool AllowsNetwork, IReadOnlyList<string> Hosts)
{
    /// <summary>Gets the rules of a plugin, from its manifest as Runesmith read it, never from what the plugin says at run time. A manifest
    /// in the older format declares no hosts, so its web views stay on its own files.</summary>
    public static WebViewPolicy For(PluginInfo plugin, Uri origin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        var declared = plugin.Manifest.Capabilities?.Any(c => c.Id == Capabilities.Network) == true;
        return new WebViewPolicy(plugin.Manifest.Id, origin, declared, declared ? plugin.Manifest.NetworkHosts : []);
    }

    /// <summary>Gets what an address is, and why it is blocked when it is.</summary>
    public (WebAddressKind Kind, string? Reason) Check(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!address.IsAbsoluteUri)
            return (WebAddressKind.Blocked, "the address is not absolute.");
        if (address.AbsoluteUri == "about:blank")
            return (WebAddressKind.Blank, null);
        if (IsOwnOrigin(address))
        {
            return BundledFiles.IsSafePath(Uri.UnescapeDataString(address.AbsolutePath))
                ? (WebAddressKind.Bundled, null)
                : (WebAddressKind.Blocked, "the path leaves the plugin's web folder.");
        }

        if (address.Scheme != Uri.UriSchemeHttps)
            return (WebAddressKind.Blocked, $"web views open only the plugin's own files and https pages, not {address.Scheme} addresses.");
        if (!AllowsNetwork)
            return (WebAddressKind.Blocked, "the plugin's manifest does not declare the network capability.");
        if (!AllowsHost(address.IdnHost))
            return (WebAddressKind.Blocked, $"{address.IdnHost} is not in the plugin's networkHosts.");
        return (WebAddressKind.Network, null);
    }

    /// <summary>Gets the content security policy of the plugin's own pages: scripts, styles and other resources from its own files, and from
    /// its declared hosts over https when it has the network capability; no frames, plugins, popups or downloads.</summary>
    public string ContentSecurityPolicy
    {
        get
        {
            var network = !AllowsNetwork || Hosts.Count == 0 ? ""
                : Hosts.Contains("*") ? " https:"
                : string.Concat(Hosts.Select(h => $" https://{h}"));
            var sockets = !AllowsNetwork || Hosts.Count == 0 ? ""
                : Hosts.Contains("*") ? " wss:"
                : string.Concat(Hosts.Select(h => $" wss://{h}"));
            return $"default-src 'none'; script-src 'self'{network}; style-src 'self' 'unsafe-inline'{network}; img-src 'self' data: blob:{network}; "
                + $"font-src 'self' data:{network}; media-src 'self' blob:{network}; connect-src 'self'{network}{sockets}; worker-src 'self'; "
                + "frame-src 'none'; child-src 'none'; object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'; "
                + "sandbox allow-scripts allow-same-origin allow-forms allow-modals";
        }
    }

    public bool IsOwnOrigin(Uri address) =>
        address.IsAbsoluteUri && address.Scheme == Origin.Scheme && address.Host == Origin.Host && address.Port == Origin.Port && address.UserInfo.Length == 0;

    private bool AllowsHost(string host) =>
        Hosts.Any(h => h == "*" || string.Equals(h.TrimEnd('.'), host.TrimEnd('.'), StringComparison.OrdinalIgnoreCase));
}
