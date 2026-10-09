using System.Net;
using Runesmith.Sdk.VersionControl;

namespace Runesmith.Plugins.Gitea.Tests;

public sealed class GiteaHostTests
{
    private readonly Harness harness = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Origin => "https://" + Harness.DefaultServer.Key;

    private static string Remote => Origin + "/forgejo/forgejo.git";

    [Fact]
    public void NamesTheAccountByServiceServerAndLogin()
    {
        var flavor = Subject.Flavor;
        var host = harness.Host();
        var other = harness.Host("bob", GiteaServer.Of("git.example.com:3000"));

        Assert.Equal($"{flavor.IdPrefix}:{flavor.DefaultServer.Key}:alice", host.Id);
        Assert.Equal(flavor.DefaultServerName, host.Name);
        Assert.Equal($"{flavor.IdPrefix}:git.example.com:3000:bob", other.Id);
        Assert.Equal($"{flavor.Name} (git.example.com:3000)", other.Name);
        Assert.Equal("git.example.com:3000", ((IRepositoryHost)other).Server);
        Assert.Equal(flavor.Icon, host.Icon);
        Assert.Equal(new Uri(Origin + "/user/settings/applications"), host.ManageAccessUrl);
        Assert.True(host.SupportsDraftPullRequests);
    }

