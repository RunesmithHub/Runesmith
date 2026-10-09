using Runesmith.Git.Editor;
using Runesmith.Git.Repositories;
using Runesmith.Git.Tests.Git;

namespace Runesmith.Git.Tests.Repositories;

public sealed class ChangeBaseProviderTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GivesHeadsVersionEmptyForNewFilesAndNothingOutside()
    {
        using var temp = await TempRepository.CreateAsync();
        await temp.CommitFileAsync("src/a.txt", "line 1\r\nline 2\r\n", "first");
        temp.Write(".gitignore", "*.log\n");
        temp.Write("src/a.txt", "changed\n");
        temp.Write("new.txt", "new\n");
        temp.Write("debug.log", "ignored\n");
        var (service, provider) = await OpenAsync(temp.Path);
        using var _ = service;

        Assert.Equal("line 1\nline 2\n", await provider.GetBaseTextAsync(Path.Combine(temp.Path, "src", "a.txt"), Token));
        Assert.Equal("", await provider.GetBaseTextAsync(Path.Combine(temp.Path, "new.txt"), Token));
        Assert.Null(await provider.GetBaseTextAsync(Path.Combine(temp.Path, "debug.log"), Token));
        Assert.Null(await provider.GetBaseTextAsync(Path.Combine(Path.GetTempPath(), "elsewhere.txt"), Token));
        Assert.Null(await provider.GetBaseTextAsync(Path.Combine(temp.Path, ".git", "HEAD"), Token));
    }

    [Fact]
    public async Task SaysTheBaseChangedAfterACommitAndGivesTheNewOne()
    {
        using var temp = await TempRepository.CreateAsync();
        await temp.CommitFileAsync("a.txt", "one\n", "first");
        var (service, provider) = await OpenAsync(temp.Path);
        using var _ = service;
        var file = Path.Combine(temp.Path, "a.txt");
        Assert.Equal("one\n", await provider.GetBaseTextAsync(file, Token));
        var changed = new TaskCompletionSource<IReadOnlyList<string>?>();
        provider.BaseTextChanged += (_, e) => changed.TrySetResult(e.FilePaths);

        await temp.CommitFileAsync("a.txt", "two\n", "second");
        await service.RefreshAsync(Token);

        Assert.Null(await changed.Task.WaitAsync(TimeSpan.FromSeconds(5), Token));
        Assert.Equal("two\n", await provider.GetBaseTextAsync(file, Token));
    }

    [Fact]
    public async Task GivesEveryFileAnEmptyBaseBeforeTheFirstCommit()
    {
        using var temp = await TempRepository.CreateAsync();
        temp.Write("a.txt", "a\n");
        var (service, provider) = await OpenAsync(temp.Path);
        using var _ = service;

        Assert.Equal("", await provider.GetBaseTextAsync(Path.Combine(temp.Path, "a.txt"), Token));
    }

    private static async Task<(RepositoryService Service, ChangeBaseProvider Provider)> OpenAsync(string folder)
    {
        var workspace = new FakeWorkspace();
        var service = new RepositoryService(workspace, null, null, action => action(), TimeSpan.FromMilliseconds(50));
        workspace.Open(folder);
        await service.Opened;
        return (service, new ChangeBaseProvider(service));
    }
}
