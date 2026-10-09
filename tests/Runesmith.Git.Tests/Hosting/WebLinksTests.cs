using Runesmith.Git.Hosting;
using Runesmith.Sdk.VersionControl;
using Runesmith.Text;

namespace Runesmith.Git.Tests.Hosting;

public sealed class WebLinksTests
{
    private static readonly string Root = OperatingSystem.IsWindows() ? @"C:\src\hello" : "/src/hello";

    [Fact]
    public void TargetsAFileAtItsBranchWithTheSelectedLines()
    {
        var target = WebLinks.File(Repository(branch: "feature/login", upstream: "origin/feature/login"), Path.Combine(Root, "src", "My File.cs"), (12, 18));

        Assert.Equal(new WebTarget(WebTargetKind.File, "feature/login", "src/My File.cs") { FirstLine = 12, LastLine = 18 }, target);
    }

    [Fact]
    public void TargetsTheCommitWhenTheBranchWasNeverPushed()
    {
        var target = WebLinks.File(Repository(branch: "local", upstream: null, head: "4b825dc"), Path.Combine(Root, "README.md"), (3, 3));

        Assert.Equal("4b825dc", target?.Revision);
    }

    [Fact]
    public void TargetsAFolderWithoutLines()
    {
        var target = WebLinks.File(Repository(), Path.Combine(Root, "src", "App"), (1, 2), isDirectory: true);

        Assert.Equal(new WebTarget(WebTargetKind.Folder, "main", "src/App"), target);
    }

    [Fact]
    public void HasNoTargetOutsideTheRepository() =>
        Assert.Null(WebLinks.File(Repository(), Path.Combine(Path.GetDirectoryName(Root)!, "other.txt"), null));

    [Theory]
    [InlineData("https://codeberg.org/alice/hello.git", "alice/hello")]
    [InlineData("git@gitea.com:alice/hello.git", "alice/hello")]
    [InlineData("ssh://git@git.example.com:2222/team/tools", "team/tools")]
    public void NamesTheRepositoryFromItsRemote(string url, string name) => Assert.Equal(name, WebLinks.RepositoryName(url));

    [Fact]
    public void ASelectionEndingAtTheStartOfALineLeavesThatLineOut()
    {
        var text = TextSnapshot.Create("one\ntwo\nthree\nfour\n");

        Assert.Equal((2, 3), WebLinks.Lines(text, new TextSpan(4, 10)));
        Assert.Equal((2, 4), WebLinks.Lines(text, new TextSpan(4, 11)));
        Assert.Equal((3, 3), WebLinks.Lines(text, new TextSpan(9, 0)));
    }

    private static RepositoryInfo Repository(string? branch = "main", string? upstream = "origin/main", string? head = "abc123") =>
        new(Root, branch, head, upstream, 0, 0, [new GitRemote("origin", "https://github.com/octocat/hello.git", "https://github.com/octocat/hello.git")]);
}
