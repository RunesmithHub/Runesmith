using Runesmith.Git.Views.PullRequests;

namespace Runesmith.Git.Tests.Hosting;

public sealed class PullRequestTextTests
{
    [Fact]
    public void OneCommitGivesItsSubjectAndBody()
    {
        var draft = PullRequestService.Draft("Add the clone dialog\u001fIt lists repositories by owner.\n\u001e\n", "feature/clone");

        Assert.Equal(new PullRequestText("Add the clone dialog", "It lists repositories by owner."), draft);
    }

    [Fact]
    public void SeveralCommitsGiveTheBranchAndTheirSubjectsOldestFirst()
    {
        var draft = PullRequestService.Draft("Third\u001f\u001e\nSecond\u001f\u001e\nFirst\u001fbody\u001e\n", "feature/streaming-export");

        Assert.Equal(new PullRequestText("Streaming export", "- First\n- Second\n- Third"), draft);
    }

    [Fact]
    public void NoCommitsGiveTheBranchAlone() => Assert.Equal(new PullRequestText("Fix login", ""), PullRequestService.Draft("", "fix_login"));
}
