using System.Composition;
using System.Globalization;
using Runesmith.Languages.Java.Jdk;
using Runesmith.Plugins.Sdks;
using Runesmith.Sdk.Sdks;

namespace Runesmith.Java.Sdks;

/// <summary>Finds, downloads and removes JDKs of several distributions, from the foojay Discovery API and, for Temurin, Adoptium's own API,
/// one folder each.</summary>
[Export(typeof(ISdkProvider))]
public sealed class JdkSdkProvider : ISdkProvider
{
    /// <summary>The kind of JDKs.</summary>
    public const string SdkKind = "jdk";

    private static readonly TimeSpan MetadataAge = TimeSpan.FromDays(1);

    private readonly HttpClient http;
    private readonly JdkSdkEnvironment environment;
    private readonly MetadataCache metadata;

    /// <summary>Creates the provider for this machine.</summary>
    public JdkSdkProvider()
        : this(SdkDownloads.Client, JdkSdkEnvironment.Current())
    {
    }

    internal JdkSdkProvider(HttpClient http, JdkSdkEnvironment environment)
    {
        this.http = http;
        this.environment = environment;
        metadata = new MetadataCache(http, environment.CacheFolder, MetadataAge);
    }

    public string Kind => SdkKind;

    public string Name => "JDK";

    public Task<IReadOnlyList<InstalledSdk>> FindInstalledAsync(CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<InstalledSdk>>(() =>
        {
            var search = environment.Search with { ExtraInstallFolders = [.. environment.Search.ExtraInstallFolders, environment.ManagedFolder] };
            return [.. JdkLocator.FindAll(search).Select(Describe).DistinctBy(sdk => RealPaths.Of(sdk.Path)).OrderByDescending(sdk => NormalizeVersion(sdk.Version), VersionText.Comparer)];
        }, cancellationToken);

    public async Task<IReadOnlyList<SdkRelease>> GetReleasesAsync(CancellationToken cancellationToken)
    {
        var distributions = FoojayMetadata.ParseDistributions(await metadata.GetAsync(FoojayMetadata.DistributionsUri, "distributions.json", cancellationToken).ConfigureAwait(false));
        var maintained = distributions.Where(d => d.Maintained).Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        var packages = await Task.WhenAll(environment.Architectures.Select(async architecture =>
        {
            var uri = FoojayMetadata.PackagesUri(environment.Os, architecture);
            return FoojayMetadata.ParsePackages(await metadata.GetAsync(uri, $"packages-{environment.Os}-{architecture}.json", cancellationToken).ConfigureAwait(false));
        })).ConfigureAwait(false);
        var releases = FoojayMetadata.ToReleases(packages.SelectMany(p => p), maintained, environment.Os == "windows", environment.Musl);
        if (await TemurinReleasesAsync(cancellationToken).ConfigureAwait(false) is not { } temurin)
            return releases;

        var vendor = JdkDistributions.ById("temurin")!.Name;
        List<SdkRelease> merged = [.. releases.Where(r => r.Vendor != vendor), .. temurin];
        merged.Sort((a, b) => VersionText.Compare(b.Version, a.Version));
        return merged;
    }

    // Temurin's own API has every release; when it cannot be reached, foojay's Temurin releases stay.
    private async Task<IReadOnlyList<SdkRelease>?> TemurinReleasesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var available = AdoptiumMetadata.ParseAvailableReleases(
                await metadata.GetAsync(AdoptiumMetadata.AvailableReleasesUri, "adoptium-releases.json", cancellationToken).ConfigureAwait(false));
            var os = AdoptiumMetadata.Os(environment.Os, environment.Musl);
            var lists = await Task.WhenAll(environment.Architectures.SelectMany(architecture => available.Offered.Select(async feature =>
            {
                var uri = AdoptiumMetadata.LatestAssetsUri(feature, os, architecture);
                var json = await metadata.GetAsync(uri, FormattableString.Invariant($"adoptium-{feature}-{os}-{architecture}.json"), cancellationToken).ConfigureAwait(false);
                return AdoptiumMetadata.ParseLatestAssets(json, available.LongTermSupport.Contains(feature), environment.Os == "windows");
            }))).ConfigureAwait(false);
            return [.. lists.SelectMany(list => list)];
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<InstalledSdk> InstallAsync(SdkRelease release, IProgress<SdkInstallProgress> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(progress);
        if (release.Kind != SdkKind)
            throw new ArgumentException($"The release is a {release.Kind}, not a JDK.", nameof(release));

        var download = release;
        if (FoojayMetadata.PackageId(release.Download) is { } id)
        {
            progress.Report(new SdkInstallProgress("Preparing", null));
            var info = FoojayMetadata.ParsePackageInfo(await http.GetStringAsync(FoojayMetadata.PackageInfoUri(id), cancellationToken).ConfigureAwait(false))
                ?? throw new InvalidDataException($"There is no download for {release.Vendor} {release.Version}.");
            download = release with { Download = info.Download, Checksum = release.Checksum ?? info.Checksum, ChecksumAlgorithm = release.ChecksumAlgorithm ?? info.ChecksumType };
        }

        var work = Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(environment.ManagedFolder))!, ".downloads");
        var unpacked = await SdkDownloads.DownloadAndUnpackAsync(http, download, work, progress, cancellationToken).ConfigureAwait(false);
        try
        {
            progress.Report(new SdkInstallProgress("Installing", null));
            var entries = Directory.GetFileSystemEntries(unpacked);
            var content = entries is [var only] && Directory.Exists(only) ? only : unpacked;
            var target = Path.Combine(environment.ManagedFolder, FolderName(release));
            Directory.CreateDirectory(environment.ManagedFolder);
            SdkDownloads.DeleteFolder(target);
            Directory.Move(content, target);
            if (FindHome(target) is not { } home || JdkInstallation.FromHome(home) is not { } jdk)
            {
                SdkDownloads.DeleteFolder(target);
                throw new InvalidDataException($"The download of {release.Vendor} {release.Version} does not contain a JDK.");
            }

            return Describe(jdk);
        }
        finally
        {
            SdkDownloads.DeleteFolder(unpacked);
        }
    }

    public Task UninstallAsync(InstalledSdk sdk, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sdk);
        if (sdk.Kind != SdkKind || sdk.Source != SdkSource.Managed || ManagedFolderOf(sdk.Path) is not { } folder)
            throw new InvalidOperationException($"Runesmith did not install the JDK at {sdk.Path}, so it does not remove it.");

        return Task.Run(() => SdkDownloads.DeleteFolder(folder), cancellationToken);
    }

    public SdkRelease? FindUpdate(InstalledSdk sdk, IReadOnlyList<SdkRelease> releases)
    {
        ArgumentNullException.ThrowIfNull(sdk);
        ArgumentNullException.ThrowIfNull(releases);
        var major = JdkInstallation.ParseFeatureVersion(sdk.Version)?.ToString(CultureInfo.InvariantCulture);
        var candidates = releases
            .Where(r => r.Kind == SdkKind && r.Line == major && string.Equals(r.Vendor, sdk.Vendor, StringComparison.OrdinalIgnoreCase))
            .Where(r => sdk.Architecture is null || r.Architecture is null || r.Architecture == sdk.Architecture)
            .ToList();
        var plain = candidates.Where(r => r.Package is null).ToList();
        var newest = (plain.Count > 0 ? plain : candidates).MaxBy(r => r.Version, VersionText.Comparer);
        return newest is not null && VersionText.Compare(NormalizeVersion(newest.Version), NormalizeVersion(sdk.Version), includeBuild: false) > 0 ? newest : null;
    }

    /// <summary>Writes Java 8's version the way later ones are: <c>8.0.392</c> for <c>1.8.0_392</c>.</summary>
    internal static string NormalizeVersion(string version) =>
        version.StartsWith("1.", StringComparison.Ordinal) ? version[2..].Replace('_', '.') : version;

    /// <summary>Finds the JDK home in an unpacked folder: the shallowest folder with a <c>release</c> file and <c>bin/java</c>.</summary>
    internal static string? FindHome(string folder)
    {
        var java = OperatingSystem.IsWindows() ? "java.exe" : "java";
        var level = new List<string> { folder };
        for (var depth = 0; depth < 4 && level.Count > 0; depth++)
        {
            if (level.FirstOrDefault(d => File.Exists(Path.Combine(d, "release")) && File.Exists(Path.Combine(d, "bin", java))) is { } home)
                return home;
            level = [.. level.SelectMany(d => Directory.EnumerateDirectories(d)).Order(StringComparer.Ordinal)];
        }

        return null;
    }

    private InstalledSdk Describe(JdkInstallation jdk)
    {
        var properties = ReleaseProperties(jdk.HomePath);
        var managed = ManagedFolderOf(jdk.HomePath);
        var distribution = managed is not null ? JdkDistributions.ById(Path.GetFileName(managed).Split('-')[0]) : null;
        var implementor = properties.GetValueOrDefault("IMPLEMENTOR") is { Length: > 0 } value and not "N/A" ? value : null;
        distribution ??= JdkDistributions.ByImplementor(implementor);
        return new InstalledSdk(SdkKind, jdk.Version, jdk.HomePath)
        {
            Vendor = distribution?.Name ?? implementor,
            Source = managed is null ? SdkSource.System : SdkSource.Managed,
            Architecture = properties.TryGetValue("OS_ARCH", out var architecture) ? JdkSdkEnvironment.Architecture(architecture) : null,
        };
    }

    // The subfolder of the managed folder a path is in, or null when it is outside it.
    private string? ManagedFolderOf(string path)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(environment.ManagedFolder));
        var relative = Path.GetRelativePath(root, Path.GetFullPath(path));
        if (relative == "." || relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            return null;

        return Path.Combine(root, relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0]);
    }

    private static string FolderName(SdkRelease release)
    {
        var distribution = JdkDistributions.ByName(release.Vendor)?.Id ?? release.Vendor.ToLowerInvariant().Replace(' ', '_');
        var name = $"{distribution}-{release.Version}{(release.Package is null ? "" : "-fx")}";
        return string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    }

    private static Dictionary<string, string> ReleaseProperties(string home)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var line in File.ReadLines(Path.Combine(home, "release")))
            {
                var equals = line.IndexOf('=', StringComparison.Ordinal);
                if (equals > 0)
                    properties[line[..equals].Trim()] = line[(equals + 1)..].Trim().Trim('"');
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return properties;
    }
}
