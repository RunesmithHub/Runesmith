using System.Globalization;

namespace Runesmith.Git.Git;

/// <summary>What a reference names.</summary>
internal enum RefKind
{
    LocalBranch,
    RemoteBranch,
    Tag,
}

/// <summary>A branch or tag.</summary>
/// <param name="FullName">The full name, such as <c>refs/heads/main</c>.</param>
/// <param name="Kind">What it names.</param>
/// <param name="Sha">The commit it points at; for an annotated tag, the tagged commit.</param>
internal sealed record GitRef(string FullName, RefKind Kind, string Sha)
{
    private const string Heads = "refs/heads/";
    private const string Remotes = "refs/remotes/";
    private const string Tags = "refs/tags/";

    /// <summary>Gets the short name, such as <c>main</c>, <c>origin/main</c> or <c>v1.0</c>.</summary>
    public string Name => ShortName(FullName);

    /// <summary>Gets the upstream of a local branch, such as <c>origin/main</c>, or null.</summary>
    public string? Upstream { get; init; }

    /// <summary>Gets the commits a local branch has that its upstream does not.</summary>
    public int Ahead { get; init; }

    /// <summary>Gets the commits the upstream has that a local branch does not.</summary>
    public int Behind { get; init; }

    /// <summary>Gets whether the upstream was deleted on the remote.</summary>
    public bool IsUpstreamGone { get; init; }

    /// <summary>Gets whether this is the current branch.</summary>
    public bool IsHead { get; init; }

    /// <summary>Gets when the commit or tag was made.</summary>
    public DateTimeOffset? Date { get; init; }

    /// <summary>Gets the subject of the commit or tag.</summary>
    public string Subject { get; init; } = "";

    /// <summary>Gets the remote of a remote branch, such as <c>origin</c>, or null.</summary>
    public string? Remote => Kind == RefKind.RemoteBranch ? Name.Split('/', 2)[0] : null;

    /// <summary>Gets a remote branch's name on its remote, such as <c>main</c> for <c>origin/main</c>; a local branch's or tag's name otherwise.</summary>
    public string BranchName => Kind == RefKind.RemoteBranch && Name.Split('/', 2) is [_, var branch] ? branch : Name;

    /// <summary>Shortens a full reference name: <c>refs/heads/main</c> to <c>main</c>, <c>refs/remotes/origin/main</c> to <c>origin/main</c>.</summary>
    public static string ShortName(string fullName)
    {
        ArgumentNullException.ThrowIfNull(fullName);
        return fullName.StartsWith(Heads, StringComparison.Ordinal) ? fullName[Heads.Length..]
            : fullName.StartsWith(Remotes, StringComparison.Ordinal) ? fullName[Remotes.Length..]
            : fullName.StartsWith(Tags, StringComparison.Ordinal) ? fullName[Tags.Length..]
            : fullName;
    }

    /// <summary>Finds the kind of a full reference name, or null for references that are not branches or tags, such as the stash.</summary>
    public static RefKind? KindOf(string fullName)
    {
        ArgumentNullException.ThrowIfNull(fullName);
        return fullName.StartsWith(Heads, StringComparison.Ordinal) ? RefKind.LocalBranch
            : fullName.StartsWith(Remotes, StringComparison.Ordinal) ? RefKind.RemoteBranch
            : fullName.StartsWith(Tags, StringComparison.Ordinal) ? RefKind.Tag
            : null;
    }
}

/// <summary>Reads the output of <c>git for-each-ref</c> with <see cref="Format"/>.</summary>
internal static class RefParser
{
    /// <summary>The format: fields separated by NUL, which no reference name or subject contains, and one reference per line.</summary>
    public const string Format =
        "%(refname)%00%(objectname)%00%(*objectname)%00%(symref)%00%(HEAD)%00%(upstream)%00%(upstream:track,nobracket)%00%(creatordate:unix)%00%(contents:subject)";

    /// <summary>Parses the output; symbolic references such as <c>origin/HEAD</c> are left out.</summary>
    public static IReadOnlyList<GitRef> Parse(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var refs = new List<GitRef>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split('\0');
            if (fields.Length < 9 || fields[3].Length > 0 || GitRef.KindOf(fields[0]) is not { } kind)
                continue;

            var (ahead, behind, gone) = ParseTrack(fields[6]);
            refs.Add(new GitRef(fields[0], kind, fields[2].Length > 0 ? fields[2] : fields[1])
            {
                IsHead = fields[4] == "*",
                Upstream = fields[5].Length > 0 ? GitRef.ShortName(fields[5]) : null,
                Ahead = ahead,
                Behind = behind,
                IsUpstreamGone = gone,
                Date = long.TryParse(fields[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null,
                Subject = fields[8],
            });
        }

        return refs;
    }

    // Reads "ahead 2, behind 1", "ahead 2", "behind 1", "gone" or nothing.
    private static (int Ahead, int Behind, bool Gone) ParseTrack(string track)
    {
        if (track == "gone")
            return (0, 0, true);

        int ahead = 0, behind = 0;
        foreach (var part in track.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var words = part.Split(' ');
            if (words.Length == 2 && int.TryParse(words[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
            {
                if (words[0] == "ahead")
                    ahead = count;
                else if (words[0] == "behind")
                    behind = count;
            }
        }

        return (ahead, behind, false);
    }
}
