using Runesmith.Git.Commands;
using Runesmith.Git.Git;

namespace Runesmith.Git.Tests.Git;

public sealed class ContextPathTests
{
    private static readonly StatusSnapshot Status = new(BranchStatus.Empty,
    [
        new StatusEntry("src/a.cs", '.', 'M', StatusKind.Ordinary),
        new StatusEntry("src/app/b.cs", 'A', '.', StatusKind.Ordinary),
        new StatusEntry("src/new.txt", '?', '?', StatusKind.Untracked),
        new StatusEntry("src/debug.log", '!', '!', StatusKind.Ignored),
        new StatusEntry("srcs/c.cs", '.', 'M', StatusKind.Ordinary),
    ]);

    [Fact]
    public void AFolderHasTheChangesInsideItAndAFileItsOwnLeavingIgnoredFilesOut()
    {
        Assert.Equal(["src/a.cs", "src/app/b.cs", "src/new.txt"], GitOperations.ChangesAt(Status, "src", isDirectory: true).Select(e => e.Path));
        Assert.Equal(["src/app/b.cs"], GitOperations.ChangesAt(Status, "src/app", isDirectory: true).Select(e => e.Path));
        Assert.Equal(["src/a.cs"], GitOperations.ChangesAt(Status, "src/a.cs", isDirectory: false).Select(e => e.Path));
        Assert.Empty(GitOperations.ChangesAt(Status, "src/debug.log", isDirectory: false));
        Assert.Empty(GitOperations.ChangesAt(Status, "docs", isDirectory: true));
    }

    [Theory]
    [InlineData("src/a.cs", true)]
    [InlineData(".gitignore", true)]
    [InlineData(".github/workflows", true)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData("../other/a.cs", false)]
    [InlineData(".git", false)]
    [InlineData(".git/HEAD", false)]
    public void TellsWhetherAPathIsInTheWorkingTree(string relativePath, bool expected) =>
        Assert.Equal(expected, GitOperations.IsInWorkingTree(relativePath));
}
