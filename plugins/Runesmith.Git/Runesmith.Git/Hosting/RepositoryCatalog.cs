using System.Composition;
using Runesmith.Sdk.VersionControl;

namespace Runesmith.Git.Hosting;

/// <summary>An account or organization that owns repositories on a host.</summary>
internal sealed record RepositoryOwner(string Login, Uri? AvatarUrl, bool IsOrganization);

/// <summary>A row of the clone dialog's repository list: an owner's heading or a repository.</summary>
internal abstract record RepositoryListRow;

/// <summary>The heading of one owner's repositories.</summary>
internal sealed record OwnerRow(RepositoryOwner Owner, bool IsAccount, int Count) : RepositoryListRow;

internal sealed record RepositoryRow(HostedRepository Repository) : RepositoryListRow;

/// <summary>The repositories a host's account may clone.</summary>
/// <param name="Repositories">The repositories, each once.</param>
internal sealed record RepositoryCatalog(IReadOnlyList<HostedRepository> Repositories)
{
    /// <summary>Gets the owners of the repositories: the signed-in account first, then organizations and other users by name.</summary>
    public IReadOnlyList<RepositoryOwner> Owners(string? accountLogin) =>
    [
        .. Repositories.Select(r => new RepositoryOwner(r.Owner, r.OwnerAvatarUrl, r.OwnerIsOrganization)).DistinctBy(o => o.Login, StringComparer.OrdinalIgnoreCase)
            .OrderBy(o => string.Equals(o.Login, accountLogin, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(o => o.Login, StringComparer.OrdinalIgnoreCase),
    ];

    /// <summary>Gets the list's rows: for each owner with matches, its heading and its repositories, most recently changed first.</summary>
    /// <param name="accountLogin">The signed-in account, whose repositories come first.</param>
    /// <param name="search">Words that must each appear in the name, owner or description; empty shows everything.</param>
    /// <param name="owner">The only owner to show, or null for all.</param>
    public IReadOnlyList<RepositoryListRow> Rows(string? accountLogin, string? search, string? owner)
    {
        var words = (search ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var rows = new List<RepositoryListRow>();
        foreach (var candidate in Owners(accountLogin))
        {
            if (owner is not null && !string.Equals(candidate.Login, owner, StringComparison.OrdinalIgnoreCase))
                continue;

            var matches = Repositories
                .Where(r => string.Equals(r.Owner, candidate.Login, StringComparison.OrdinalIgnoreCase) && Matches(r, words))
                .OrderByDescending(r => r.UpdatedAt ?? DateTimeOffset.MinValue)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (matches.Count == 0)
                continue;

            rows.Add(new OwnerRow(candidate, string.Equals(candidate.Login, accountLogin, StringComparison.OrdinalIgnoreCase), matches.Count));
            rows.AddRange(matches.Select(r => new RepositoryRow(r)));
        }

        return rows;
    }

    private static bool Matches(HostedRepository repository, string[] words) =>
        words.All(word => repository.Name.Contains(word, StringComparison.OrdinalIgnoreCase)
            || repository.Owner.Contains(word, StringComparison.OrdinalIgnoreCase)
            || repository.Description?.Contains(word, StringComparison.OrdinalIgnoreCase) == true);
}

/// <summary>Keeps each host's repository list until its account or access changes or the list is refreshed, so the clone dialog opens quickly.</summary>
[Export]
[Shared]
internal sealed class HostedRepositories
{
    private readonly Dictionary<string, RepositoryCatalog> cached = new(StringComparer.Ordinal);
    private readonly Lock gate = new();

    public HostedRepositories()
    {
    }

    /// <summary>Creates the cache, which forgets every list when a host signs in or out or its access changes.</summary>
    [ImportingConstructor]
    public HostedRepositories(RepositoryHosts hosts)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        hosts.Changed += (_, _) => Forget();
    }

    /// <summary>Forgets every host's list, so the next look loads it again.</summary>
    public void Forget()
    {
        lock (gate)
            cached.Clear();
    }

    /// <summary>Gets a host's repositories, from the last load unless <paramref name="refresh"/> is set or another account signed in.</summary>
    public async Task<RepositoryCatalog> LoadAsync(IRepositoryHost host, bool refresh, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        var key = host.Id + "\n" + host.Account;
        lock (gate)
        {
            if (!refresh && cached.TryGetValue(key, out var known))
                return known;
        }

        var catalog = new RepositoryCatalog(await host.GetRepositoriesAsync(cancellationToken).ConfigureAwait(false));
        // An empty list usually means access is still being granted, so the next look asks again.
        if (catalog.Repositories.Count > 0)
        {
            lock (gate)
                cached[key] = catalog;
        }

        return catalog;
    }
}
