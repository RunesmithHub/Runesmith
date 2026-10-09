using System.Globalization;

namespace Runesmith.Git.Git;

/// <summary>A label on a commit in the log: a branch, a tag, or HEAD.</summary>
/// <param name="Name">The short name, such as <c>main</c>, <c>origin/main</c> or <c>v1.0</c>; <c>HEAD</c> for a detached HEAD.</param>
/// <param name="Kind">What it names; null for a detached HEAD.</param>
/// <param name="IsHead">Whether HEAD is on it: the current branch, or HEAD itself when detached.</param>
internal sealed record CommitRef(string Name, RefKind? Kind, bool IsHead);

/// <summary>A commit as the log shows it.</summary>
/// <param name="Sha">The full hash.</param>
/// <param name="Parents">The parents' hashes, first parent first; none for a root commit.</param>
/// <param name="AuthorName">Who wrote the change.</param>
/// <param name="AuthorEmail">Their address.</param>
/// <param name="AuthorDate">When they wrote it.</param>
/// <param name="CommitterName">Who made the commit.</param>
/// <param name="CommitterEmail">Their address.</param>
/// <param name="CommitDate">When they made it.</param>
/// <param name="Subject">The message's first line.</param>
/// <param name="Refs">The branches and tags on the commit.</param>
internal sealed record GitCommit(string Sha, IReadOnlyList<string> Parents, string AuthorName, string AuthorEmail, DateTimeOffset AuthorDate,
    string CommitterName, string CommitterEmail, DateTimeOffset CommitDate, string Subject, IReadOnlyList<CommitRef> Refs)
{
    /// <summary>Gets the short hash: the first eight characters.</summary>
    public string ShortSha => Sha.Length > 8 ? Sha[..8] : Sha;

    /// <summary>Gets whether the commit merges branches.</summary>
    public bool IsMerge => Parents.Count > 1;
}

/// <summary>Which commits the log shows, and which page of them.</summary>
internal sealed record LogQuery
{
    /// <summary>Gets the revisions to start from, separated by spaces, such as <c>main</c>, <c>main..feature</c> or <c>HEAD --not --remotes</c>;
    /// null for every branch, remote branch and tag. Reference names cannot contain spaces.</summary>
    public string? Revision { get; init; }

    /// <summary>Gets text the author's name or address must contain, or null.</summary>
    public string? Author { get; init; }

    /// <summary>Gets text the message must contain, ignoring case, or null.</summary>
    public string? Text { get; init; }

    /// <summary>Gets a path, relative to the repository, that the commits must change, or null.</summary>
    public string? Path { get; init; }

    /// <summary>Gets whether to follow <see cref="Path"/>, a single file, through renames.</summary>
    public bool Follow { get; init; }

    /// <summary>Gets how many commits to skip, for the pages after the first.</summary>
    public int Skip { get; init; }

    /// <summary>Gets how many commits to read.</summary>
    public int Count { get; init; } = 200;

    /// <summary>Gets whether the commits form a graph worth drawing: no text or author filter leaves gaps in it.</summary>
    public bool HasGraph => string.IsNullOrEmpty(Author) && string.IsNullOrEmpty(Text);
}

/// <summary>A file a commit changed.</summary>
/// <param name="Status">The change: <c>A</c>, <c>M</c>, <c>D</c>, <c>R</c>, <c>C</c> or <c>T</c>.</param>
/// <param name="Path">The path after the change.</param>
/// <param name="OldPath">The path before a rename or copy, or null.</param>
internal sealed record FileChange(char Status, string Path, string? OldPath = null);

/// <summary>A commit with its full message and the files it changed, compared with its first parent.</summary>
internal sealed record CommitDetails(GitCommit Commit, string Message, IReadOnlyList<FileChange> Files);

/// <summary>Builds <c>git log</c> commands and reads their output.</summary>
internal static class LogParser
{
    /// <summary>The format: fields separated by NUL, with <c>-z</c> putting a NUL between commits too.</summary>
    public const string Format = "--format=%H%x00%P%x00%an%x00%ae%x00%at%x00%cn%x00%ce%x00%ct%x00%D%x00%s";

    private const int FieldCount = 10;

