using Runesmith.Git.Git;

namespace Runesmith.Git.Tests.Git;

public sealed class ParserTests
{
    [Fact]
    public void ReadsEveryKindOfStatusLine()
    {
        var output = string.Join('\0',
            "# branch.oid 1111111111111111111111111111111111111111",
            "# branch.head feature/x",
            "# branch.upstream origin/feature/x",
            "# branch.ab +2 -3",
            "1 M. N... 100644 100644 100644 aaaa bbbb src/with space.cs",
            "1 .D N... 100644 100644 000000 aaaa aaaa gone.txt",
            "2 R. N... 100644 100644 100644 aaaa aaaa R100 new name.txt",
            "old name.txt",
            "u UU N... 100644 100644 100644 100644 aaaa bbbb cccc both.txt",
            "? new folder/untracked.txt",
            "! bin/ignored.dll",
            "");

        var status = StatusParser.Parse(output);

        Assert.Equal(new BranchStatus("1111111111111111111111111111111111111111", "feature/x", "origin/feature/x", 2, 3), status.Branch);
        Assert.Collection(status.Entries,
            e => Assert.Equal(new StatusEntry("src/with space.cs", 'M', '.', StatusKind.Ordinary), e),
            e => Assert.Equal(new StatusEntry("gone.txt", '.', 'D', StatusKind.Ordinary), e),
            e => Assert.Equal(new StatusEntry("new name.txt", 'R', '.', StatusKind.Renamed) { OriginalPath = "old name.txt" }, e),
            e => Assert.Equal(new StatusEntry("both.txt", 'U', 'U', StatusKind.Unmerged), e),
            e => Assert.Equal(new StatusEntry("new folder/untracked.txt", '?', '?', StatusKind.Untracked), e),
            e => Assert.Equal(new StatusEntry("bin/ignored.dll", '!', '!', StatusKind.Ignored), e));
        Assert.Equal(["src/with space.cs", "new name.txt"], status.Staged.Select(e => e.Path));
        Assert.Equal(["gone.txt"], status.Unstaged.Select(e => e.Path));
        Assert.Equal(5, status.ChangedCount);
    }

    [Fact]
    public void ReadsTheStatusOfARepositoryWithoutCommitsOrADetachedHead()
    {
        Assert.Equal(new BranchStatus(null, "main", null, 0, 0), StatusParser.Parse("# branch.oid (initial)\0# branch.head main\0").Branch);
        Assert.Null(StatusParser.Parse("# branch.oid abc\0# branch.head (detached)\0").Branch.Head);
    }

    [Fact]
    public void ReadsReferencesWithTheirUpstreamsAndLeavesOutSymbolicOnes()
    {
        var output = string.Join('\n',
            "refs/heads/main\0aaa\0\0\0*\0refs/remotes/origin/main\0ahead 1, behind 2\0100\0Subject",
            "refs/heads/old\0bbb\0\0\0 \0refs/remotes/origin/old\0gone\0200\0Other",
            "refs/remotes/origin/HEAD\0aaa\0\0refs/remotes/origin/main\0 \0\0\0100\0Subject",
            "refs/remotes/origin/feature/x\0ccc\0\0\0 \0\0\0300\0Feature",
            "refs/tags/v1\0tagobject\0ddd\0\0 \0\0\0400\0Release",
            "");

        var refs = RefParser.Parse(output);

        Assert.Equal(4, refs.Count);
        var main = refs[0];
        Assert.Equal(("main", RefKind.LocalBranch, true, "origin/main", 1, 2), (main.Name, main.Kind, main.IsHead, main.Upstream, main.Ahead, main.Behind));
        Assert.True(refs[1].IsUpstreamGone);
        Assert.Equal(("origin", "feature/x"), (refs[2].Remote, refs[2].BranchName));
        Assert.Equal(("v1", RefKind.Tag, "ddd"), (refs[3].Name, refs[3].Kind, refs[3].Sha));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(400), refs[3].Date);
    }

    [Fact]
    public void ReadsDecorationsAsLabels()
    {
        var refs = LogParser.ParseRefs("HEAD -> refs/heads/main, refs/remotes/origin/main, refs/remotes/origin/HEAD, tag: refs/tags/v1.0, refs/stash");

        Assert.Equal([new CommitRef("main", RefKind.LocalBranch, true), new CommitRef("origin/main", RefKind.RemoteBranch, false), new CommitRef("v1.0", RefKind.Tag, false)], refs);
        Assert.Equal([new CommitRef("HEAD", null, true)], LogParser.ParseRefs("HEAD"));
    }

    [Fact]
    public void ReadsNameStatusWithRenames()
    {
        var changes = LogParser.ParseNameStatus("M\0a.txt\0R087\0old.txt\0new.txt\0A\0b c.txt\0");

        Assert.Equal([new FileChange('M', "a.txt"), new FileChange('R', "new.txt", "old.txt"), new FileChange('A', "b c.txt")], changes);
    }

    [Fact]
    public void BuildsLogArgumentsForFilters()
    {
        var arguments = LogParser.Arguments(new LogQuery { Author = "ada", Text = "fix", Path = "src/a.cs", Follow = true, Skip = 200, Count = 50 });

        Assert.Contains("--skip=200", arguments);
        Assert.Contains("--max-count=50", arguments);
        Assert.Contains("--author=ada", arguments);
        Assert.Contains("--grep=fix", arguments);
        Assert.Contains("--fixed-strings", arguments);
        Assert.Contains("--follow", arguments);
        Assert.Equal(["--", "src/a.cs"], arguments.TakeLast(2));
        Assert.Equal(["--branches", "--remotes", "--tags", "HEAD", "--"], LogParser.Arguments(new LogQuery()).TakeLast(5));
    }

    [Theory]
    [InlineData("error: Your local changes to the following files would be overwritten by checkout:\n\ta.txt", nameof(GitErrorKind.LocalChangesWouldBeOverwritten))]
    [InlineData("error: The branch 'x' is not fully merged.", nameof(GitErrorKind.BranchNotFullyMerged))]
    [InlineData(" ! [rejected]        main -> main (fetch first)", nameof(GitErrorKind.PushRejected))]
    [InlineData(" ! [rejected]        main -> main (stale info)", nameof(GitErrorKind.StaleRemote))]
    [InlineData("fatal: The current branch x has no upstream branch.", nameof(GitErrorKind.NoUpstream))]
    [InlineData("CONFLICT (content): Merge conflict in a.txt", nameof(GitErrorKind.Conflict))]
    [InlineData("fatal: could not read Username for 'https://github.com': terminal prompts disabled", nameof(GitErrorKind.AuthenticationFailed))]
    [InlineData("fatal: something else", nameof(GitErrorKind.Other))]
    public void RecognizesFailures(string message, string kind) => Assert.Equal(Enum.Parse<GitErrorKind>(kind), GitErrors.Classify(message));
}
