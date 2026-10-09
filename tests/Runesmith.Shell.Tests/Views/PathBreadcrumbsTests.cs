using Runesmith.Shell.Views;

namespace Runesmith.Shell.Tests.Views;

public sealed class PathBreadcrumbsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "Runesmith");

    [Fact]
    public void ShowsAFileInTheFolderFromTheFolderDown()
    {
        var file = Path.Combine(Root, "src", "Shell", "MainWindow.cs");

        var parts = PathBreadcrumbs.Parts(file, Root);

        Assert.Equal(["Runesmith", "src", "Shell", "MainWindow.cs"], parts.Select(p => p.Name));
        Assert.Equal([Root, Path.Combine(Root, "src"), Path.Combine(Root, "src", "Shell"), file], parts.Select(p => p.Path));
    }

    [Fact]
    public void ShowsTheLastFoldersOfAFileOutsideTheFolder()
    {
        var file = Path.Combine(Path.GetTempPath(), "a", "b", "c", "d", "e.txt");

        var parts = PathBreadcrumbs.Parts(file, Root);

        Assert.Equal(["b", "c", "d", "e.txt"], parts.Select(p => p.Name));
        Assert.Equal(file, parts[^1].Path);
        Assert.Equal(Path.Combine(Path.GetTempPath(), "a", "b"), parts[0].Path);
    }

    [Fact]
    public void ShowsTheLastFoldersWhenNoFolderIsOpen()
    {
        var file = Path.Combine(Root, "notes.md");

        Assert.Equal(file, PathBreadcrumbs.Parts(file, null)[^1].Path);
    }
}