    /// <summary>Gets the arguments of <c>git log</c> for a query.</summary>
    public static IReadOnlyList<string> Arguments(LogQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var arguments = new List<string> { "--literal-pathspecs", "log", "-z", Format, "--decorate=full", "--date-order", $"--skip={query.Skip}", $"--max-count={query.Count}" };
        if (!string.IsNullOrEmpty(query.Author))
            arguments.Add($"--author={query.Author}");
        if (!string.IsNullOrEmpty(query.Text))
        {
            arguments.Add($"--grep={query.Text}");
            arguments.Add("--regexp-ignore-case");
        }

        if (!string.IsNullOrEmpty(query.Author) || !string.IsNullOrEmpty(query.Text))
            arguments.Add("--fixed-strings");

        if (query.Follow && !string.IsNullOrEmpty(query.Path))
            arguments.Add("--follow");
        else if (!string.IsNullOrEmpty(query.Path))
            arguments.Add("--parents");

        if (query.Revision is { Length: > 0 } revision)
            arguments.AddRange(revision.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        else
            arguments.AddRange(["--branches", "--remotes", "--tags", "HEAD"]);

        arguments.Add("--");
        if (!string.IsNullOrEmpty(query.Path))
            arguments.Add(query.Path);
        return arguments;
    }

    /// <summary>Parses the output of <c>git log</c> with <see cref="Format"/> and <c>-z</c>.</summary>
    public static IReadOnlyList<GitCommit> Parse(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var commits = new List<GitCommit>();
        var fields = output.Split('\0');
        for (var i = 0; i + FieldCount <= fields.Length; i += FieldCount)
        {
            var sha = fields[i].Trim('\n');
            if (sha.Length == 0)
                continue;
            commits.Add(new GitCommit(
                sha,
                fields[i + 1].Split(' ', StringSplitOptions.RemoveEmptyEntries),
                fields[i + 2],
                fields[i + 3],
                Date(fields[i + 4]),
                fields[i + 5],
                fields[i + 6],
                Date(fields[i + 7]),
                fields[i + 9],
                ParseRefs(fields[i + 8])));
        }

        return commits;
    }

    /// <summary>Reads the decorations of <c>%D</c> with <c>--decorate=full</c>, such as <c>HEAD -&gt; refs/heads/main, tag: refs/tags/v1</c>.</summary>
    public static IReadOnlyList<CommitRef> ParseRefs(string decorations)
    {
        ArgumentNullException.ThrowIfNull(decorations);
        if (decorations.Length == 0)
            return [];

        var refs = new List<CommitRef>();
        foreach (var part in decorations.Split(", ", StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == "HEAD")
            {
                refs.Add(new CommitRef("HEAD", null, true));
                continue;
            }

            var isHead = part.StartsWith("HEAD -> ", StringComparison.Ordinal);
            var name = isHead ? part[8..] : part.StartsWith("tag: ", StringComparison.Ordinal) ? part[5..] : part;
            if (GitRef.KindOf(name) is not { } kind || name.EndsWith("/HEAD", StringComparison.Ordinal))
                continue;
            refs.Add(new CommitRef(GitRef.ShortName(name), kind, isHead));
        }

        return refs;
    }

    /// <summary>Parses the output of <c>git diff-tree -z -r --name-status</c>.</summary>
    public static IReadOnlyList<FileChange> ParseNameStatus(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var changes = new List<FileChange>();
        var fields = output.Split('\0');
        for (var i = 0; i < fields.Length; i++)
        {
            var status = fields[i].Trim('\n');
            if (status.Length == 0 || !char.IsAsciiLetterUpper(status[0]))
                continue;

            if (status[0] is 'R' or 'C' && i + 2 < fields.Length)
            {
                changes.Add(new FileChange(status[0], fields[i + 2], fields[i + 1]));
                i += 2;
            }
            else if (i + 1 < fields.Length)
            {
                changes.Add(new FileChange(status[0], fields[i + 1]));
                i++;
            }
        }

        return changes;
    }

    private static DateTimeOffset Date(string seconds) =>
        long.TryParse(seconds, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? DateTimeOffset.FromUnixTimeSeconds(value) : DateTimeOffset.MinValue;
}
