using Runesmith.Git.Git;
using Runesmith.Sdk.VersionControl;

namespace Runesmith.Git.Tests.Git;

public sealed class GitProcessTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("runesmith-git-").FullName;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RunsGitAndReturnsItsOutput()
    {
        await GitProcess.RunCheckedAsync(folder, ["init", "--quiet", "--initial-branch", "main"], Token);

        var branch = await GitProcess.RunCheckedAsync(folder, ["symbolic-ref", "--short", "HEAD"], Token);

        Assert.Equal("main", branch.Trim());
    }

    [Fact]
    public async Task AFailureCarriesGitsOwnMessage()
    {
        var exception = await Assert.ThrowsAsync<GitException>(() => GitProcess.RunCheckedAsync(folder, ["log"], Token));

        Assert.Contains("not a git repository", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("fatal:", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GivesGitTheCredentialsWithoutPuttingTheTokenOnTheCommandLine()
    {
        var credentials = new GitCredentials("github.com", "x-access-token", "secret-token");

        var filled = await GitProcess.RunCheckedAsync(folder, ["credential", "fill"], Token, credentials, input: "protocol=https\nhost=github.com\n\n");

        Assert.Contains("username=x-access-token", filled, StringComparison.Ordinal);
        Assert.Contains("password=secret-token", filled, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportsProgressLinesThatEndInCarriageReturns()
    {
        var lines = new List<string>();
        var progress = new SynchronousProgress(lines);

        await GitProcess.RunAsync(folder, ["-c", "core.pager=cat", "sh-does-not-exist"], Token, progress: progress);

        Assert.NotEmpty(lines);
    }

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private sealed class SynchronousProgress(List<string> lines) : IProgress<string>
    {
        public void Report(string value) => lines.Add(value);
    }
}
