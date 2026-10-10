using Runesmith.Shell.Web;

namespace Runesmith.Shell.Tests.Web;

public sealed class BundledFilesTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("runesmith-web-").FullName;

    public BundledFilesTests()
    {
        Root = Path.Combine(folder, "web");
        Directory.CreateDirectory(Path.Combine(Root, "pages"));
        File.WriteAllText(Path.Combine(Root, "index.html"), "<p>home</p>");
        File.WriteAllText(Path.Combine(Root, "pages", "index.html"), "<p>pages</p>");
        File.WriteAllText(Path.Combine(Root, "pages", "app.js"), "1");
        File.WriteAllText(Path.Combine(Root, "tool.exe"), "MZ");
        File.WriteAllText(Path.Combine(folder, "secret.txt"), "secret");
        File.WriteAllText(Path.Combine(folder, "web-other.txt"), "secret");
    }

    private string Root { get; }

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Theory]
    [InlineData("/index.html", "index.html")]
    [InlineData("/", "index.html")]
    [InlineData("/pages/", "pages/index.html")]
    [InlineData("/pages/app.js?v=2", "pages/app.js")]
    [InlineData("/pages/%61pp.js", "pages/app.js")]
    public void FindsTheFilesOfTheWebFolder(string path, string file) =>
        Assert.Equal(Path.Combine(Root, file.Replace('/', Path.DirectorySeparatorChar)), BundledFiles.Resolve(Root, path));

    [Theory]
    [InlineData("/../secret.txt")]
    [InlineData("/pages/../../secret.txt")]
    [InlineData("/%2e%2e/secret.txt")]
    [InlineData("/%2E%2E%2Fsecret.txt")]
    [InlineData("/pages/..%2f..%2fsecret.txt")]
    [InlineData("/..%5Csecret.txt")]
    [InlineData("/..\\secret.txt")]
    [InlineData("/../web-other.txt")]
    [InlineData("//etc/passwd")]
    [InlineData("/C:/Windows/win.ini")]
    [InlineData("/index.html%00.png")]
    [InlineData("/./index.html")]
    [InlineData("index.html")]
    [InlineData("/tool.exe")]
    [InlineData("/missing.html")]
    [InlineData("/%E0%A4%A")]
    public void NothingOutsideTheFolderOrOfAnUnservedKindIsFound(string path) => Assert.Null(BundledFiles.Resolve(Root, path));

    [Fact]
    public void ASymbolicLinkOutOfTheFolderIsNotFollowed()
    {
        var link = Path.Combine(Root, "leak.txt");
        try
        {
            File.CreateSymbolicLink(link, Path.Combine(folder, "secret.txt"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("This system does not let the tests create symbolic links.");
        }

        Directory.CreateSymbolicLink(Path.Combine(Root, "outside"), folder);

        Assert.Null(BundledFiles.Resolve(Root, "/leak.txt"));
        Assert.Null(BundledFiles.Resolve(Root, "/outside/secret.txt"));
    }
}
