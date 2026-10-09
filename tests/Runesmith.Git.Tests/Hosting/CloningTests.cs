using Runesmith.Git.Clone;
using Runesmith.Git.Git;

namespace Runesmith.Git.Tests.Hosting;

public sealed class CloningTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("runesmith-clone-").FullName;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("https://github.com/octocat/hello.git", "hello")]
    [InlineData("https://github.com/octocat/hello", "hello")]
    [InlineData("git@github.com:octocat/hello.git", "hello")]
    [InlineData("ssh://git@example.com:2222/team/tools.git/", "tools")]
    [InlineData("git://example.com/project", "project")]
    public void NamesTheFolderAsGitDoes(string url, string name)
    {
        Assert.True(RepositoryCloner.IsValidUrl(url));
        Assert.Equal(name, RepositoryCloner.FolderName(url));
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("https://github.com")]
    [InlineData("ftp://example.com/repo.git")]
    [InlineData("https://github.com/octocat/hello world")]
    public void RefusesTextThatIsNoGitUrl(string url) => Assert.False(RepositoryCloner.IsValidUrl(url));

    [Theory]
    [InlineData("Cloning into 'hello'...", "Cloning into 'hello'...", null)]
    [InlineData("remote: Counting objects:  50% (5/10)", "Counting objects:  50% (5/10)", 0.01)]
    [InlineData("Receiving objects:   0% (0/100)", "Receiving objects:   0% (0/100)", 0.05)]
    [InlineData("Receiving objects:  40% (40/100), 1.20 MiB | 2.00 MiB/s", "Receiving objects:  40% (40/100), 1.20 MiB | 2.00 MiB/s", 0.35)]
    [InlineData("Resolving deltas: 100% (12/12), done.", "Resolving deltas: 100% (12/12), done.", 0.92)]
    [InlineData("Updating files: 100% (30/30), done.", "Updating files: 100% (30/30), done.", 1.0)]
    public void ReadsGitsProgress(string line, string text, double? fraction)
    {
        var step = CloneProgress.Parse(line);

        Assert.Equal(text, step.Text);
        if (fraction is null)
            Assert.Null(step.Fraction);
        else
            Assert.Equal(fraction.Value, step.Fraction!.Value, 3);
    }

    [Fact]
    public async Task ClonesARepositoryAndReportsGitsProgress()
    {
        var source = await CreateRepositoryAsync();
        var target = Path.Combine(folder, "parent", "copy");
        var steps = new List<CloneStep>();

        await RepositoryCloner.CloneAsync(new Uri(source).AbsoluteUri, target, null, new Collect(steps), Token);

        Assert.True(File.Exists(Path.Combine(target, "README.md")));
        Assert.Contains(steps, step => step.Text.StartsWith("Cloning into", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFailedCloneLeavesNoFolderAndCarriesGitsMessage()
    {
        var target = Path.Combine(folder, "missing-copy");

        var exception = await Assert.ThrowsAsync<GitException>(() =>
            RepositoryCloner.CloneAsync(new Uri(Path.Combine(folder, "does-not-exist")).AbsoluteUri, target, null, new Collect([]), Token));

        Assert.Contains("does-not-exist", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void AFolderWithFilesIsOccupied()
    {
        var empty = Directory.CreateDirectory(Path.Combine(folder, "empty")).FullName;
        var full = Directory.CreateDirectory(Path.Combine(folder, "full")).FullName;
        File.WriteAllText(Path.Combine(full, "file.txt"), "x");

        Assert.False(RepositoryCloner.IsOccupied(empty));
        Assert.False(RepositoryCloner.IsOccupied(Path.Combine(folder, "new")));
        Assert.True(RepositoryCloner.IsOccupied(full));
    }

    private async Task<string> CreateRepositoryAsync()
    {
        var source = Directory.CreateDirectory(Path.Combine(folder, "source")).FullName;
        await GitProcess.RunCheckedAsync(source, ["init", "--quiet", "--initial-branch", "main"], Token);
        await File.WriteAllTextAsync(Path.Combine(source, "README.md"), "# Hello\n", Token);
        await GitProcess.RunCheckedAsync(source, ["add", "README.md"], Token);
        await GitProcess.RunCheckedAsync(source, ["-c", "user.name=Test", "-c", "user.email=test@example.com", "commit", "--quiet", "-m", "First"], Token);
        return source;
    }

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(folder, recursive: true);
    }

    private sealed class Collect(List<CloneStep> steps) : IProgress<CloneStep>
    {
        public void Report(CloneStep value)
        {
            lock (steps)
                steps.Add(value);
        }
    }
}
