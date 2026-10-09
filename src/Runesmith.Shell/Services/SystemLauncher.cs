using System.Composition;
using Runesmith.Composition;
using Runesmith.Sdk.Shell;
using RunesmithHub.Protocol;

namespace Runesmith.Shell.Services;

/// <summary>Hands links and files to the system's browser and file manager, for plugins that declared the process capability.</summary>
[Export(typeof(ILauncher))]
[Shared]
public sealed class SystemLauncher : ILauncher
{
    private readonly Func<PluginInfo?> caller;
    private readonly Action<string> log;

    [ImportingConstructor]
    public SystemLauncher(IOutputService output)
        : this(PluginCallers.Current, line => output.GetChannel(PluginAccess.ChannelName).AppendLine(line))
    {
    }

    internal SystemLauncher(Func<PluginInfo?> caller, Action<string> log)
    {
        this.caller = caller;
        this.log = log;
    }

    /// <exception cref="ArgumentException">The address is not an absolute http or https address.</exception>
    /// <exception cref="UnauthorizedAccessException">The calling plugin did not declare the process capability.</exception>
    public void OpenUrl(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        PluginAccess.Demand(caller(), Capabilities.Process, "launcher", log);
        if (!url.IsAbsoluteUri || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
            throw new ArgumentException($"{url} is not a web address.", nameof(url));

        Launcher.OpenUrl(url.AbsoluteUri);
    }

    /// <exception cref="UnauthorizedAccessException">The calling plugin did not declare the process capability.</exception>
    public void Reveal(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        PluginAccess.Demand(caller(), Capabilities.Process, "launcher", log);
        Launcher.Reveal(path);
    }
}
