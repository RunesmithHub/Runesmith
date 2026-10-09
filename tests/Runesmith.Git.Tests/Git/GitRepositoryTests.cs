using Runesmith.Git.Git;
using Runesmith.Sdk.VersionControl;

namespace Runesmith.Git.Tests.Git;

public sealed class GitRepositoryTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FindsTheRepositoryOfAFolderInsideIt()
    {
        using var temp = await TempRepository.CreateAsync();
        temp.Write("src/a.txt", "a");

        var found = await GitRepository.DiscoverAsync(Path.Combine(temp.Path, "src"), null, Token);

        Assert.NotNull(found);
        Assert.Equal(Path.GetFullPath(temp.Path), found.Root);
        Assert.Equal(Path.Combine(Path.GetFullPath(temp.Path), ".git"), found.GitDirectory);
    }

    [Fact]
    public async Task FindsNoRepositoryInAPlainFolder()
    {
        var folder = Directory.CreateTempSubdirectory("runesmith-plain-").FullName;
        try
        {
            Assert.Null(await GitRepository.DiscoverAsync(folder, null, Token));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ReadsTheStatusOfAnEmptyRepository()
    {
        using var temp = await TempRepository.CreateAsync();
        temp.Write("new.txt", "new");

        var status = await temp.Repository.GetStatusAsync(Token);

        Assert.Null(status.Branch.Oid);
        Assert.Equal("main", status.Branch.Head);
        Assert.Equal([new StatusEntry("new.txt", '?', '?', StatusKind.Untracked)], status.Entries);
        Assert.False(await temp.Repository.HasCommitsAsync(Token));
        Assert.Empty(await temp.Repository.GetLogAsync(new LogQuery(), Token));
        Assert.Null(await temp.Repository.GetLastMessageAsync(Token));
    }

    [Fact]
    public async Task StagesAndUnstagesInARepositoryWithoutCommits()
    {
        using var temp = await TempRepository.CreateAsync();
        temp.Write("a.txt", "a");

        await temp.Repository.StageAsync(["a.txt"], Token);
        Assert.Equal('A', (await temp.Repository.GetStatusAsync(Token)).Entries.Single().Index);

        await temp.Repository.UnstageAsync(["a.txt"], Token);
        Assert.Equal(StatusKind.Untracked, (await temp.Repository.GetStatusAsync(Token)).Entries.Single().Kind);
    }

    [Fact]
    public async Task ReadsStagedUnstagedRenamedAndUntrackedFiles()
    {
        using var temp = await TempRepository.CreateAsync();
        temp.Write("keep.txt", "keep\n");
        temp.Write("both.txt", "both\n");
        await temp.CommitFileAsync("old.txt", "a file long enough to be found again after a rename\n", "first");
        await temp.GitAsync("mv", "old.txt", "renamed.txt");
        temp.Write("keep.txt", "changed\n");
        temp.Write("both.txt", "staged\n");
        await temp.GitAsync("add", "both.txt");
        temp.Write("both.txt", "staged and then changed\n");
        temp.Write("dir/untracked file.txt", "new\n");

        var status = await temp.Repository.GetStatusAsync(Token);

        Assert.Equal(new StatusEntry("renamed.txt", 'R', '.', StatusKind.Renamed) { OriginalPath = "old.txt" }, status.Entries.Single(e => e.Path == "renamed.txt"));
        Assert.Equal(['M', 'M'], [.. status.Entries.Where(e => e.Path == "both.txt").Select(e => e.Index).Concat(status.Entries.Where(e => e.Path == "both.txt").Select(e => e.WorkTree))]);
        Assert.Equal(["both.txt", "renamed.txt"], status.Staged.Select(e => e.Path).Order());
        Assert.Equal(["both.txt", "keep.txt"], status.Unstaged.Select(e => e.Path).Order());
        Assert.Equal(["dir/untracked file.txt"], status.Untracked.Select(e => e.Path));
    }

    [Fact]
    public async Task StagesManyFilesAtOnceWithoutWaitingOnTheIndexLock()
    {
        using var temp = await TempRepository.CreateAsync();
        for (var i = 0; i < 20; i++)
            temp.Write($"f{i}.txt", i.ToString(System.Globalization.CultureInfo.InvariantCulture));

        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => temp.Repository.StageAsync([$"f{i}.txt"], Token)));

        Assert.Equal(20, (await temp.Repository.GetStatusAsync(Token)).Staged.Count());
    }

    [Fact]
    public async Task RollsBackChangesRenamesAndNewFiles()
    {
        using var temp = await TempRepository.CreateAsync();
        temp.Write("a.txt", "a\n");
        await temp.CommitFileAsync("old.txt", "a file long enough to be found again after a rename\n", "first");
        temp.Write("a.txt", "changed\n");
        await temp.GitAsync("mv", "old.txt", "new.txt");
        temp.Write("added.txt", "added\n");
        await temp.GitAsync("add", "added.txt");
        temp.Write("untracked.txt", "untracked\n");

        await temp.Repository.DiscardAsync((await temp.Repository.GetStatusAsync(Token)).Entries, Token);

        var status = await temp.Repository.GetStatusAsync(Token);
        Assert.Equal("a\n", temp.Read("a.txt"));
        Assert.True(temp.Exists("old.txt"));
        Assert.False(temp.Exists("new.txt"));
        Assert.False(temp.Exists("untracked.txt"));
        Assert.Equal([new StatusEntry("added.txt", '?', '?', StatusKind.Untracked)], status.Entries);
    }

    [Fact]
    public async Task CommitsAMessageFromStandardInputAndAmendsIt()
    {
        using var temp = await TempRepository.CreateAsync();
        temp.Write("a.txt", "a");
        await temp.Repository.StageAsync(["a.txt"], Token);

        var first = await temp.Repository.CommitAsync("Subject with \"quotes\"\n\nA body line.", new CommitOptions(Signoff: true), Token);
        Assert.Equal("Subject with \"quotes\"\n\nA body line.\n\nSigned-off-by: Ada Lovelace <ada@example.com>", await temp.Repository.GetLastMessageAsync(Token));

        var amended = await temp.Repository.CommitAsync("Better subject", new CommitOptions(Amend: true), Token);
        Assert.NotEqual(first, amended);
        Assert.Equal("Better subject", await temp.Repository.GetLastMessageAsync(Token));
        Assert.Single(await temp.Repository.GetLogAsync(new LogQuery(), Token));
        Assert.Equal(["Better subject"], await temp.Repository.GetRecentMessagesAsync(10, Token));
    }

    [Fact]
    public async Task SkipsHooksWhenAsked()
    {
        using var temp = await TempRepository.CreateAsync();
        var hook = Path.Combine(temp.Path, ".git", "hooks", "pre-commit");
        Directory.CreateDirectory(Path.GetDirectoryName(hook)!);
        await File.WriteAllTextAsync(hook, "#!/bin/sh\necho 'hook says no' >&2\nexit 1\n", Token);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        temp.Write("a.txt", "a");
        await temp.Repository.StageAsync(["a.txt"], Token);

        var refused = await Assert.ThrowsAsync<GitException>(() => temp.Repository.CommitAsync("Message", new CommitOptions(), Token));
        Assert.Contains("hook says no", refused.Message, StringComparison.Ordinal);

        await temp.Repository.CommitAsync("Message", new CommitOptions(NoVerify: true), Token);
        Assert.True(await temp.Repository.HasCommitsAsync(Token));
    }

    [Fact]
    public async Task SaysWhenNothingIsStaged()
    {
        using var temp = await TempRepository.CreateAsync();
        await temp.CommitFileAsync("a.txt", "a", "first");

        var exception = await Assert.ThrowsAsync<GitException>(() => temp.Repository.CommitAsync("Empty", new CommitOptions(), Token));

        Assert.Equal(GitErrorKind.NothingToCommit, exception.Kind);
    }

    [Fact]
    public async Task CreatesRenamesAndDeletesBranches()
    {
        using var temp = await TempRepository.CreateAsync();
        await temp.CommitFileAsync("a.txt", "a", "first");

        await temp.Repository.CreateBranchAsync("feature", null, checkout: true, Token);
        await temp.CommitFileAsync("b.txt", "b", "on feature");
        await temp.Repository.RenameBranchAsync("feature", "feature-2", Token);
        await temp.Repository.CheckoutAsync(new GitRef("refs/heads/main", RefKind.LocalBranch, ""), force: false, Token);

        var unmerged = await Assert.ThrowsAsync<GitException>(() => temp.Repository.DeleteBranchAsync("feature-2", force: false, Token));
        Assert.Equal(GitErrorKind.BranchNotFullyMerged, unmerged.Kind);
        await temp.Repository.DeleteBranchAsync("feature-2", force: true, Token);

        var refs = await temp.Repository.GetRefsAsync(Token);
        Assert.Equal(["main"], refs.Select(r => r.Name));
        Assert.True(refs[0].IsHead);
        Assert.False(await temp.Repository.IsValidBranchNameAsync("bad..name", Token));
        Assert.True(await temp.Repository.IsValidBranchNameAsync("feature/good-name", Token));
    }

    [Fact]
    public async Task RemembersTheBranchesCheckedOutMostRecently()
    {
        using var temp = await TempRepository.CreateAsync();
        await temp.CommitFileAsync("a.txt", "a", "first");
        foreach (var name in new[] { "one", "two", "three" })
            await temp.Repository.CreateBranchAsync(name, null, checkout: false, Token);
        foreach (var name in new[] { "one", "three", "two", "main" })
            await temp.GitAsync("switch", "--quiet", name);

        Assert.Equal(["main", "two", "three"], await temp.Repository.GetRecentBranchesAsync(3, Token));
    }

    [Fact]
    public async Task RefusesACheckoutThatWouldOverwriteLocalChanges()
    {
        using var temp = await TempRepository.CreateAsync();
        await temp.CommitFileAsync("a.txt", "main\n", "first");
        await temp.Repository.CreateBranchAsync("other", null, checkout: true, Token);
        await temp.CommitFileAsync("a.txt", "other\n", "change on other");
        await temp.Repository.CheckoutAsync(new GitRef("refs/heads/main", RefKind.LocalBranch, ""), force: false, Token);
        temp.Write("a.txt", "local change\n");
        var other = new GitRef("refs/heads/other", RefKind.LocalBranch, "");

        var exception = await Assert.ThrowsAsync<GitException>(() => temp.Repository.CheckoutAsync(other, force: false, Token));
        Assert.Equal(GitErrorKind.LocalChangesWouldBeOverwritten, exception.Kind);

        await temp.Repository.StashAsync("before switching", Token);
        await temp.Repository.CheckoutAsync(other, force: false, Token);
        Assert.Equal("On main: before switching", Assert.Single(await temp.Repository.GetStashesAsync(Token)).Message);
        await temp.Repository.CheckoutAsync(new GitRef("refs/heads/main", RefKind.LocalBranch, ""), force: false, Token);
        await temp.Repository.UnstashAsync(0, Token);
        Assert.Equal("local change\n", temp.Read("a.txt"));
        Assert.Empty(await temp.Repository.GetStashesAsync(Token));
    }

    [Fact]
    public async Task ChecksOutATagOrCommitWithADetachedHead()
    {
        using var temp = await TempRepository.CreateAsync();
        var first = await temp.CommitFileAsync("a.txt", "1", "first");
        await temp.GitAsync("tag", "-a", "v1", "-m", "Version 1");
        await temp.CommitFileAsync("a.txt", "2", "second");

        await temp.Repository.CheckoutAsync((await temp.Repository.GetRefsAsync(Token)).Single(r => r.Kind == RefKind.Tag), force: false, Token);

        var status = await temp.Repository.GetStatusAsync(Token);
        Assert.Null(status.Branch.Head);
        Assert.Equal(first, status.Branch.Oid);
        var log = await temp.Repository.GetLogAsync(new LogQuery { Revision = "HEAD" }, Token);
        Assert.Contains(new CommitRef("HEAD", null, true), log[0].Refs);
        Assert.Contains(new CommitRef("v1", RefKind.Tag, false), log[0].Refs);
    }

    [Fact]
    public async Task MergesCherryPicksRevertsAndResets()
    {
        using var temp = await TempRepository.CreateAsync();
        await temp.CommitFileAsync("a.txt", "a", "first");
        await temp.Repository.CreateBranchAsync("feature", null, checkout: true, Token);
        var picked = await temp.CommitFileAsync("b.txt", "b", "feature work");
        await temp.CommitFileAsync("c.txt", "c", "more feature work");
        await temp.Repository.CheckoutAsync(new GitRef("refs/heads/main", RefKind.LocalBranch, ""), force: false, Token);
        await temp.CommitFileAsync("d.txt", "d", "main work");

        await temp.Repository.MergeAsync("feature", Token);
        var merge = (await temp.Repository.GetLogAsync(new LogQuery { Revision = "HEAD", Count = 1 }, Token))[0];
        Assert.True(merge.IsMerge);

        await temp.Repository.ResetAsync(merge.Parents[0], ResetMode.Hard, Token);
        Assert.False(temp.Exists("b.txt"));
        var commits = await temp.Repository.GetLogAsync(new LogQuery { Revision = "feature" }, Token);
        await temp.Repository.CherryPickAsync(commits.Single(c => c.Sha == picked), Token);
        Assert.True(temp.Exists("b.txt"));

        var head = (await temp.Repository.GetLogAsync(new LogQuery { Revision = "HEAD", Count = 1 }, Token))[0];
        await temp.Repository.RevertAsync(head, Token);
        Assert.False(temp.Exists("b.txt"));

        await temp.Repository.ResetAsync(head.Sha, ResetMode.Soft, Token);
        Assert.Equal('D', (await temp.Repository.GetStatusAsync(Token)).Entries.Single().Index);
        await temp.Repository.ResetAsync(head.Sha, ResetMode.Mixed, Token);
        Assert.Equal('D', (await temp.Repository.GetStatusAsync(Token)).Entries.Single().WorkTree);
    }

    [Fact]
    public async Task ReportsConflictsAndAbortsTheMerge()
    {
        using var temp = await TempRepository.CreateAsync();
        await temp.CommitFileAsync("a.txt", "base\n", "first");
        await temp.Repository.CreateBranchAsync("other", null, checkout: true, Token);
        await temp.CommitFileAsync("a.txt", "theirs\n", "theirs");
        await temp.Repository.CheckoutAsync(new GitRef("refs/heads/main", RefKind.LocalBranch, ""), force: false, Token);
        await temp.CommitFileAsync("a.txt", "ours\n", "ours");

        var exception = await Assert.ThrowsAsync<GitException>(() => temp.Repository.MergeAsync("other", Token));

        Assert.Equal(GitErrorKind.Conflict, exception.Kind);
        Assert.Equal(OngoingOperation.Merge, temp.Repository.GetOngoingOperation());
        Assert.Equal(new StatusEntry("a.txt", 'U', 'U', StatusKind.Unmerged), (await temp.Repository.GetStatusAsync(Token)).Conflicts.Single());
        await temp.Repository.AbortAsync(OngoingOperation.Merge, Token);
        Assert.Equal(OngoingOperation.None, temp.Repository.GetOngoingOperation());
        Assert.Equal("ours\n", temp.Read("a.txt"));
    }

    [Fact]
    public async Task ReadsCommitDetailsAndFilesAtRevisions()
    {
        using var temp = await TempRepository.CreateAsync();
        var root = await temp.CommitFileAsync("old.txt", "a file long enough to be found again after a rename\n", "first\n\nWith a body.");
        await temp.GitAsync("mv", "old.txt", "new.txt");
        temp.Write("other.txt", "other\n");
        await temp.GitAsync("add", "--all");
        await temp.GitAsync("commit", "--quiet", "-m", "rename");

        var rootDetails = await temp.Repository.GetCommitAsync(root, Token);
        var details = await temp.Repository.GetCommitAsync("HEAD", Token);

        Assert.Equal("first\n\nWith a body.", rootDetails.Message);
        Assert.Equal([new FileChange('A', "old.txt")], rootDetails.Files);
        Assert.Equal([new FileChange('R', "new.txt", "old.txt"), new FileChange('A', "other.txt")], details.Files);
        Assert.Equal("Ada Lovelace", details.Commit.AuthorName);
        Assert.Equal("a file long enough to be found again after a rename\n", await temp.Repository.GetFileAsync(root, "old.txt", Token));
        Assert.Null(await temp.Repository.GetFileAsync("HEAD", "old.txt", Token));

        var tree = await temp.Repository.GetTreeAsync("HEAD", Token);
        Assert.Equal(["new.txt", "other.txt"], tree.Keys.Order());
        Assert.Equal("other\n", await temp.Repository.GetBlobAsync(tree["other.txt"], Token));
    }

    [Fact]
    public async Task PagesAndFiltersTheLog()
    {
        using var temp = await TempRepository.CreateAsync();
        for (var i = 0; i < 5; i++)
            await temp.CommitFileAsync("a.txt", $"{i}\n", $"change {i}");
        await temp.GitAsync("mv", "a.txt", "b.txt");
        await temp.GitAsync("commit", "--quiet", "-m", "rename a to b");
        await temp.GitAsync("-c", "user.name=Grace Hopper", "-c", "user.email=grace@example.com", "commit", "--quiet", "--allow-empty", "-m", "Grace FIXES it");

        var first = await temp.Repository.GetLogAsync(new LogQuery { Count = 3 }, Token);
        var second = await temp.Repository.GetLogAsync(new LogQuery { Count = 3, Skip = 3 }, Token);
        var byAuthor = await temp.Repository.GetLogAsync(new LogQuery { Author = "grace" }, Token);
        var byText = await temp.Repository.GetLogAsync(new LogQuery { Text = "fixes" }, Token);
        var followed = await temp.Repository.GetLogAsync(new LogQuery { Path = "b.txt", Follow = true }, Token);
        var notFollowed = await temp.Repository.GetLogAsync(new LogQuery { Path = "b.txt" }, Token);

        Assert.Equal(["Grace FIXES it", "rename a to b", "change 4"], first.Select(c => c.Subject));
        Assert.Equal(["change 3", "change 2", "change 1"], second.Select(c => c.Subject));
        Assert.Equal(["Grace FIXES it"], byAuthor.Select(c => c.Subject));
        Assert.Equal(["Grace FIXES it"], byText.Select(c => c.Subject));
        Assert.Equal(6, followed.Count);
        Assert.Equal(["rename a to b"], notFollowed.Select(c => c.Subject));
        Assert.Equal(first[1].Sha, first[0].Parents.Single());
        Assert.Equal("grace@example.com", first[0].AuthorEmail);
    }

    [Fact]
    public async Task FetchesPullsAndPushesWithALocalRemote()
    {
        var remote = await TempRepository.CreateBareAsync();
        try
        {
            using var mine = await TempRepository.CreateAsync();
            await mine.GitAsync("remote", "add", "origin", remote);
            await mine.CommitFileAsync("a.txt", "a\n", "first");

            await mine.Repository.PushAsync("main", "origin", "main", setUpstream: true, forceWithLease: false, Token);
            Assert.Equal(("origin", "main", true), await mine.Repository.GetPushTargetAsync("main", Token));
            Assert.Equal("origin/main", (await mine.Repository.GetStatusAsync(Token)).Branch.Upstream);

            using var theirs = await TempRepository.CloneAsync(remote);
            await theirs.CommitFileAsync("b.txt", "b\n", "theirs");
            await theirs.GitAsync("push", "--quiet", "origin", "main");

            await mine.Repository.FetchAsync(Token);
            Assert.Equal(1, (await mine.Repository.GetStatusAsync(Token)).Branch.Behind);

            await mine.CommitFileAsync("c.txt", "c\n", "mine");
            var rejected = await Assert.ThrowsAsync<GitException>(() => mine.Repository.PushAsync("main", "origin", "main", false, false, Token));
            Assert.Equal(GitErrorKind.PushRejected, rejected.Kind);

            mine.Write("a.txt", "a local change the pull keeps\n");
            await mine.Repository.PullAsync(PullStrategy.Rebase, Token);
            var status = await mine.Repository.GetStatusAsync(Token);
            Assert.Equal((1, 0), (status.Branch.Ahead, status.Branch.Behind));
            Assert.Equal("a local change the pull keeps\n", mine.Read("a.txt"));
            Assert.False((await mine.Repository.GetLogAsync(new LogQuery { Revision = "HEAD", Count = 1 }, Token))[0].IsMerge);

            await mine.Repository.PushAsync("main", "origin", "main", false, false, Token);
            await mine.Repository.FetchAsync(Token);
            Assert.Equal((0, 0), ((await mine.Repository.GetStatusAsync(Token)).Branch.Ahead, (await mine.Repository.GetStatusAsync(Token)).Branch.Behind));
        }
        finally
        {
            TempRepository.Delete(remote);
        }
    }

    [Fact]
    public async Task PullsWithAMergeWhenTheUsersConfigurationSaysNothing()
    {
        var remote = await TempRepository.CreateBareAsync();
        try
        {
            using var mine = await TempRepository.CloneAsync(remote);
            await mine.CommitFileAsync("a.txt", "a\n", "first");
            await mine.GitAsync("push", "--quiet", "--set-upstream", "origin", "main");
            using var theirs = await TempRepository.CloneAsync(remote);
            await theirs.CommitFileAsync("b.txt", "b\n", "theirs");
            await theirs.GitAsync("push", "--quiet", "origin", "main");
            await mine.CommitFileAsync("c.txt", "c\n", "mine");

            await mine.Repository.PullAsync(PullStrategy.UserConfig, Token);

            Assert.True((await mine.Repository.GetLogAsync(new LogQuery { Revision = "HEAD", Count = 1 }, Token))[0].IsMerge);
        }
        finally
        {
            TempRepository.Delete(remote);
        }
    }

    [Fact]
    public async Task ForcePushesOnlyWhenTheRemoteIsWhereItWasLastSeen()
    {
        var remote = await TempRepository.CreateBareAsync();
        try
        {
            using var mine = await TempRepository.CloneAsync(remote);
            await mine.CommitFileAsync("a.txt", "a\n", "first");
            await mine.GitAsync("push", "--quiet", "--set-upstream", "origin", "main");
            await mine.CommitFileAsync("a.txt", "b\n", "second");
            await mine.GitAsync("push", "--quiet");

            await mine.Repository.ResetAsync("HEAD~1", ResetMode.Hard, Token);
            await mine.CommitFileAsync("a.txt", "c\n", "rewritten");
            await mine.Repository.PushAsync("main", "origin", "main", false, forceWithLease: true, Token);

            using var theirs = await TempRepository.CloneAsync(remote);
            Assert.Equal("c\n", theirs.Read("a.txt"));
            await theirs.CommitFileAsync("a.txt", "d\n", "theirs");
            await theirs.GitAsync("push", "--quiet");
            await mine.CommitFileAsync("a.txt", "e\n", "mine again");
            await mine.Repository.ResetAsync("HEAD~1", ResetMode.Soft, Token);
            await mine.Repository.CommitAsync("amended", new CommitOptions(), Token);
            await mine.GitAsync("update-ref", "refs/remotes/origin/main", "HEAD~1");

            var stale = await Assert.ThrowsAsync<GitException>(() => mine.Repository.PushAsync("main", "origin", "main", false, forceWithLease: true, Token));
            Assert.Equal(GitErrorKind.StaleRemote, stale.Kind);
        }
        finally
        {
            TempRepository.Delete(remote);
        }
    }

    [Fact]
    public async Task ChecksOutARemoteBranchAsATrackingBranchAndDeletesItOnTheRemote()
    {
        var remote = await TempRepository.CreateBareAsync();
        try
        {
            using var theirs = await TempRepository.CloneAsync(remote);
            await theirs.CommitFileAsync("a.txt", "a\n", "first");
            await theirs.GitAsync("push", "--quiet", "origin", "main");
            await theirs.GitAsync("switch", "--quiet", "-c", "feature");
            await theirs.CommitFileAsync("f.txt", "f\n", "feature");
            await theirs.GitAsync("push", "--quiet", "origin", "feature");
            using var mine = await TempRepository.CloneAsync(remote);

            var feature = (await mine.Repository.GetRefsAsync(Token)).Single(r => r.Name == "origin/feature");
            await mine.Repository.CheckoutAsync(feature, force: false, Token);

            var local = (await mine.Repository.GetRefsAsync(Token)).Single(r => r.Name == "feature");
            Assert.Equal(("origin/feature", true), (local.Upstream, local.IsHead));

            await mine.Repository.CheckoutAsync(new GitRef("refs/heads/main", RefKind.LocalBranch, ""), force: false, Token);
            await mine.Repository.DeleteRemoteBranchAsync("origin", "feature", Token);
            await mine.Repository.FetchAsync(Token);
            Assert.True((await mine.Repository.GetRefsAsync(Token)).Single(r => r.Name == "feature").IsUpstreamGone);
        }
        finally
        {
            TempRepository.Delete(remote);
        }
    }

    [Fact]
    public async Task AsksTheCredentialSourcesOnlyForRemotesOverHttps()
    {
        var source = new FakeCredentialSource();
        using var temp = await TempRepository.CreateAsync([source]);
        await temp.GitAsync("remote", "add", "origin", "https://github.com/owner/repo.git");
        await temp.GitAsync("remote", "add", "other", "https://example.com/owner/repo.git");
        await temp.GitAsync("remote", "add", "ssh", "git@github.com:owner/repo.git");

        Assert.Equal("token", (await temp.Repository.CredentialsForAsync(["origin"], Token))?.Token);
        Assert.Null(await temp.Repository.CredentialsForAsync(["other", "ssh"], Token));
        Assert.Equal(["https://github.com/owner/repo.git", "https://example.com/owner/repo.git"], source.Asked);
        Assert.Equal(["origin", "other", "ssh"], (await temp.Repository.GetRemotesAsync(Token)).Select(r => r.Name));
    }

    private sealed class FakeCredentialSource : IGitCredentialSource
    {
        public List<string> Asked { get; } = [];

        public Task<GitCredentials?> GetAsync(string remoteUrl, CancellationToken cancellationToken)
        {
            Asked.Add(remoteUrl);
            return Task.FromResult(remoteUrl.StartsWith("https://github.com/", StringComparison.Ordinal) ? new GitCredentials("github.com", "x-access-token", "token") : null);
        }
    }
}
