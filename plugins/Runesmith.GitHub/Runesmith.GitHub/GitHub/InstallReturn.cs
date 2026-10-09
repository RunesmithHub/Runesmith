namespace Runesmith.GitHub.GitHub;

/// <summary>Remembers the installations from before the user went to GitHub to add an organization, and finds the new ones each time they come
/// back, until one appears or <see cref="Patience"/> passes.</summary>
internal sealed class InstallReturn(GitHubInstallations installations, TimeProvider? time = null)
{
    /// <summary>How long returns are watched for a new installation; an owner may take a while to approve a member's request.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromMinutes(30);

    private readonly TimeProvider time = time ?? TimeProvider.System;
    private HashSet<long>? before;
    private DateTimeOffset until;

    /// <summary>Gets whether a return is awaited.</summary>
    public bool IsWaiting => time.GetUtcNow() < until;

    /// <summary>Notes the installations there are now; call it before opening GitHub's install page.</summary>
    public async Task BeginAsync(CancellationToken cancellationToken)
    {
        var known = installations.Current;
        if (known is null)
        {
            try
            {
                known = await installations.LoadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (GitHubException)
            {
            }
        }

        // Without a list from before, every installation would look new, so nothing is announced.
        before = known is null ? null : [.. known.Installations.Select(i => i.Id)];
        until = time.GetUtcNow() + Patience;
    }

    /// <summary>Loads the installations again and returns the ones added since <see cref="BeginAsync"/>; stops waiting once there are some.</summary>
    public async Task<IReadOnlyList<GitHubInstallation>> ReturnAsync(CancellationToken cancellationToken)
    {
        var snapshot = await installations.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!IsWaiting || before is null)
            return [];

        var added = snapshot.AddedSince(before);
        if (added.Count > 0)
            until = DateTimeOffset.MinValue;
        return added;
    }

    /// <summary>Gets the toast for new installations, such as "Runesmith can now use repositories of lumen-labs", or null when there are none.</summary>
    public static string? Announcement(IReadOnlyList<GitHubInstallation> added)
    {
        ArgumentNullException.ThrowIfNull(added);
        var names = added.Select(i => i.Account.Login).ToList();
        return names.Count switch
        {
            0 => null,
            1 => $"Runesmith can now use repositories of {names[0]}",
            _ => $"Runesmith can now use repositories of {string.Join(", ", names[..^1])} and {names[^1]}",
        };
    }
}
