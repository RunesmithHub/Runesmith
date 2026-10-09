using Runesmith.Sdk.VersionControl;

namespace Runesmith.Plugins.Gitea.Tests;

public sealed class GiteaRemoteTests
{
    private static readonly GiteaServer Codeberg = GiteaServer.Of("codeberg.org");
    private static readonly GiteaServer Ported = GiteaServer.Of("git.example.com:3000");
    private static readonly GiteaServer UnderPath = GiteaServer.Of("https://example.com/git/");

    [Theory]
    [InlineData("https://codeberg.org/alice/notes.git")]
    [InlineData("https://codeberg.org/alice/notes")]
    [InlineData("https://codeberg.org/alice/notes/")]
    [InlineData("https://alice@codeberg.org/alice/notes.git")]
    [InlineData("https://CODEBERG.org/alice/notes.git")]
    [InlineData("https://codeberg.org:443/alice/notes.git")]
    [InlineData("git@codeberg.org:alice/notes.git")]
    [InlineData("codeberg.org:alice/notes")]
    [InlineData("ssh://git@codeberg.org/alice/notes.git")]
    [InlineData("ssh://git@codeberg.org:22/alice/notes.git")]
    [InlineData("ssh://git@codeberg.org:2222/alice/notes.git")]
    [InlineData("git+ssh://git@codeberg.org/alice/notes.git")]
    [InlineData("  https://codeberg.org/alice/notes.git  ")]
    public void OwnsTheServersRemotes(string url)
    {
        var remote = GiteaRemote.Parse(Codeberg, url);

        Assert.Equal(new GiteaRemote(Codeberg, "alice", "notes"), remote);
    }

    [Theory]
    [InlineData("https://github.com/alice/notes.git")]
    [InlineData("https://codeberg.org.evil.example/alice/notes.git")]
    [InlineData("https://codeberg.org:8443/alice/notes.git")]
    [InlineData("git@gitea.com:alice/notes.git")]
    [InlineData("https://codeberg.org/alice")]
    [InlineData("https://codeberg.org/alice/notes/src/branch/main")]
    [InlineData("https://codeberg.org/alice/.git")]
    [InlineData("file:///home/alice/notes")]
    [InlineData("/home/alice/notes")]
    [InlineData("")]
    [InlineData(null)]
    public void LeavesOtherRemotesAlone(string? url) => Assert.Null(GiteaRemote.Parse(Codeberg, url));

    [Theory]
    [InlineData("https://git.example.com:3000/team/api.git", true)]
    [InlineData("https://git.example.com/team/api.git", false)]
    [InlineData("ssh://git@git.example.com:2222/team/api.git", true)]
    [InlineData("git@git.example.com:team/api.git", true)]
    public void MatchesTheHttpsPortButAnySshPort(string url, bool owned) =>
        Assert.Equal(owned, GiteaRemote.Parse(Ported, url) is not null);

    [Theory]
    [InlineData("https://example.com/git/team/api.git", true)]
    [InlineData("https://example.com/team/api.git", false)]
    [InlineData("https://example.com/gitx/team/api.git", false)]
    [InlineData("ssh://git@example.com/git/team/api.git", true)]
    [InlineData("ssh://git@example.com/team/api.git", true)]
    [InlineData("git@example.com:team/api.git", true)]
    public void ReadsRemotesOfAServerUnderAPath(string url, bool owned)
    {
        var remote = GiteaRemote.Parse(UnderPath, url);

        Assert.Equal(owned, remote is not null);
        if (remote is not null)
            Assert.Equal(("team", "api"), (remote.Owner, remote.Name));
    }

    [Fact]
    public void OnlyGivesTokensToHttpsRemotes()
    {
        Assert.True(GiteaRemote.IsWebRemote(Codeberg, "https://codeberg.org/alice/notes.git"));
        Assert.False(GiteaRemote.IsWebRemote(Codeberg, "http://codeberg.org/alice/notes.git"));
        Assert.False(GiteaRemote.IsWebRemote(Codeberg, "git@codeberg.org:alice/notes.git"));
        Assert.False(GiteaRemote.IsWebRemote(Codeberg, "ssh://git@codeberg.org/alice/notes.git"));
    }

