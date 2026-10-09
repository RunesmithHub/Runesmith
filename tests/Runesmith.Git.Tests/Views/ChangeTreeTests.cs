using Runesmith.Git.Git;
using Runesmith.Git.Views.Changes;

namespace Runesmith.Git.Tests.Views;

public sealed class ChangeTreeTests
{
    [Fact]
    public void GroupsFilesAndJoinsFoldersThatHoldOneFolder()
    {
        var status = new StatusSnapshot(BranchStatus.Empty,
        [
            new StatusEntry("src/app/core/a.cs", 'M', '.', StatusKind.Ordinary),
            new StatusEntry("src/app/core/b.cs", 'M', 'M', StatusKind.Ordinary),
            new StatusEntry("src/app/ui/c.cs", '.', 'D', StatusKind.Ordinary),
            new StatusEntry("README.md", '.', 'M', StatusKind.Ordinary),
            new StatusEntry("new/file.txt", '?', '?', StatusKind.Untracked),
            new StatusEntry("both.txt", 'U', 'U', StatusKind.Unmerged),
        ]);

        var groups = ChangeNode.Build(status, new HashSet<string>());

        Assert.Equal(["Conflicts", "Staged", "Changes", "Untracked Files"], groups.Select(g => g.Name));
        var staged = groups[1];
        Assert.Equal("src/app/core", Assert.Single(staged.Children).Name);
        Assert.Equal(["a.cs", "b.cs"], staged.Children[0].Children.Select(c => c.Name));
        Assert.Equal(['M', 'M'], staged.Files().Select(f => f.Letter));
        var unstaged = groups[2];
        Assert.Equal(["src/app", "README.md"], unstaged.Children.Select(c => c.Name));
        Assert.Equal(["core", "ui"], unstaged.Children[0].Children.Select(c => c.Name));
        Assert.Equal(['M', 'D', 'M'], unstaged.Files().Select(f => f.Letter));
        Assert.Equal('U', groups[3].Files().Single().Letter);
        Assert.Equal('!', groups[0].Files().Single().Letter);
        Assert.True(staged.IsStaged);
        Assert.False(unstaged.Files().First().IsStaged);
    }

    [Fact]
    public void KeepsWhatTheUserCollapsedAndStartsLargeGroupsCollapsed()
    {
        var entries = Enumerable.Range(0, 10).Select(i => new StatusEntry($"dir{i}/f.txt", '.', 'M', StatusKind.Ordinary)).ToList();
        var status = new StatusSnapshot(BranchStatus.Empty, entries);

        var normal = ChangeNode.Build(status, new HashSet<string> { "Unstaged:dir3/" });
        var large = ChangeNode.Build(status, new HashSet<string> { "open:Unstaged:dir3/" }, collapseFoldersAbove: 5);

        Assert.Equal([3], normal[0].Children.Select((c, i) => (c, i)).Where(p => !p.c.IsExpanded).Select(p => p.i));
        Assert.Equal([3], large[0].Children.Select((c, i) => (c, i)).Where(p => p.c.IsExpanded).Select(p => p.i));
    }
}
