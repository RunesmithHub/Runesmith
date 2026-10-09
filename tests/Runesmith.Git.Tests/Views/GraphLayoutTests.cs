using Runesmith.Git.Git;
using Runesmith.Git.Views.History;

namespace Runesmith.Git.Tests.Views;

public sealed class GraphLayoutTests
{
    [Fact]
    public void KeepsALinearHistoryInOneLane()
    {
        var layout = new GraphLayout();

        var rows = new[] { Commit("c", "b"), Commit("b", "a"), Commit("a") }.Select(layout.Add).ToList();

        Assert.All(rows, row => Assert.Equal(0, row.Lane));
        Assert.Empty(rows[0].Top);
        Assert.Equal([new GraphEdge(0, 0, 0)], rows[0].Bottom);
        Assert.Equal([new GraphEdge(0, 0, 0)], rows[1].Top);
        Assert.Empty(rows[2].Bottom);
    }

    [Fact]
    public void OpensALaneForAMergedBranchAndClosesItWhereItBranchedOff()
    {
        var layout = new GraphLayout();

        var merge = layout.Add(Commit("m", "b", "d"));
        var side = layout.Add(Commit("d", "a"));
        var main = layout.Add(Commit("b", "a"));
        var root = layout.Add(Commit("a"));

        Assert.Equal((0, 0), (merge.Lane, merge.Color));
        Assert.Equal([new GraphEdge(0, 0, 0), new GraphEdge(0, 1, 1)], merge.Bottom);
        Assert.Equal((1, 1), (side.Lane, side.Color));
        Assert.Equal(0, main.Lane);
        Assert.Equal([new GraphEdge(0, 0, 0), new GraphEdge(1, 1, 1)], main.Bottom);
        Assert.Equal(0, root.Lane);
        Assert.Equal([new GraphEdge(0, 0, 0), new GraphEdge(1, 0, 1)], root.Top);
        Assert.Empty(root.Bottom);
        Assert.Equal(2, root.Width);
    }

    [Fact]
    public void ClosesUpLanesToTheLeftWhenOneEnds()
    {
        var layout = new GraphLayout();

        layout.Add(Commit("x", "x1"));
        layout.Add(Commit("y", "y1"));
        layout.Add(Commit("z", "z1"));
        var ends = layout.Add(Commit("y1"));
        var after = layout.Add(Commit("z1"));

        Assert.Equal(1, ends.Lane);
        Assert.Equal([new GraphEdge(0, 0, 0), new GraphEdge(2, 1, 2)], ends.Bottom);
        Assert.Equal(1, after.Lane);
    }

    [Fact]
    public void DrawsAMergeIntoALaneThatAlreadyWaitsForTheParent()
    {
        var layout = new GraphLayout();

        layout.Add(Commit("t", "p"));
        var merge = layout.Add(Commit("m", "q", "p"));

        Assert.Equal(1, merge.Lane);
        Assert.Contains(new GraphEdge(1, 0, 0), merge.Bottom);
        Assert.Contains(new GraphEdge(0, 0, 0), merge.Bottom);
    }

    [Fact]
    public void LaysOutPagesOneAfterAnotherAsIfTheyWereOne()
    {
        var commits = Enumerable.Range(0, 50).Select(i => i % 7 == 3 ? Commit($"c{i}", $"c{i + 1}", $"c{i + 2}") : Commit($"c{i}", $"c{i + 1}")).ToList();
        var whole = new GraphLayout();
        var paged = new GraphLayout();

        var expected = commits.Select(whole.Add).ToList();
        var actual = commits.Take(20).Select(paged.Add).Concat(commits.Skip(20).Select(paged.Add)).ToList();

        Assert.Equal(expected.Select(Describe), actual.Select(Describe));
    }

    private static string Describe(GraphRow row) => $"{row.Lane}/{row.Color}/{string.Join(",", row.Top)}/{string.Join(",", row.Bottom)}";

    private static GitCommit Commit(string sha, params string[] parents) =>
        new(sha, parents, "Ada", "ada@example.com", DateTimeOffset.UnixEpoch, "Ada", "ada@example.com", DateTimeOffset.UnixEpoch, sha, []);
}
