using Runesmith.GitHub.GitHub;

namespace Runesmith.GitHub.Tests.GitHub;

public sealed class GitHubRemoteTests
{
    [Theory]
    [InlineData("https://github.com/octocat/hello.git")]
    [InlineData("https://github.com/octocat/hello")]
    [InlineData("https://github.com/octocat/hello/")]
    [InlineData("https://someone@github.com/octocat/hello.git")]
    [InlineData("git@github.com:octocat/hello.git")]
    [InlineData("github.com:octocat/hello")]
    [InlineData("ssh://git@github.com/octocat/hello.git")]
    [InlineData("ssh://git@github.com:22/octocat/hello")]
    public void ReadsEveryFormOfAGitHubRemote(string url) => Assert.Equal(new GitHubRemote("octocat", "hello"), GitHubRemote.Parse(url));

    [Theory]
    [InlineData("https://gitlab.com/octocat/hello.git")]
    [InlineData("git@bitbucket.org:octocat/hello.git")]
    [InlineData("https://github.com/octocat")]
    [InlineData("https://github.com/octocat/hello/tree/main")]
    [InlineData("/home/octocat/hello")]
    [InlineData("")]
    public void IgnoresRemotesElsewhere(string url) => Assert.Null(GitHubRemote.Parse(url));

    [Fact]
    public void LinksAFileWithItsLines() =>
        Assert.Equal("https://github.com/octocat/hello/blob/feature/login/src/My%20File.cs#L12-L18",
            new GitHubRemote("octocat", "hello").FileUri("feature/login", @"src\My File.cs", 12, 18).AbsoluteUri);

    [Fact]
    public void LinksOneLineWithoutARange() =>
        Assert.Equal("https://github.com/octocat/hello/blob/abc/README.md#L3", new GitHubRemote("octocat", "hello").FileUri("abc", "README.md", 3, 3).AbsoluteUri);
}