    [Fact]
    public async Task ATokenIsCheckedWithTheServerThenKeptInTheSecretStore()
    {
        harness.Server.On("/api/v1/user", Responses.User);
        var host = harness.Host(login: null);

        var user = await host.SignInWithTokenAsync("  pasted-token \n", Token);

        Assert.Equal("alice", user.Login);
        Assert.Equal("alice", host.Account);
        Assert.Equal(SignInKind.Token, host.Kind);
        Assert.Equal("Alice Liddell", host.Profile.FullName);
        Assert.Equal("token pasted-token", Assert.Single(harness.Server.Requests).Authorization);
        var (key, value) = Assert.Single(harness.Secrets.Secrets);
        Assert.Equal($"{Subject.Flavor.PluginId}/{Harness.DefaultServer.Key}/alice", key);
        Assert.Contains("pasted-token", value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARefusedTokenLeavesTheAccountSignedOut()
    {
        harness.Server.On("/api/v1/user", Responses.Unauthorized, HttpStatusCode.Unauthorized);
        var host = harness.Host(login: null);

        var failure = await Assert.ThrowsAsync<GiteaException>(() => host.SignInWithTokenAsync("wrong", Token));

        Assert.Equal(GiteaFailure.Unauthorized, failure.Failure);
        Assert.False(host.IsSignedIn);
        Assert.Empty(harness.Secrets.Secrets);
        Assert.DoesNotContain("wrong", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SigningOutForgetsTheToken()
    {
        harness.Server.On("/api/v1/user", Responses.User);
        var host = harness.Host(login: null);
        await host.SignInWithTokenAsync("pasted-token", Token);

        await host.SignOutAsync();

        Assert.Null(host.Account);
        Assert.Equal("alice", host.Login);
        Assert.Empty(harness.Secrets.Secrets);
        Assert.Null(await host.GetTokenAsync(Token));
    }

    [Fact]
    public async Task ReadsTheTokenBackFromTheSecretStore()
    {
        await harness.Secrets.SetAsync(harness.SecretKey("alice"), GiteaHost.Serialize(new StoredTokens(SignInKind.Token, "kept", null, null)), Token);
        var host = harness.Host();

        await host.LoadAsync(Token);

        Assert.True(host.IsSignedIn);
        Assert.Equal("kept", (await host.GetTokenAsync(Token))?.Value);
    }

    [Fact]
    public async Task ARefusedTokenSignsTheAccountOutAndOffersToSignInAgain()
    {
        await SignedInAsync();
        harness.Server.On("/api/v1/repos/forgejo/forgejo", Responses.Unauthorized, HttpStatusCode.Unauthorized);
        var host = harness.Host();

        await Assert.ThrowsAsync<GiteaException>(() => host.GetDefaultBranchAsync(Remote, Token));
        await WaitUntilAsync(() => !host.IsSignedIn);

        Assert.Empty(harness.Secrets.Secrets);
        Assert.Contains(harness.Notifications.Shown, shown => shown.Title.StartsWith("Signed out of", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListsTheAccountsAndItsOrganizationsRepositoriesOnce()
    {
        await SignedInAsync();
        harness.Server.On("/api/v1/user/repos", Responses.List(
            Responses.Repository(1, "alice", "notes", updated: "2026-09-01T00:00:00Z"),
            Responses.Repository(2, "alice", "site", isPrivate: true, updated: "2026-10-01T00:00:00Z"),
            Responses.Repository(3, "wonderland", "tea-party")));
        harness.Server.On("/api/v1/user/orgs", Responses.Organizations);
        harness.Server.On("/api/v1/orgs/wonderland/repos", Responses.List(Responses.Repository(3, "wonderland", "tea-party"), Responses.Repository(4, "wonderland", "croquet")));

        var repositories = await harness.Host().GetRepositoriesAsync(Token);

        Assert.Equal(["alice/site", "alice/notes", "wonderland/tea-party", "wonderland/croquet"], repositories.Select(repository => $"{repository.Owner}/{repository.Name}"));
        Assert.True(repositories[0].IsPrivate);
        Assert.False(repositories[0].OwnerIsOrganization);
        Assert.True(repositories[3].OwnerIsOrganization);
        Assert.Equal("https://codeberg.org/alice/site.git", repositories[0].CloneUrl);
        Assert.Equal("C#", repositories[0].Language);
        Assert.Equal(4, repositories[0].Stars);
    }

    [Fact]
    public async Task ReadsTheDefaultBranch()
    {
        await SignedInAsync();
        harness.Server.On("/api/v1/repos/forgejo/forgejo", Responses.Read("repository.json"));

        Assert.Equal("forgejo", await harness.Host().GetDefaultBranchAsync(Remote, Token));
    }

    [Fact]
    public async Task ListsPullRequestsWithTheirReviewsAndChecks()
    {
        await SignedInAsync();
        harness.Server.On("/api/v1/repos/forgejo/forgejo/pulls", Responses.Read("pulls.json"));
        harness.Server.On("/api/v1/repos/forgejo/forgejo/pulls/14764/reviews", Responses.Read("reviews.json"));
        harness.Server.On("/api/v1/repos/forgejo/forgejo/pulls/14762/reviews", Responses.Read("reviews-14758.json"));
        harness.Server.On("/api/v1/repos/forgejo/forgejo/commits/b873c80508c6e34a8898a370b6db73ea9ed85753/status", Responses.Read("status.json"));
        harness.Server.On("/api/v1/repos/forgejo/forgejo/commits/3ce0c70b7bea5ab3c8db35c94b970a61a352d4c2/status", """{"state":"","total_count":0}""");

        var pulls = await harness.Host().GetPullRequestsAsync(Remote, Token);

        Assert.Equal(2, pulls.Count);
        var first = pulls[0];
        Assert.Equal(14764, first.Number);
        Assert.Equal("viceice-bot", first.Author);
        Assert.Equal("renovate/v15.0/forgejo-npm-katex-vulnerability", first.HeadBranch);
        Assert.Equal("v15.0/forgejo", first.BaseBranch);
        Assert.Equal("refs/pull/14764/head", first.HeadRef);
        Assert.Equal(ReviewState.ReviewRequired, first.Review);
        Assert.Equal(ChecksState.Pending, first.Checks);
        Assert.Equal(new Uri("https://codeberg.org/forgejo/forgejo/pulls/14764"), first.WebUrl);
        Assert.False(first.IsDraft);
        Assert.Equal(ReviewState.Approved, pulls[1].Review);
        Assert.Equal(ChecksState.None, pulls[1].Checks);
    }

    [Fact]
    public async Task AReviewOrStatusItCannotReadLeavesThePullRequestListed()
    {
        await SignedInAsync();
        harness.Server.On("/api/v1/repos/forgejo/forgejo/pulls", Responses.List(Responses.Read("pull-draft.json")));
        harness.Server.On(request => request.RequestUri!.AbsolutePath.EndsWith("/reviews", StringComparison.Ordinal),
            (_, _) => new HttpResponseMessage(HttpStatusCode.Forbidden));

        var pull = Assert.Single(await harness.Host().GetPullRequestsAsync(Remote, Token));

        Assert.True(pull.IsDraft);
        Assert.Equal(ReviewState.None, pull.Review);
    }

    [Fact]
    public async Task CreatesADraftByItsWorkInProgressPrefix()
    {
        await SignedInAsync();
        harness.Server.On("/api/v1/repos/alice/notes/pulls", Responses.CreatedPullRequest, HttpStatusCode.Created);

        var created = await harness.Host().CreatePullRequestAsync(Origin + "/alice/notes.git", new PullRequestDraft("index", "main", "Add the index", "Lists every note.", IsDraft: true), Token);
        await harness.Host().CreatePullRequestAsync(Origin + "/alice/notes.git", new PullRequestDraft("index", "main", "[WIP] Add the index", "", IsDraft: true), Token);

        var bodies = harness.Server.Requests.Where(request => request.Method == HttpMethod.Post).Select(request => request.Body).ToList();
        Assert.Contains("\"title\":\"WIP: Add the index\"", bodies[0], StringComparison.Ordinal);
        Assert.Contains("\"title\":\"[WIP] Add the index\"", bodies[1], StringComparison.Ordinal);
        Assert.DoesNotContain("\"body\"", bodies[1], StringComparison.Ordinal);
        Assert.True(created.IsDraft);
        Assert.Equal("refs/pull/12/head", created.HeadRef);
    }

    [Fact]
    public async Task RefusesARemoteOfAnotherServer()
    {
        await SignedInAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => harness.Host().GetPullRequestsAsync("https://github.com/alice/notes.git", Token));
        Assert.Null(harness.Host().GetWebUrl("https://github.com/alice/notes.git", new WebTarget(WebTargetKind.Repository)));
        Assert.False(harness.Host().Owns("https://github.com/alice/notes.git"));
        Assert.True(harness.Host().Owns($"git@{Harness.DefaultServer.Key}:alice/notes.git"));
    }

    [Theory]
    [InlineData("APPROVED", false, false, ReviewState.Approved)]
    [InlineData("APPROVED", true, false, ReviewState.None)]
    [InlineData("APPROVED", false, true, ReviewState.None)]
    [InlineData("REQUEST_CHANGES", false, false, ReviewState.ChangesRequested)]
    [InlineData("COMMENT", false, false, ReviewState.None)]
    [InlineData("REQUEST_REVIEW", false, false, ReviewState.ReviewRequired)]
    public void SumsUpReviews(string state, bool stale, bool dismissed, ReviewState expected) =>
        Assert.Equal(expected, GiteaHost.Summarize([new GiteaReview(1, new GiteaOwner("bob", null), state, stale, dismissed, Harness.Start)], false));

    [Fact]
    public void ALaterApprovalReplacesRequestedChanges()
    {
        GiteaReview[] reviews =
        [
            new(1, new GiteaOwner("bob", null), "REQUEST_CHANGES", false, false, Harness.Start),
            new(2, new GiteaOwner("bob", null), "APPROVED", false, false, Harness.Start.AddHours(1)),
        ];

        Assert.Equal(ReviewState.Approved, GiteaHost.Summarize(reviews, false));
        Assert.Equal(ReviewState.ReviewRequired, GiteaHost.Summarize([], true));
    }

    [Theory]
    [InlineData("pending", 2, ChecksState.Pending)]
    [InlineData("success", 2, ChecksState.Passing)]
    [InlineData("warning", 2, ChecksState.Passing)]
    [InlineData("failure", 2, ChecksState.Failing)]
    [InlineData("error", 2, ChecksState.Failing)]
    [InlineData("", 0, ChecksState.None)]
    public void SumsUpChecks(string state, int count, ChecksState expected) =>
        Assert.Equal(expected, GiteaHost.Summarize(new GiteaCombinedStatus(state, count)));

    private Task SignedInAsync() =>
        harness.Secrets.SetAsync(harness.SecretKey("alice"), GiteaHost.Serialize(new StoredTokens(SignInKind.Token, "kept", null, null)), Token);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
            await Task.Delay(10, Token);
        Assert.True(condition());
    }
}