    [Theory]
    [InlineData(WebTargetKind.Repository, null, null, "https://codeberg.org/alice/notes")]
    [InlineData(WebTargetKind.Branch, "feature/sync", null, "https://codeberg.org/alice/notes/src/branch/feature/sync")]
    [InlineData(WebTargetKind.Commit, "9f3a2b1c", null, "https://codeberg.org/alice/notes/commit/9f3a2b1c")]
    [InlineData(WebTargetKind.Folder, "main", "docs/guides", "https://codeberg.org/alice/notes/src/branch/main/docs/guides")]
    [InlineData(WebTargetKind.File, "main", "src/My Notes.md", "https://codeberg.org/alice/notes/src/branch/main/src/My%20Notes.md")]
    [InlineData(WebTargetKind.File, "main", "src\\win.cs", "https://codeberg.org/alice/notes/src/branch/main/src/win.cs")]
    [InlineData(WebTargetKind.File, "0123456789abcdef0123456789abcdef01234567", "a.cs",
        "https://codeberg.org/alice/notes/src/commit/0123456789abcdef0123456789abcdef01234567/a.cs")]
    [InlineData(WebTargetKind.File, "release#1", "a.cs", "https://codeberg.org/alice/notes/src/branch/release%231/a.cs")]
    public void LinksToPagesOnTheServer(WebTargetKind kind, string? revision, string? path, string expected)
    {
        var remote = GiteaRemote.Parse(Codeberg, "git@codeberg.org:alice/notes.git")!;

        Assert.Equal(expected, remote.WebUrl(new WebTarget(kind, revision, path)).AbsoluteUri);
    }

    [Theory]
    [InlineData(12, null, "#L12")]
    [InlineData(12, 12, "#L12")]
    [InlineData(12, 20, "#L12-L20")]
    public void LinksToSelectedLines(int first, int? last, string fragment)
    {
        var remote = GiteaRemote.Parse(Codeberg, "https://codeberg.org/alice/notes.git")!;

        var url = remote.WebUrl(new WebTarget(WebTargetKind.File, "main", "a.cs") { FirstLine = first, LastLine = last });

        Assert.Equal("https://codeberg.org/alice/notes/src/branch/main/a.cs" + fragment, url.AbsoluteUri);
    }

    [Fact]
    public void LinksKeepTheServersPortAndPath()
    {
        Assert.Equal("https://git.example.com:3000/team/api/src/branch/main/a.cs",
            GiteaRemote.Parse(Ported, "ssh://git@git.example.com:2222/team/api.git")!.WebUrl(new WebTarget(WebTargetKind.File, "main", "a.cs")).AbsoluteUri);
        Assert.Equal("https://example.com/git/team/api/commit/abc",
            GiteaRemote.Parse(UnderPath, "https://example.com/git/team/api.git")!.WebUrl(new WebTarget(WebTargetKind.Commit, "abc")).AbsoluteUri);
    }

    [Theory]
    [InlineData("codeberg.org", "https://codeberg.org/", "codeberg.org")]
    [InlineData("https://Codeberg.org/", "https://codeberg.org/", "codeberg.org")]
    [InlineData("git.example.com:3000", "https://git.example.com:3000/", "git.example.com:3000")]
    [InlineData("example.com/git", "https://example.com/git/", "example.com/git")]
    [InlineData("http://192.168.1.20:3000/", "http://192.168.1.20:3000/", "http://192.168.1.20:3000")]
    public void ReadsServerAddresses(string text, string baseUri, string key)
    {
        var server = GiteaServer.Parse(text)!;

        Assert.Equal(baseUri, server.BaseUri.AbsoluteUri);
        Assert.Equal(key, server.Key);
        Assert.Equal(server, GiteaServer.Parse(key));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ftp://example.com")]
    [InlineData("https://alice:secret@example.com")]
    [InlineData(null)]
    public void RejectsWhatIsNotAServerAddress(string? text) => Assert.Null(GiteaServer.Parse(text));

    [Fact]
    public void KnowsTheServersPages()
    {
        Assert.Equal("https://example.com/git/api/v1/", UnderPath.ApiUri.AbsoluteUri);
        Assert.Equal("https://example.com/git/user/settings/applications", UnderPath.ApplicationsUri.AbsoluteUri);
    }

    [Fact]
    public void ReadsAndWritesClientIdsPerServer()
    {
        var value = ClientIds.With("", Ported, "abc", Codeberg);
        value = ClientIds.With(value, Codeberg, "def", Codeberg);

        Assert.Equal("abc", ClientIds.Find(value, Ported, Codeberg));
        Assert.Equal("def", ClientIds.Find(value, Codeberg, Codeberg));
        Assert.Equal("ghi", ClientIds.Find("ghi", Codeberg, Codeberg));
        Assert.Null(ClientIds.Find("ghi", Ported, Codeberg));
        Assert.Equal("codeberg.org=def", ClientIds.With(value, Ported, "", Codeberg));
    }
}
