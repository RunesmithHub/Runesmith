using Runesmith.Git.Hosting;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.VersionControl;

namespace Runesmith.Git.Views.Clone;

/// <summary>A tab of the clone dialog: a host's account, signed in or waiting to be, or any Git URL.</summary>
internal abstract record CloneSource;

/// <summary>The tab of a host: its repositories while signed in, and a sign-in prompt otherwise.</summary>
internal sealed record HostSource(IRepositoryHost Host) : CloneSource
{
    public bool IsSignedIn => Host.Account is not null;
}

/// <summary>The tab that clones any Git URL.</summary>
internal sealed record UrlSource : CloneSource
{
    public static UrlSource Instance { get; } = new();
}

/// <summary>What the clone dialog shows: a tab per host, signed-in hosts first, then the URL tab, and an Add Account entry for each provider.</summary>
internal sealed class CloneModel(RepositoryHosts hosts, HostedRepositories repositories, ICommandService? commands = null)
{
    /// <summary>Gets the tabs: signed-in hosts, then signed-out ones, each in their providers' order, then the URL tab.</summary>
    public IReadOnlyList<CloneSource> Sources =>
    [
        .. hosts.Hosts.Select(h => new HostSource(h)).OrderBy(s => s.IsSignedIn ? 0 : 1),
        UrlSource.Instance,
    ];

    /// <summary>Gets the providers, each offering to add an account.</summary>
    public IReadOnlyList<IRepositoryHostProvider> Providers => hosts.Providers;

    /// <summary>Raised, on any thread, when hosts come and go or sign in and out.</summary>
    public event EventHandler? Changed
    {
        add => hosts.Changed += value;
        remove => hosts.Changed -= value;
    }

    /// <summary>Asks a provider for a new account; returns the tab to show, or null when the user cancelled.</summary>
    public async Task<CloneSource?> AddAccountAsync(IRepositoryHostProvider provider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return await provider.AddAccountAsync(cancellationToken).ConfigureAwait(true) is { } host
            ? Sources.OfType<HostSource>().FirstOrDefault(s => ReferenceEquals(s.Host, host)) ?? new HostSource(host)
            : null;
    }

    /// <summary>Gets the command that adds an organization to a host's account, such as <c>github.addOrganization</c>; hosting plugins that
    /// can add organizations register it.</summary>
    public static string AddOrganizationCommand(IRepositoryHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return host.Id + ".addOrganization";
    }

    /// <summary>Gets whether the host's plugin can add an organization now.</summary>
    public bool CanAddOrganization(IRepositoryHost host) => commands?.CanExecute(AddOrganizationCommand(host)) == true;

    /// <summary>Asks the host's plugin to add an organization, which usually continues in the browser.</summary>
    public Task AddOrganizationAsync(IRepositoryHost host) => commands?.ExecuteAsync(AddOrganizationCommand(host)) ?? Task.CompletedTask;

    /// <summary>Gets a host's repositories, from the last load unless <paramref name="refresh"/> is set.</summary>
    public Task<RepositoryCatalog> LoadAsync(IRepositoryHost host, bool refresh, CancellationToken cancellationToken) =>
        repositories.LoadAsync(host, refresh, cancellationToken);
}
