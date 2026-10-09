using Runesmith.Git.Git;

namespace Runesmith.Git.Views.Branches;

/// <summary>A row of the branch popup: an action, a group's heading, or a branch.</summary>
internal abstract record BranchRow;

/// <summary>An action at the top of the popup, such as Update Project.</summary>
/// <param name="Title">The action's label.</param>
/// <param name="Icon">The name of its icon.</param>
/// <param name="CommandId">The command it runs.</param>
internal sealed record ActionRow(string Title, string Icon, string CommandId) : BranchRow;

/// <summary>The heading of a group, such as Local.</summary>
internal sealed record HeaderRow(string Title) : BranchRow;

/// <summary>A branch or tag.</summary>
/// <param name="Ref">The branch or tag.</param>
/// <param name="IsRecent">Whether it is in the Recent group, which repeats branches of the Local group.</param>
internal sealed record RefRow(GitRef Ref, bool IsRecent) : BranchRow;

/// <summary>Builds the rows of the branch popup.</summary>
internal static class BranchRows
{
    private const int RecentCount = 5;
    private const int NewestTags = 10;

    /// <summary>Builds the rows: the actions, then Recent, Local and Remote branches and the newest tags; a filter keeps the rows whose
    /// text contains it, and every tag that matches.</summary>
    public static IReadOnlyList<BranchRow> Build(IReadOnlyList<ActionRow> actions, IReadOnlyList<GitRef> refs, IReadOnlyList<string> recent, string? filter)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(refs);
        ArgumentNullException.ThrowIfNull(recent);
        var text = filter?.Trim() ?? "";
        bool Matches(string value) => text.Length == 0 || value.Contains(text, StringComparison.OrdinalIgnoreCase);

        var rows = new List<BranchRow>(actions.Where(a => Matches(a.Title)));
        var locals = refs.Where(r => r.Kind == RefKind.LocalBranch && Matches(r.Name)).ToList();
        var recentRows = recent.Select(name => locals.FirstOrDefault(r => r.Name == name)).OfType<GitRef>().Where(r => !r.IsHead).Take(RecentCount).ToList();
        if (text.Length == 0 && recentRows.Count > 0 && locals.Count > RecentCount)
            Group("Recent", recentRows, isRecent: true);
        Group("Local", locals.OrderByDescending(r => r.IsHead).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase), isRecent: false);
        Group("Remote", refs.Where(r => r.Kind == RefKind.RemoteBranch && Matches(r.Name)).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase), isRecent: false);
        Group("Tags", refs.Where(r => r.Kind == RefKind.Tag && Matches(r.Name)).OrderByDescending(r => r.Date).Take(text.Length == 0 ? NewestTags : int.MaxValue), isRecent: false);
        return rows;

        void Group(string title, IEnumerable<GitRef> members, bool isRecent)
        {
            var list = members.ToList();
            if (list.Count == 0)
                return;
            rows.Add(new HeaderRow(title));
            rows.AddRange(list.Select(r => new RefRow(r, isRecent)));
        }
    }
}
