using System.Composition;
using Runesmith.Sdk.VersionControl;

namespace Runesmith.Git.Hosting;

/// <summary>A remote of the open repository and the host that owns it.</summary>
internal sealed record HostedRemote(GitRemote Remote, IRepositoryHost Host);

/// <summary>The hosting plugins' providers and their hosts, and which host owns a remote.</summary>
[Export]
[Shared]
internal sealed class RepositoryHosts : IDisposable
{
    private readonly IReadOnlyList<IRepositoryHostProvider> providers;
    private readonly HashSet<IRepositoryHost> watched = [];
    private readonly Lock gate = new();

    /// <summary>Creates the list over the providers, ordered by name so the clone dialog's tabs keep their places.</summary>
    [ImportingConstructor]
    public RepositoryHosts([ImportMany] IEnumerable<IRepositoryHostProvider> providers)
    {
        this.providers = [.. providers.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)];
        foreach (var provider in this.providers)
            provider.Changed += OnProviderChanged;
        WatchHosts();
    }

    /// <summary>Gets the providers, by name.</summary>
    public IReadOnlyList<IRepositoryHostProvider> Providers => providers;

    /// <summary>Gets every provider's hosts, in the providers' order.</summary>
    public IReadOnlyList<IRepositoryHost> Hosts => [.. providers.SelectMany(p => p.Hosts)];

    /// <summary>Raised, on any thread, when a provider adds or removes a host or a host signs in or out.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the provider a host belongs to.</summary>
    public IRepositoryHostProvider? ProviderOf(IRepositoryHost host) => providers.FirstOrDefault(p => p.Hosts.Contains(host));

    /// <summary>Gets the host that owns a remote URL, preferring one that is signed in.</summary>
    public IRepositoryHost? Owner(string remoteUrl) =>
        Hosts.Where(h => h.Owns(remoteUrl)).OrderBy(h => h.Account is null ? 1 : 0).FirstOrDefault();

    /// <summary>Finds the repository's hosted remote: <c>origin</c> when a host owns it, then the other remotes, <c>upstream</c> last.</summary>
    /// <param name="repository">The repository.</param>
    /// <param name="preferUpstream">Whether the remote of the current branch's upstream comes first, such as for commits that may only be on a fork.</param>
    public HostedRemote? Find(RepositoryInfo? repository, bool preferUpstream = false)
    {
        if (repository is null)
            return null;

        var upstream = preferUpstream ? repository.Upstream?.Split('/')[0] : null;
        foreach (var remote in repository.Remotes.OrderBy(r => r.Name == upstream ? -1 : r.Name == "origin" ? 0 : r.Name == "upstream" ? 2 : 1))
        {
            if (Owner(remote.FetchUrl) is { } host)
                return new HostedRemote(remote, host);
        }

        return null;
    }

    public void Dispose()
    {
        foreach (var provider in providers)
            provider.Changed -= OnProviderChanged;
        lock (gate)
        {
            foreach (var host in watched)
                host.Changed -= OnHostChanged;
            watched.Clear();
        }
    }

    private void OnProviderChanged(object? sender, EventArgs e)
    {
        WatchHosts();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnHostChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

    private void WatchHosts()
    {
        var hosts = Hosts;
        lock (gate)
        {
            foreach (var gone in watched.Except(hosts).ToList())
            {
                gone.Changed -= OnHostChanged;
                watched.Remove(gone);
            }

            foreach (var host in hosts.Where(watched.Add))
                host.Changed += OnHostChanged;
        }
    }
}
