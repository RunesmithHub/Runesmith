using System.Diagnostics;
using Runesmith.Languages.Java.Jdk;

namespace Runesmith.Languages.Java.Tests.Jdk;

public sealed class JdkApiTests
{
    [Theory]
    [InlineData(8, '8')]
    [InlineData(9, '9')]
    [InlineData(10, 'A')]
    [InlineData(17, 'H')]
    [InlineData(25, 'P')]
    public void NamesReleasesAsCtSymDoes(int release, char letter) => Assert.Equal(letter, JdkApi.ReleaseLetter(release));

    [Theory]
    [InlineData("java.lang.Record", 11, false)]
    [InlineData("java.lang.Record", 16, true)]
    [InlineData("java.lang.Record", 17, true)]
    [InlineData("java.util.SequencedCollection", 17, false)]
    [InlineData("java.util.SequencedCollection", 21, true)]
    [InlineData("java.lang.IO", 24, false)]
    [InlineData("java.lang.IO", 25, true)]
    [InlineData("java.lang.ScopedValue", 17, false)]
    [InlineData("java.lang.ScopedValue", 25, true)]
    [InlineData("java.util.concurrent.StructuredTaskScope", 21, true)]
    [InlineData("java.lang.String", 8, true)]
    public void GivesEachReleaseExactlyItsOwnClasses(string type, int release, bool expected) =>
        Assert.Equal(expected, TestJdk.Api(release).Contains(type));

    [Theory]
    [InlineData(8, false)]
    [InlineData(11, true)]
    [InlineData(25, true)]
    public void GivesEachReleaseItsOwnMembers(int release, bool hasIsBlank) =>
        Assert.Equal(hasIsBlank, TestJdk.Api(release).FindClass("java.lang.String")!.Methods.Any(m => m.Name == "isBlank"));

    [Fact]
    public void LeavesOutPackagesModulesDoNotExport()
    {
        var api = TestJdk.Api(21);

        Assert.False(api.Contains("jdk.internal.misc.Unsafe"));
        Assert.DoesNotContain("jdk.internal.misc", api.Packages);
        Assert.Contains("java.util.concurrent", api.Packages);
    }

    [Fact]
    public void LooksTypesUpByPackageAndBySimpleName()
    {
        var api = TestJdk.Api(21);

        Assert.Contains("java.util.Map$Entry", api.GetTypes("java.util"));
        Assert.Contains("java.util.ArrayList", api.GetTypes("java.util"));
        Assert.DoesNotContain(api.GetTypes("java.util"), name => name.Contains("$1", StringComparison.Ordinal));
        var lists = api.FindBySimpleName("List");
        Assert.Contains("java.util.List", lists);
        Assert.Contains("java.awt.List", lists);
        Assert.Empty(api.GetTypes("no.such.package"));
        Assert.Null(api.FindClass("no.such.Type"));
    }

    [Fact]
    public void RefusesAReleaseTheJDKCannotGive()
    {
        var jdk = TestJdk.Require();

        Assert.Throws<ArgumentOutOfRangeException>(() => JdkApi.Open(jdk, jdk.FeatureVersion + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => JdkApi.Open(jdk, 7));
    }

    [Fact]
    public void LoadsTheSecondTimeFromItsCache()
    {
        var jdk = TestJdk.Require();
        var cache = Directory.CreateTempSubdirectory("runesmith-jdk-cache-").FullName;
        try
        {
            var stopwatch = Stopwatch.StartNew();
            using var first = JdkApi.Open(jdk, 21, cache);
            var cold = stopwatch.Elapsed;
            Assert.NotEmpty(Directory.GetFiles(cache, "*.idx"));

            stopwatch.Restart();
            using var second = JdkApi.Open(jdk, 21, cache);
            var cached = stopwatch.Elapsed;

            Assert.Equal(first.TypeNames.Count(), second.TypeNames.Count());
            Assert.Equal(first.FindClass("java.util.HashMap")!.Methods.Count, second.FindClass("java.util.HashMap")!.Methods.Count);
            Assert.True(cached < cold, $"Loading from the cache took {cached.TotalMilliseconds} ms, the first load {cold.TotalMilliseconds} ms.");
            Assert.True(cached < TimeSpan.FromMilliseconds(250), $"Loading from the cache took {cached.TotalMilliseconds} ms.");
        }
        finally
        {
            Directory.Delete(cache, recursive: true);
        }
    }

    [Fact]
    public void IgnoresADamagedCache()
    {
        var jdk = TestJdk.Require();
        var cache = Directory.CreateTempSubdirectory("runesmith-jdk-cache-").FullName;
        try
        {
            using (JdkApi.Open(jdk, 17, cache))
            {
            }

            foreach (var file in Directory.GetFiles(cache))
                File.WriteAllBytes(file, [1, 2, 3]);

            using var api = JdkApi.Open(jdk, 17, cache);
            Assert.True(api.Contains("java.lang.Record"));
        }
        finally
        {
            Directory.Delete(cache, recursive: true);
        }
    }
}
