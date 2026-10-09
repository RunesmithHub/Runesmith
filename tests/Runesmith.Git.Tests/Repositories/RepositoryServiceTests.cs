using Runesmith.Git.Git;
using Runesmith.Git.Repositories;
using Runesmith.Git.Tests.Git;
using Runesmith.Sdk.Messaging;

namespace Runesmith.Git.Tests.Repositories;

public sealed class RepositoryServiceTests
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(50);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FollowsTheOpenFolder()
    {
        using var temp = await TempRepository.CreateAsync();
        await temp.CommitFileAsync("a.txt", "a", "first");
        temp.Write("b.txt", "b");
        var workspace = new FakeWorkspace();
        using var service = new RepositoryService(workspace, null, null, action => action(), Debounce);
        var changes = 0;
        service.Changed += (_, _) => changes++;

        workspace.Open(temp.Path);
        await service.Opened;

        Assert.Equal(Path.GetFullPath(temp.Path), service.Current?.Root);
        Assert.Equal("main", service.Current?.Branch);
        Assert.Equal(["b.txt"], service.Status!.Untracked.Select(e => e.Path));
        Assert.Equal(1, changes);

        workspace.Open(null);
        await service.Opened;

        Assert.Null(service.Current);
        Assert.Null(service.Repository);
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task NoticesARepositoryCreatedInTheOpenFolder()
    {
        var folder = Directory.CreateTempSubdirectory("runesmith-plain-").FullName;
        try
        {
            var workspace = new FakeWorkspace();
            using var service = new RepositoryService(workspace, null, null, action => action(), Debounce);
            workspace.Open(folder);
            await service.Opened;
            Assert.Null(service.Current);

            await GitRepository.InitAsync(folder, Token);

            await WaitUntilAsync(() => service.Current is not null);
            Assert.Equal(Path.GetFullPath(folder), service.Current!.Root);
            Assert.Null(service.Current.Head);
        }
        finally
        {
            TempRepository.Delete(folder);
        }
    }

    [Fact]
    public async Task NoticesARepositoryWhoseGitFolderExistedBeforeGitFinishedIt()
    {
        var folder = Directory.CreateTempSubdirectory("runesmith-plain-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, ".git"));
            var workspace = new FakeWorkspace();
            using var service = new RepositoryService(workspace, null, null, action => action(), Debounce);
            workspace.Open(folder);
            await service.Opened;
            Assert.Null(service.Current);

            await GitRepository.InitAsync(folder, Token);

            await WaitUntilAsync(() => service.Current is not null);
            Assert.Equal(Path.GetFullPath(folder), service.Current!.Root);
        }
        finally
        {
            TempRepository.Delete(folder);
        }
    }

    [Fact]
    public async Task RefreshesAfterTheIndexOrHeadChangesAndSaysWhenHeadMoved()
    {
        using var temp = await TempRepository.CreateAsync();
        await temp.CommitFileAsync("a.txt", "a", "first");
        var workspace = new FakeWorkspace();
        using var service = new RepositoryService(workspace, null, null, action => action(), Debounce);
        workspace.Open(temp.Path);
        await service.Opened;
        var headMoves = 0;
        var indexChanges = 0;
        service.HeadMoved += (_, _) => Interlocked.Increment(ref headMoves);
        service.IndexChanged += (_, _) => Interlocked.Increment(ref indexChanges);

        temp.Write("a.txt", "changed");
        await temp.GitAsync("add", "a.txt");
        await WaitUntilAsync(() => service.Status?.Staged.Any() == true);

        await temp.GitAsync("commit", "--quiet", "-m", "second");
        await WaitUntilAsync(() => Volatile.Read(ref headMoves) > 0);
        Assert.Empty(service.Status!.Entries);
        Assert.True(Volatile.Read(ref indexChanges) > 0);
    }

    [Fact]
    public async Task RefreshesWhenTheWorkspaceSaysFilesChanged()
    {
        using var temp = await TempRepository.CreateAsync();
        await temp.CommitFileAsync("a.txt", "a", "first");
        var bus = new MessageBus();
        var workspace = new FakeWorkspace();
        using var service = new RepositoryService(workspace, bus, null, action => action(), Debounce);
        workspace.Open(temp.Path);
        await service.Opened;

        temp.Write("new.txt", "new");
        bus.Publish(new FilesChangedMessage([Path.Combine(temp.Path, "new.txt")]));

        await WaitUntilAsync(() => service.Status?.Untracked.Any() == true);
    }

    [Fact]
    public async Task AnswersRefreshesAskedForTogetherWithOneReadAtATime()
    {
        using var temp = await TempRepository.CreateAsync();
        await temp.CommitFileAsync("a.txt", "a", "first");
        var workspace = new FakeWorkspace();
        using var service = new RepositoryService(workspace, null, null, action => action(), Debounce);
        workspace.Open(temp.Path);
        await service.Opened;
        temp.Write("a.txt", "changed");

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => service.RefreshAsync(Token)));

        Assert.Equal(["a.txt"], service.Status!.Unstaged.Select(e => e.Path));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(25, Token);
        Assert.True(condition(), "The condition did not become true in five seconds.");
    }
}
