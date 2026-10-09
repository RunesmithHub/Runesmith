using Runesmith.Git.Git;
using Runesmith.Sdk.VersionControl;

namespace Runesmith.Git.Tests.Git;

public sealed class GitServiceTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("runesmith-git-").FullName;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheFirstSourceThatAnswersWins()
    {
        var silent = new Source(null);
        var first = new Source(new GitCredentials("codeberg.org", "alice", "first"));
        var second = new Source(new GitCredentials("codeberg.org", "alice", "second"));
        var resolver = new GitCredentialResolver([silent, first, second]);

        var credentials = await resolver.GetAsync("https://codeberg.org/alice/hello.git", Token);

        Assert.Equal("first", credentials?.Token);
        Assert.Single(silent.Asked);
        Assert.Empty(second.Asked);
    }

    [Theory]
    [InlineData("git@codeberg.org:alice/hello.git")]
    [InlineData("ssh://git@codeberg.org/alice/hello.git")]
    [InlineData("http://codeberg.org/alice/hello.git")]
    [InlineData("/home/alice/hello")]
    public async Task RemotesThatAreNotHttpsKeepTheUsersOwnSetup(string url)
    {
        var source = new Source(new GitCredentials("codeberg.org", "alice", "token"));

        Assert.Null(await new GitCredentialResolver([source]).GetAsync(url, Token));
        Assert.Empty(source.Asked);
    }

    [Fact]
    public async Task RunsGitWithTheCredentialsOfTheRemoteItIsGiven()
    {
        var source = new Source(new GitCredentials("codeberg.org", "alice", "secret-token"));
        var git = new GitService(new GitCredentialResolver([source]));

        var result = await git.RunAsync(folder, ["credential", "fill"],
            new GitRunOptions { CredentialsFor = "https://codeberg.org/alice/hello.git", Input = "protocol=https\nhost=codeberg.org\n\n" }, Token);

        Assert.True(result.Succeeded);
        Assert.Contains("username=alice", result.Output, StringComparison.Ordinal);
        Assert.Contains("password=secret-token", result.Output, StringComparison.Ordinal);
        Assert.Equal(["https://codeberg.org/alice/hello.git"], source.Asked);
    }

    [Fact]
    public async Task AsksNoSourceWithoutARemote()
    {
        var source = new Source(new GitCredentials("codeberg.org", "alice", "token"));

        await new GitService(new GitCredentialResolver([source])).RunAsync(folder, ["--version"], cancellationToken: Token);

        Assert.Empty(source.Asked);
    }

    [Fact]
    public async Task AFailureIsAResultRatherThanAnException()
    {
        var result = await new GitService(new GitCredentialResolver([])).RunAsync(folder, ["log"], cancellationToken: Token);

        Assert.False(result.Succeeded);
        Assert.Contains("not a git repository", result.Error, StringComparison.Ordinal);
    }

    public void Dispose() => TempRepository.Delete(folder);

    private sealed class Source(GitCredentials? answer) : IGitCredentialSource
    {
        public List<string> Asked { get; } = [];

        public Task<GitCredentials?> GetAsync(string remoteUrl, CancellationToken cancellationToken)
        {
            Asked.Add(remoteUrl);
            return Task.FromResult(answer);
        }
    }
}
