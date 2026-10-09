using System.IO.Compression;
using Runesmith.Languages.Java.Jdk;

namespace Runesmith.Languages.Java.Tests.Jdk;

public sealed class JdkLocatorTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("runesmith-jdks-").FullName;

    [Theory]
    [InlineData("1.8.0_392", 8)]
    [InlineData("11.0.22", 11)]
    [InlineData("21", 21)]
    [InlineData("25-ea", 25)]
    [InlineData("25.0.4.1", 25)]
    public void ReadsFeatureVersions(string version, int expected) => Assert.Equal(expected, JdkInstallation.ParseFeatureVersion(version));

    [Fact]
    public void FindsJDKsInInstallFoldersAndJAVAHOMENewestFirst()
    {
        var jdk17 = FakeJdk("jdk-17", "17.0.9");
        FakeJdk("jdk-21", "21.0.2");
        var home = FakeJdk("custom", "11.0.1", parent: Path.Combine(folder, "elsewhere"));
        Directory.CreateDirectory(Path.Combine(folder, "not-a-jdk"));

        var found = JdkLocator.FindAll(new JdkSearchOptions { VersionedJavaHomes = [], JavaHome = home, PathFolders = [], InstallFolders = [folder] });

        Assert.Equal([21, 17, 11], found.Select(j => j.FeatureVersion));
        Assert.Contains(found, j => j.HomePath == jdk17);
        Assert.Equal(21, JdkLocator.FindFor(17, new JdkSearchOptions { VersionedJavaHomes = [], JavaHome = null, PathFolders = [], InstallFolders = [folder] })!.FeatureVersion);
        Assert.Null(JdkLocator.FindFor(22, new JdkSearchOptions { VersionedJavaHomes = [], JavaHome = null, PathFolders = [], InstallFolders = [folder] }));
    }

    [Fact]
    public void FindsTheJdksThatVersionedJavaHomeVariablesName()
    {
        var jdk8 = FakeJdk("jdk-8", "1.8.0_462", parent: Path.Combine(folder, "eight"));
        var jdk21 = FakeJdk("jdk-21", "21.0.2", parent: Path.Combine(folder, "toolcache"));

        var found = JdkLocator.FindAll(new JdkSearchOptions { VersionedJavaHomes = [jdk21], JavaHome = jdk8, PathFolders = [], InstallFolders = [] });

        Assert.Equal(21, found[0].FeatureVersion);
        Assert.Equal(21, JdkLocator.FindFor(17, new JdkSearchOptions { VersionedJavaHomes = [jdk21], JavaHome = jdk8, PathFolders = [], InstallFolders = [] })!.FeatureVersion);
    }

    [Fact]
    public void FindsTheJDKOfJavaOnThePATHThroughLinks()
    {
        if (OperatingSystem.IsWindows())
            Assert.Skip("Links need extra rights on Windows.");

        var home = FakeJdk("linked", "21.0.1");
        Directory.CreateDirectory(Path.Combine(home, "bin"));
        File.WriteAllText(Path.Combine(home, "bin", "java"), "");
        var bin = Directory.CreateDirectory(Path.Combine(folder, "profile-bin")).FullName;
        File.CreateSymbolicLink(Path.Combine(bin, "java"), Path.Combine(home, "bin", "java"));

        var found = JdkLocator.FindAll(new JdkSearchOptions { VersionedJavaHomes = [], JavaHome = null, PathFolders = [bin], InstallFolders = [] });

        Assert.Equal(home, Assert.Single(found).HomePath);
    }

    [Fact]
    public void ListsThePreferredJDKFirstAndServesFromItWhenItCan()
    {
        FakeJdk("jdk-25", "25.0.1");
        var extra = Path.Combine(folder, "runesmith");
        var preferred = FakeJdk("temurin-21.0.4", "21.0.4", parent: extra);
        var options = new JdkSearchOptions { VersionedJavaHomes = [], JavaHome = null, PathFolders = [], InstallFolders = [folder], ExtraInstallFolders = [extra], PreferredHome = preferred + Path.DirectorySeparatorChar };

        var found = JdkLocator.FindAll(options);

        Assert.Equal([21, 25], found.Select(j => j.FeatureVersion));
        Assert.Equal(preferred, JdkLocator.FindFor(17, options)!.HomePath);
        Assert.Equal(25, JdkLocator.FindFor(22, options)!.FeatureVersion);
    }

    [Fact]
    public void FindsTheJDKTheTestsRunWith() => Assert.True(TestJdk.Require(8).FeatureVersion >= 8);

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private string FakeJdk(string name, string version, string? parent = null)
    {
        var home = Path.Combine(parent ?? folder, name);
        Directory.CreateDirectory(Path.Combine(home, "lib"));
        File.WriteAllText(Path.Combine(home, "release"), $"IMPLEMENTOR=\"test\"\nJAVA_VERSION=\"{version}\"\n");
        using (ZipFile.Open(Path.Combine(home, "lib", "ct.sym"), ZipArchiveMode.Create))
        {
        }

        return Path.GetFullPath(home);
    }
}
