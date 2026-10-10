using Runesmith.Shell.Hub;

namespace Runesmith.Shell.Tests.Hub;

public sealed class GitFolderTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("runesmith-git-folder-").FullName;

    public void Dispose() => TestFolders.Delete(root);

    [Theory]
    [InlineData("https://github.com/RunesmithHub/plugin-git.git", "github.com")]
    [InlineData("https://user@GitHub.com/RunesmithHub/plugin-git", "github.com")]
    [InlineData("ssh://git@github.com:22/RunesmithHub/plugin-git.git", "github.com")]
    [InlineData("git@github.com:RunesmithHub/plugin-git.git", "github.com")]
    [InlineData("github.com:RunesmithHub/plugin-git", "github.com")]
    [InlineData("https://gitlab.com/acme/shop.git", "gitlab.com")]
    [InlineData("https://github.com.evil.example/acme/shop", "github.com.evil.example")]
    [InlineData("/srv/git/shop.git", null)]
    [InlineData("../shop", null)]
    [InlineData("C:/git/shop", null)]
    [InlineData("file:///srv/git/shop.git", null)]
    public void FindsTheHostOfARemoteUrl(string url, string? host) =>
        Assert.Equal(host, GitFolder.Host(url), StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void ReadsOnlyTheUrlsOfRemotes()
    {
        string[] config =
        [
            "[core]",
            "\turl = https://github.com/not/a-remote",
            "[remote \"origin\"]",
            "\turl = git@github.com:RunesmithHub/plugin-git.git",
            "\tfetch = +refs/heads/*:refs/remotes/origin/*",
            "; url = https://github.com/commented/out",
            "[remote \"mirror\"]",
            "\tURL = \"https://gitlab.com/acme/shop.git\"",
            "[branch \"main\"]",
            "\tremote = origin",
        ];

        Assert.Equal(["git@github.com:RunesmithHub/plugin-git.git", "https://gitlab.com/acme/shop.git"], GitFolder.ParseRemoteUrls(config));
    }

    [Fact]
    public void AFolderWithoutGitIsNoRepository()
    {
        Assert.False(GitFolder.IsRepository(root));
        Assert.Empty(GitFolder.RemoteUrls(root));
    }

    [Fact]
    public void AWorktreeFindsTheSharedConfigThroughItsGitFile()
    {
        var main = Path.Combine(root, "main", ".git");
        Directory.CreateDirectory(Path.Combine(main, "worktrees", "feature"));
        File.WriteAllText(Path.Combine(main, "config"), "[remote \"origin\"]\n\turl = https://github.com/RunesmithHub/plugin-git\n");
        File.WriteAllText(Path.Combine(main, "worktrees", "feature", "commondir"), "../..\n");
        var worktree = Path.Combine(root, "feature");
        Directory.CreateDirectory(worktree);
        File.WriteAllText(Path.Combine(worktree, ".git"), $"gitdir: {Path.Combine(main, "worktrees", "feature")}\n");

        Assert.True(GitFolder.IsRepository(worktree));
        Assert.True(GitFolder.HasGitHubRemote(worktree));
    }

    [Fact]
    public void ASubmoduleFindsItsConfigThroughARelativeGitFile()
    {
        var modules = Path.Combine(root, ".git", "modules", "lib");
        Directory.CreateDirectory(modules);
        File.WriteAllText(Path.Combine(modules, "config"), "[remote \"origin\"]\n\turl = https://gitlab.com/acme/lib.git\n");
        var submodule = Path.Combine(root, "lib");
        Directory.CreateDirectory(submodule);
        File.WriteAllText(Path.Combine(submodule, ".git"), "gitdir: ../.git/modules/lib\n");

        Assert.True(GitFolder.IsRepository(submodule));
        Assert.Equal(["https://gitlab.com/acme/lib.git"], GitFolder.RemoteUrls(submodule));
        Assert.False(GitFolder.HasGitHubRemote(submodule));
    }
}
