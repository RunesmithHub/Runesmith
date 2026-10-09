using Runesmith.Git.Git;
using Runesmith.Git.Tests.Git;
using Runesmith.Git.Views.History;

namespace Runesmith.Git.Tests.Views;

public sealed class LogSourceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReadsPagesAsTheyAreAskedForAndLaysOutTheGraph()
    {
        using var temp = await TempRepository.CreateAsync();
        await temp.CommitFileAsync("a.txt", "0", "first");
        await temp.GitAsync("switch", "--quiet", "-c", "side");
        await temp.CommitFileAsync("b.txt", "b", "side work");
        await temp.GitAsync("switch", "--quiet", "main");
        for (var i = 1; i <= LogSource.PageSize + 20; i++)
            await temp.GitAsync("commit", "--quiet", "--allow-empty", "-m", $"change {i}");
        await temp.GitAsync("merge", "--quiet", "--no-edit", "side");
        var source = new LogSource(action => action());

        await source.ResetAsync(temp.Repository, new LogQuery());
        Assert.Equal(LogSource.PageSize, source.Entries.Count);
        Assert.False(source.IsComplete);
        Assert.True(source.Entries[0].Commit.IsMerge);
        Assert.Equal(2, source.GraphWidth);

        await source.LoadMoreAsync();
        Assert.Equal(LogSource.PageSize + 23, source.Entries.Count);
        Assert.True(source.IsComplete);
        Assert.Equal(source.Entries.Count, source.Entries.Select(e => e.Commit.Sha).Distinct().Count());
        Assert.All(source.Entries, e => Assert.NotNull(e.Graph));
        Assert.Equal(0, source.IndexOf(source.Entries[0].Commit.Sha));
    }

    [Fact]
    public async Task LeavesOutTheGraphWhenFilteredByTextAndReportsBadRevisions()
    {
        using var temp = await TempRepository.CreateAsync();
        await temp.CommitFileAsync("a.txt", "a", "first");
        await temp.CommitFileAsync("a.txt", "b", "fix the thing");
        var source = new LogSource(action => action());

        await source.ResetAsync(temp.Repository, new LogQuery { Text = "fix" });
        Assert.Null(Assert.Single(source.Entries).Graph);

        await source.ResetAsync(temp.Repository, new LogQuery { Revision = "no-such-branch" });
        Assert.Empty(source.Entries);
        Assert.NotNull(source.Error);
        Assert.True(source.IsComplete);
    }
}
