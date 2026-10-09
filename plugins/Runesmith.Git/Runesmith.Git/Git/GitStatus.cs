using System.Globalization;

namespace Runesmith.Git.Git;

/// <summary>What kind of line of <c>git status --porcelain=v2</c> a file came from.</summary>
internal enum StatusKind
{
    /// <summary>A tracked file that changed: modified, added, deleted or with a changed type.</summary>
    Ordinary,

    /// <summary>A file renamed from <see cref="StatusEntry.OriginalPath"/>.</summary>
    Renamed,

    /// <summary>A file copied from <see cref="StatusEntry.OriginalPath"/>.</summary>
    Copied,

    /// <summary>A file with merge conflicts.</summary>
    Unmerged,

    /// <summary>A file Git does not track.</summary>
    Untracked,

    /// <summary>A file Git ignores.</summary>
    Ignored,
}

/// <summary>One file in <c>git status</c>.</summary>
/// <param name="Path">The path relative to the repository's top folder, with forward slashes.</param>
/// <param name="Index">The staged change: <c>.</c> for none, or <c>M</c>, <c>T</c>, <c>A</c>, <c>D</c>, <c>R</c>, <c>C</c> or <c>U</c>.</param>
/// <param name="WorkTree">The change not staged, in the same letters.</param>
/// <param name="Kind">What kind of change it is.</param>
internal sealed record StatusEntry(string Path, char Index, char WorkTree, StatusKind Kind)
{
    /// <summary>Gets the path the file was renamed or copied from, or null.</summary>
    public string? OriginalPath { get; init; }

    /// <summary>Gets whether the entry is a submodule.</summary>
    public bool IsSubmodule { get; init; }

    /// <summary>Gets whether part of the change is staged.</summary>
    public bool IsStaged => Kind is StatusKind.Ordinary or StatusKind.Renamed or StatusKind.Copied && Index != '.';

    /// <summary>Gets whether part of the change is in the working tree only; untracked files are not.</summary>
    public bool IsUnstaged => Kind is StatusKind.Ordinary or StatusKind.Renamed or StatusKind.Copied && WorkTree != '.';
}

/// <summary>The branch part of <c>git status --branch</c>.</summary>
/// <param name="Oid">The commit HEAD points at, or null before the first commit.</param>
/// <param name="Head">The current branch, or null when HEAD is detached.</param>
/// <param name="Upstream">The upstream branch, such as <c>origin/main</c>, or null.</param>
/// <param name="Ahead">Commits the branch has that its upstream does not.</param>
/// <param name="Behind">Commits the upstream has that the branch does not.</param>
internal sealed record BranchStatus(string? Oid, string? Head, string? Upstream, int Ahead, int Behind)
{
    /// <summary>Gets the status of a repository without commits on an unknown branch.</summary>
    public static BranchStatus Empty { get; } = new(null, null, null, 0, 0);
}

/// <summary>The repository's status at one moment: the branch and every changed, untracked and conflicted file.</summary>
internal sealed record StatusSnapshot(BranchStatus Branch, IReadOnlyList<StatusEntry> Entries)
{
    /// <summary>Gets the files with a staged change.</summary>
    public IEnumerable<StatusEntry> Staged => Entries.Where(e => e.IsStaged);

    /// <summary>Gets the tracked files with a change that is not staged.</summary>
    public IEnumerable<StatusEntry> Unstaged => Entries.Where(e => e.IsUnstaged);

    /// <summary>Gets the files Git does not track.</summary>
    public IEnumerable<StatusEntry> Untracked => Entries.Where(e => e.Kind == StatusKind.Untracked);

    /// <summary>Gets the files with merge conflicts.</summary>
    public IEnumerable<StatusEntry> Conflicts => Entries.Where(e => e.Kind == StatusKind.Unmerged);

    /// <summary>Gets the number of files that changed, counting each file once; ignored files do not count.</summary>
    public int ChangedCount => Entries.Count(e => e.Kind != StatusKind.Ignored);

    /// <summary>Whether two snapshots describe the same state.</summary>
    public bool IsSameAs(StatusSnapshot? other) => other is not null && Branch == other.Branch && Entries.SequenceEqual(other.Entries);
}

/// <summary>Reads the output of <c>git status --porcelain=v2 -z --branch</c>.</summary>
internal static class StatusParser
{
    /// <summary>Parses the output; unknown lines are skipped, so newer Git versions with more headers still parse.</summary>
    public static StatusSnapshot Parse(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        string? oid = null, head = null, upstream = null;
        int ahead = 0, behind = 0;
        var entries = new List<StatusEntry>();
        var records = output.Split('\0');
        for (var i = 0; i < records.Length; i++)
        {
            var record = records[i];
            if (record.Length < 2)
                continue;

            switch (record[0])
            {
                case '#':
                    ReadHeader(record, ref oid, ref head, ref upstream, ref ahead, ref behind);
                    break;
                case '1':
                    // 1 XY sub mH mI mW hH hI path
                    var ordinary = record.Split(' ', 9);
                    if (ordinary.Length == 9)
                        entries.Add(new StatusEntry(ordinary[8], ordinary[1][0], ordinary[1][1], StatusKind.Ordinary) { IsSubmodule = ordinary[2][0] == 'S' });
                    break;
                case '2':
                    // 2 XY sub mH mI mW hH hI Xscore path, then the original path as the next record
                    var moved = record.Split(' ', 10);
                    if (moved.Length == 10)
                    {
                        var original = i + 1 < records.Length ? records[++i] : null;
                        var kind = moved[8][0] == 'C' ? StatusKind.Copied : StatusKind.Renamed;
                        entries.Add(new StatusEntry(moved[9], moved[1][0], moved[1][1], kind) { OriginalPath = original, IsSubmodule = moved[2][0] == 'S' });
                    }

                    break;
                case 'u':
                    // u XY sub m1 m2 m3 mW h1 h2 h3 path
                    var unmerged = record.Split(' ', 11);
                    if (unmerged.Length == 11)
                        entries.Add(new StatusEntry(unmerged[10], unmerged[1][0], unmerged[1][1], StatusKind.Unmerged) { IsSubmodule = unmerged[2][0] == 'S' });
                    break;
                case '?':
                    entries.Add(new StatusEntry(record[2..], '?', '?', StatusKind.Untracked));
                    break;
                case '!':
                    entries.Add(new StatusEntry(record[2..], '!', '!', StatusKind.Ignored));
                    break;
            }
        }

        return new StatusSnapshot(new BranchStatus(oid, head, upstream, ahead, behind), entries);
    }

    private static void ReadHeader(string record, ref string? oid, ref string? head, ref string? upstream, ref int ahead, ref int behind)
    {
        var parts = record.Split(' ', 3);
        if (parts.Length < 3)
            return;

        switch (parts[1])
        {
            case "branch.oid":
                oid = parts[2] == "(initial)" ? null : parts[2];
                break;
            case "branch.head":
                head = parts[2] == "(detached)" ? null : parts[2];
                break;
            case "branch.upstream":
                upstream = parts[2];
                break;
            case "branch.ab":
                var counts = parts[2].Split(' ');
                if (counts.Length == 2)
                {
                    ahead = int.Parse(counts[0].TrimStart('+'), CultureInfo.InvariantCulture);
                    behind = int.Parse(counts[1].TrimStart('-'), CultureInfo.InvariantCulture);
                }

                break;
        }
    }
}
