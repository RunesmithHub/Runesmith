using System.IO.Compression;
using System.Security.Cryptography;
using Runesmith.Java.Sdks;
using Runesmith.Languages.Java.Jdk;
using Runesmith.Plugins.Sdks.Tests;
using Runesmith.Sdk.Sdks;

namespace Runesmith.Java.Tests.Sdks;

public sealed class JdkSdkProviderTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("runesmith-jdks-").FullName;
    private readonly FakeHttp http = new();

    private string ManagedFolder => Path.Combine(folder, "sdks", "jdk");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void ReadsTheDistributions()
    {
        var distributions = FoojayMetadata.ParseDistributions(Fixture("distributions.json"));

        Assert.Contains(new FoojayDistribution("temurin", "Temurin", true), distributions);
        Assert.Contains(new FoojayDistribution("trava", "Trava", false), distributions);
    }

    [Fact]
    public void ReadsPackagesAndTheirDetails()
    {
        var packages = FoojayMetadata.ParsePackages(Fixture("packages-linux-x64.json"));
        var zulu = packages.First(p => p.Distribution == "zulu" && p.MajorVersion == 25 && p.ArchiveType == "tar.gz" && p.LibC == "glibc" && !p.JavaFx);
        var info = FoojayMetadata.ParsePackageInfo(Fixture("package-info.json"));

        Assert.Equal(("25.0.4.1+1", "x64", "lts", true), (zulu.JavaVersion, zulu.Architecture, zulu.TermOfSupport, zulu.DirectlyDownloadable));
        Assert.True(zulu.Size > 0);
        Assert.NotNull(info);
        Assert.Equal("https://cdn.azul.com/zulu/bin/zulu27.28.101-ca-jdk27.0.0-linux_musl_x64.tar.gz", info.Download.ToString());
        Assert.Equal(("sha256", 64), (info.ChecksumType, info.Checksum!.Length));
    }

    [Fact]
    public void OffersOneArchivePerVersionPackageAndProcessorOfMaintainedDistributions()
    {
        var releases = Releases(windows: false, musl: false);

        Assert.DoesNotContain(releases, r => r.Vendor == "Trava");
        var zulu25 = releases.Where(r => r.Vendor == "Zulu" && r.Line == "25").ToList();
        Assert.Equal([null, FoojayMetadata.JavaFxPackage], zulu25.Select(r => r.Package).Order());
        Assert.All(zulu25, r => Assert.True(r.IsLongTermSupport));
        Assert.All(zulu25, r => Assert.Equal("x64", r.Architecture));
        var plain = zulu25.Single(r => r.Package is null);
        var id = FoojayMetadata.PackageId(plain.Download);
        var package = FoojayMetadata.ParsePackages(Fixture("packages-linux-x64.json")).Single(p => p.Id == id);
        Assert.Equal(("tar.gz", "glibc"), (package.ArchiveType, package.LibC));
        Assert.Equal(package.Size, plain.Size);
    }

    [Theory]
    [InlineData(true, false, "zip")]
    [InlineData(false, true, "tar.gz")]
    public void ChoosesTheArchiveOfThePlatform(bool windows, bool musl, string archiveType)
    {
        var packages = FoojayMetadata.ParsePackages(Fixture("packages-linux-x64.json"));
        var zulu = Releases(windows, musl).Single(r => r.Vendor == "Zulu" && r.Line == "25" && r.Package is null);
        var package = packages.Single(p => p.Id == FoojayMetadata.PackageId(zulu.Download));

        Assert.Equal(archiveType, package.ArchiveType);
        if (musl)
            Assert.Equal("musl", package.LibC);
    }

    [Fact]
    public void AsksForThePlatformsPackages()
    {
        var uri = FoojayMetadata.PackagesUri("macos", "aarch64").ToString();

        Assert.StartsWith("https://api.foojay.io/disco/v3.0/packages?operating_system=macos&architecture=aarch64&archive_type=tar.gz&archive_type=zip", uri, StringComparison.Ordinal);
        Assert.Contains("&distribution=temurin", uri, StringComparison.Ordinal);
        Assert.Contains("&release_status=ga&latest=available", uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListsReleasesFromFoojayAndKeepsThemForADay()
    {
        http.Serve(FoojayMetadata.DistributionsUri.ToString(), Fixture("distributions.json"))
            .Serve(FoojayMetadata.PackagesUri("linux", "x64").ToString(), Fixture("packages-linux-x64.json"));

        var releases = await Provider().GetReleasesAsync(Token);
        await Provider().GetReleasesAsync(Token);

        Assert.Contains(releases, r => r.Vendor == "Corretto" && r.Version == "25.0.4.1+1");
        Assert.Equal(2, http.Requests.Count(r => r.Contains("foojay", StringComparison.Ordinal)));
    }

    [Fact]
    public void ReadsTemurinReleasesFromAdoptium()
    {
        var available = AdoptiumMetadata.ParseAvailableReleases(Fixture("adoptium-available-releases.json"));
        var releases = AdoptiumMetadata.ParseLatestAssets(Fixture("adoptium-25-linux-x64.json"), isLongTermSupport: true, windows: false);

        Assert.Contains(25, available.Offered);
        Assert.Contains(21, available.Offered);
        var release = Assert.Single(releases);
        Assert.Equal("Temurin", release.Vendor);
        Assert.Equal("25", release.Line);
        Assert.Matches(@"^25\.0\.\d+(\.\d+)?\+\d+$", release.Version);
        Assert.Equal("x64", release.Architecture);
        Assert.Equal("SHA-256", release.ChecksumAlgorithm);
        Assert.Equal(64, release.Checksum?.Length);
        Assert.EndsWith(".tar.gz", release.Download.AbsolutePath, StringComparison.Ordinal);
        Assert.True(release.IsLongTermSupport);
        Assert.Empty(AdoptiumMetadata.ParseLatestAssets(Fixture("adoptium-25-linux-x64.json"), isLongTermSupport: true, windows: true));
    }

    [Fact]
    public async Task TemurinComesFromAdoptiumWhenItAnswers()
    {
        var available = AdoptiumMetadata.ParseAvailableReleases(Fixture("adoptium-available-releases.json"));
        http.Serve(FoojayMetadata.DistributionsUri.ToString(), Fixture("distributions.json"))
            .Serve(FoojayMetadata.PackagesUri("linux", "x64").ToString(), Fixture("packages-linux-x64.json"))
            .Serve(AdoptiumMetadata.AvailableReleasesUri.ToString(), Fixture("adoptium-available-releases.json"));
        foreach (var feature in available.Offered)
            http.Serve(AdoptiumMetadata.LatestAssetsUri(feature, "linux", "x64").ToString(), feature == 25 ? Fixture("adoptium-25-linux-x64.json") : "[]");

        var temurin = (await Provider().GetReleasesAsync(Token)).Where(r => r.Vendor == "Temurin").ToList();

        var release = Assert.Single(temurin);
        Assert.Equal("25", release.Line);
        Assert.Contains("adoptium", release.Download.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallsAJdkFromFoojayCheckingItsChecksum()
    {
        var archive = JdkArchive("jdk-25.0.1+8", "25.0.1", "Eclipse Adoptium");
        var release = ServeFoojay("pkg1", archive, Convert.ToHexStringLower(SHA256.HashData(archive)), "sha256");
        var progress = new ProgressLog<SdkInstallProgress>();

        var sdk = await Provider().InstallAsync(release, progress, Token);

        Assert.Equal(Path.Combine(ManagedFolder, "temurin-25.0.1+8"), sdk.Path);
        Assert.Equal(("25.0.1", "Temurin", SdkSource.Managed, "x64"), (sdk.Version, sdk.Vendor, sdk.Source, sdk.Architecture));
        Assert.Equal("Preparing", progress.Reports[0].Stage);
        if (!OperatingSystem.IsWindows())
            Assert.True(File.GetUnixFileMode(Path.Combine(sdk.Path, "bin", "java")).HasFlag(UnixFileMode.UserExecute));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(folder, "sdks", ".downloads")));
    }

    [Fact]
    public async Task UsesTheHomeInsideAMacBundle()
    {
        var archive = JdkArchive("jdk-21.0.4+7", "21.0.4", "Azul Systems, Inc.", home: "Contents/Home/");
        var release = ServeFoojay("pkg2", archive, null, null) with { Vendor = "Zulu" };

        var sdk = await Provider().InstallAsync(release, new ProgressLog<SdkInstallProgress>(), Token);

        Assert.Equal(Path.Combine(ManagedFolder, "zulu-25.0.1+8", "Contents", "Home"), sdk.Path);
        Assert.Equal("Zulu", sdk.Vendor);
    }

    [Fact]
    public async Task InstallsNothingWhenTheChecksumDiffers()
    {
        var release = ServeFoojay("pkg3", JdkArchive("jdk", "25.0.1", "Eclipse Adoptium"), new string('a', 64), "sha256");

        await Assert.ThrowsAsync<InvalidDataException>(() => Provider().InstallAsync(release, new ProgressLog<SdkInstallProgress>(), Token));

        Assert.False(Directory.Exists(ManagedFolder));
    }

    [Fact]
    public async Task FindsManagedAndSystemJdksWithTheirVendors()
    {
        FakeJdk(Path.Combine(ManagedFolder, "corretto-21.0.4.7.1"), "21.0.4", "Amazon.com Inc.");
        var system = Path.Combine(folder, "usr-lib-jvm");
        FakeJdk(Path.Combine(system, "java-25-temurin"), "25.0.1", "Eclipse Adoptium");
        FakeJdk(Path.Combine(system, "java-8-openjdk"), "1.8.0_392", "Some Builder");
        FakeJdk(Path.Combine(system, "nix-openjdk-11"), "11.0.25", "N/A");
        var javaHome = FakeJdk(Path.Combine(folder, "home", "sapmachine-17"), "17.0.12", "SAP SE");

        var found = await Provider(new JdkSearchOptions { VersionedJavaHomes = [], JavaHome = javaHome, PathFolders = [], InstallFolders = [system] }).FindInstalledAsync(Token);

        Assert.Equal(["25.0.1", "21.0.4", "17.0.12", "11.0.25", "1.8.0_392"], found.Select(j => j.Version));
        Assert.Equal(["Temurin", "Corretto", "SapMachine", null, "Some Builder"], found.Select(j => j.Vendor));
        Assert.Equal([SdkSource.System, SdkSource.Managed, SdkSource.System, SdkSource.System, SdkSource.System], found.Select(j => j.Source));
        Assert.All(found, j => Assert.Equal("arm64", j.Architecture));
    }

    [Fact]
    public void FindsANewerVersionOfTheSameDistributionAndMajor()
    {
        var releases = Releases(windows: false, musl: false);
        var provider = Provider();
        var zulu = new InstalledSdk("jdk", "25.0.1", "/jdk") { Vendor = "Zulu", Architecture = "x64" };

        var update = provider.FindUpdate(zulu, releases);

        Assert.Equal(("25.0.4.1+1", null, "Zulu"), (update?.Version, update?.Package, update?.Vendor));
        Assert.Null(provider.FindUpdate(zulu with { Version = "25.0.4.1" }, releases));
        Assert.Null(provider.FindUpdate(zulu with { Vendor = "Temurin" }, releases));
        Assert.Null(provider.FindUpdate(zulu with { Version = "24.0.2" }, releases));
        Assert.Null(provider.FindUpdate(zulu with { Architecture = "arm64" }, releases));
    }

    [Fact]
    public async Task RemovesOnlyManagedJdks()
    {
        var managed = FakeJdk(Path.Combine(ManagedFolder, "temurin-25.0.1+8"), "25.0.1", "Eclipse Adoptium");
        var system = FakeJdk(Path.Combine(folder, "system-jdk"), "21.0.1", "Eclipse Adoptium");
        var provider = Provider();

        await provider.UninstallAsync(new InstalledSdk("jdk", "25.0.1", managed) { Source = SdkSource.Managed }, Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.UninstallAsync(new InstalledSdk("jdk", "21.0.1", system) { Source = SdkSource.Managed }, Token));

        Assert.False(Directory.Exists(managed));
        Assert.True(Directory.Exists(system));
    }

    [Fact]
    public void JavaAnalysisFindsManagedJdksAndPrefersTheDefault()
    {
        var options = new JavaAnalysis(null).Search();

        Assert.Equal([JdkSdkEnvironment.ManagedFolderPath], options.ExtraInstallFolders);
        Assert.Null(options.PreferredHome);
    }

    public void Dispose()
    {
        http.Dispose();
        Directory.Delete(folder, recursive: true);
    }

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Sdks", "Fixtures", name));

    private static IReadOnlyList<SdkRelease> Releases(bool windows, bool musl)
    {
        var maintained = FoojayMetadata.ParseDistributions(Fixture("distributions.json")).Where(d => d.Maintained).Select(d => d.Id).ToHashSet();
        return FoojayMetadata.ToReleases(FoojayMetadata.ParsePackages(Fixture("packages-linux-x64.json")), maintained, windows, musl);
    }

    private JdkSdkProvider Provider(JdkSearchOptions? search = null) =>
        new(http.Client(), new JdkSdkEnvironment(ManagedFolder, Path.Combine(folder, "cache"), "linux", ["x64"])
        {
            Search = search ?? new JdkSearchOptions { VersionedJavaHomes = [], JavaHome = null, PathFolders = [], InstallFolders = [] },
        });

    private SdkRelease ServeFoojay(string id, byte[] archive, string? checksum, string? type)
    {
        var download = $"https://vendor.example.test/{id}.tar.gz";
        http.Serve(FoojayMetadata.PackageInfoUri(id).ToString(), $$"""
            { "result": [ { "filename": "{{id}}.tar.gz", "direct_download_uri": "{{download}}", "checksum": "{{checksum}}", "checksum_type": "{{type}}" } ], "message": "" }
            """);
        http.Serve(download, archive);
        return new SdkRelease("jdk", "25.0.1+8", "Temurin", FoojayMetadata.RedirectUri(id)) { Line = "25", Architecture = "x64" };
    }

    private static byte[] JdkArchive(string top, string version, string implementor, string home = "")
    {
        var java = OperatingSystem.IsWindows() ? "java.exe" : "java";
        return TestArchives.TarGz(
            new ArchiveEntry($"{top}/{home}release", $"IMPLEMENTOR=\"{implementor}\"\nJAVA_VERSION=\"{version}\"\nOS_ARCH=\"x86_64\"\n"),
            new ArchiveEntry($"{top}/{home}bin/{java}", "", TestArchives.Executable),
            new ArchiveEntry($"{top}/{home}lib/ct.sym", "api"));
    }

    private static byte[] EmptyZip()
    {
        using var stream = new MemoryStream();
        using (new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
        }

        return stream.ToArray();
    }

    private static string FakeJdk(string home, string version, string implementor)
    {
        Directory.CreateDirectory(Path.Combine(home, "lib"));
        File.WriteAllText(Path.Combine(home, "release"), $"IMPLEMENTOR=\"{implementor}\"\nJAVA_VERSION=\"{version}\"\nOS_ARCH=\"aarch64\"\n");
        File.WriteAllBytes(Path.Combine(home, "lib", "ct.sym"), EmptyZip());
        return Path.GetFullPath(home);
    }
}
