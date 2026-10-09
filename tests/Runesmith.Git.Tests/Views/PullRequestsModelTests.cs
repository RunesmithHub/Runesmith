using Runesmith.Git.Git;
using Runesmith.Git.Hosting;
using Runesmith.Git.Tests.Git;
using Runesmith.Git.Tests.Hosting;
using Runesmith.Git.Views.PullRequests;
using Runesmith.Sdk.VersionControl;

namespace Runesmith.Git.Tests.Views;

public sealed class PullRequestsModelTests : IDisposable
{
    private const string Origin = "https://codeberg.org/alice/hello.git";

    private readonly FakeHost host = new("forgejo:codeberg.org", "codeberg.org", "alice");
    private readonly RepositoryHosts hosts;
    private readonly FakeRepositoryService repositories = new(FakeRepositoryService.Repository(("origin", Origin)));

    public PullRequestsModelTests() => hosts = new RepositoryHosts([new FakeProvider("Forgejo", host)]);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ListsThePullRequestsOfTheHostThatOwnsTheRemote()
    {
        host.PullRequests.Add(PullRequest(7, "Streaming export"));

        var state = await Model().LoadAsync(Token);

        var loaded = Assert.IsType<LoadedState>(state);
        Assert.Same(host, loaded.Context.Host);
        Assert.Equal("origin", loaded.Context.Remote.Name);
        Assert.Equal(["Streaming export"], loaded.Items.Select(p => p.Title));
    }

    [Fact]
    public async Task KeepsTheListUntilForcedOrTheAccountChanges()
    {
        var model = Model();
        await model.LoadAsync(Token);

        Assert.IsType<LoadedState>(model.Ready(force: false));
        Assert.Null(model.Ready(force: true));

        host.Account = "bob";
        Assert.Null(model.Ready(force: false));
        Assert.Equal(1, host.PullRequestLoads);
    }

    [Fact]
    public async Task ASignedOutHostAsksForASignIn()
    {
        host.Account = null;

        var state = await Model().LoadAsync(Token);

        Assert.Same(host, Assert.IsType<SignedOutState>(state).Host);
        Assert.Equal(0, host.PullRequestLoads);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ARepositoryNoHostOwnsShowsWhy(bool hasRepository)
    {
        repositories.Current = hasRepository ? FakeRepositoryService.Repository(("origin", "https://example.com/alice/hello.git")) : null;

        var state = await Model().LoadAsync(Token);

        Assert.Equal(hasRepository, Assert.IsType<NotHostedState>(state).HasRepository);
    }

    [Fact]
    public async Task AHostFailureShowsItsMessage()
    {
        host.Failure = new HttpRequestException("The server is down.");

        var state = await Model().LoadAsync(Token);

        Assert.Equal("The server is down.", Assert.IsType<FailedState>(state).Message);
    }

    [Fact]
    public async Task ChecksOutAPullRequestFromItsHeadRef()
    {
        var bare = await TempRepository.CreateBareAsync();
        try
        {
            using var author = await TempRepository.CloneAsync(bare);
            await author.CommitFileAsync("README.md", "hello\n", "Start");
            await author.GitAsync("push", "--quiet", "origin", "main");
            await author.GitAsync("switch", "--quiet", "-c", "export");
            var head = await author.CommitFileAsync("export.txt", "csv\n", "Export");
            await author.GitAsync("push", "--quiet", "origin", "export:refs/pull/7/head");

            using var reviewer = await TempRepository.CloneAsync(bare);
            await reviewer.GitAsync("config", $"url.{bare}.insteadOf", Origin);
            await reviewer.GitAsync("remote", "set-url", "origin", Origin);
            repositories.Current = new RepositoryInfo(reviewer.Path, "main", null, "origin/main", 0, 0, [new GitRemote("origin", Origin, Origin)]);
            var service = new PullRequestService(hosts, new GitService(new GitCredentialResolver([])), repositories);

            var branch = await service.CheckoutAsync(service.Current!, PullRequest(7, "Export") with { HeadRef = "refs/pull/7/head" }, Token);

            Assert.Equal("export", branch);
            Assert.Equal(head, (await reviewer.GitAsync("rev-parse", "HEAD")).Trim());
            Assert.Equal(1, repositories.Refreshes);
        }
        finally
        {
            TempRepository.Delete(bare);
        }
    }

    public void Dispose() => hosts.Dispose();

    private PullRequestsModel Model() => new(new PullRequestService(hosts, new GitService(new GitCredentialResolver([])), repositories));

    private static PullRequest PullRequest(int number, string title) =>
        new(number, title, "bob", "export", "main", new Uri($"https://codeberg.org/alice/hello/pulls/{number}"));
}
