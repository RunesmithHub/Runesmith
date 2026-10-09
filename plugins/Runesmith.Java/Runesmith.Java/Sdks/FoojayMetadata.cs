using System.Globalization;
using System.Text.Json;
using Runesmith.Plugins.Sdks;
using Runesmith.Sdk.Sdks;

namespace Runesmith.Java.Sdks;

/// <summary>A JDK distribution as the foojay Discovery API lists it.</summary>
internal sealed record FoojayDistribution(string Id, string Name, bool Maintained);

/// <summary>A downloadable JDK package as the foojay Discovery API lists it.</summary>
internal sealed record FoojayPackage(string Id, string Distribution, int MajorVersion, string JavaVersion, string ArchiveType, string Architecture)
{
    /// <summary>Gets <c>lts</c>, <c>mts</c> or <c>sts</c>.</summary>
    public string? TermOfSupport { get; init; }

    /// <summary>Gets <c>glibc</c>, <c>musl</c>, <c>libc</c> or <c>c_std_lib</c>.</summary>
    public string? LibC { get; init; }

    /// <summary>Gets whether JavaFX comes with it.</summary>
    public bool JavaFx { get; init; }

    /// <summary>Gets whether the vendor allows downloading it without a sign-in.</summary>
    public bool DirectlyDownloadable { get; init; }

    /// <summary>Gets the archive's size in bytes, when known.</summary>
    public long? Size { get; init; }
}

/// <summary>Where a package downloads from and its checksum.</summary>
internal sealed record FoojayPackageInfo(Uri Download, string? Checksum, string? ChecksumType);

/// <summary>Reads the foojay Discovery API's distributions, packages and package details.</summary>
internal static class FoojayMetadata
{
    /// <summary>The variant name of a JDK that comes with JavaFX.</summary>
    public const string JavaFxPackage = "JDK with JavaFX";

    private const string Api = "https://api.foojay.io/disco/v3.0/";

    /// <summary>The list of distributions.</summary>
    public static readonly Uri DistributionsUri = new(Api + "distributions?include_versions=false");

    /// <summary>Gets the address of the newest GA JDK packages of the offered distributions for a system and processor.</summary>
    /// <param name="os">The system as foojay names it: <c>linux</c>, <c>macos</c> or <c>windows</c>.</param>
    /// <param name="architecture">The processor as foojay names it, such as <c>x64</c> or <c>aarch64</c>.</param>
    public static Uri PackagesUri(string os, string architecture)
    {
        var distributions = string.Concat(JdkDistributions.All.Select(d => "&distribution=" + d.Id));
        return new Uri($"{Api}packages?operating_system={os}&architecture={architecture}&archive_type=tar.gz&archive_type=zip"
            + $"&package_type=jdk&release_status=ga&latest=available{distributions}");
    }

    /// <summary>Gets the address of a package's details, which have its download and checksum.</summary>
    public static Uri PackageInfoUri(string id) => new($"{Api}ids/{Uri.EscapeDataString(id)}");

    /// <summary>Gets the address that redirects to a package's archive.</summary>
    public static Uri RedirectUri(string id) => new($"{Api}ids/{Uri.EscapeDataString(id)}/redirect");

    /// <summary>Gets the package id from an address made by <see cref="RedirectUri"/>, or null when it is another address.</summary>
    public static string? PackageId(Uri download)
    {
        var segments = download.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return download.Host == "api.foojay.io" && segments is [.., "ids", var id, "redirect"] ? Uri.UnescapeDataString(id) : null;
    }

    /// <summary>Reads the list of distributions.</summary>
    public static IReadOnlyList<FoojayDistribution> ParseDistributions(string json)
    {
        using var document = JsonDocument.Parse(json);
        return [.. Result(document).Select(d => new FoojayDistribution(Text(d, "api_parameter") ?? "", Text(d, "name") ?? "", Bool(d, "maintained"))).Where(d => d.Id.Length > 0)];
    }

    /// <summary>Reads a list of packages.</summary>
    public static IReadOnlyList<FoojayPackage> ParsePackages(string json)
    {
        using var document = JsonDocument.Parse(json);
        var packages = new List<FoojayPackage>();
        foreach (var item in Result(document))
        {
            if (Text(item, "id") is not { } id || Text(item, "distribution") is not { } distribution || Text(item, "java_version") is not { } version
                || !item.TryGetProperty("major_version", out var major) || major.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            packages.Add(new FoojayPackage(id, distribution, major.GetInt32(), version, Text(item, "archive_type") ?? "", Text(item, "architecture") ?? "")
            {
                TermOfSupport = Text(item, "term_of_support"),
                LibC = Text(item, "lib_c_type"),
                JavaFx = Bool(item, "javafx_bundled"),
                DirectlyDownloadable = Bool(item, "directly_downloadable"),
                Size = item.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number && size.GetInt64() > 0 ? size.GetInt64() : null,
            });
        }

        return packages;
    }

    /// <summary>Reads a package's details, or returns null when they have no download.</summary>
    public static FoojayPackageInfo? ParsePackageInfo(string json)
    {
        using var document = JsonDocument.Parse(json);
        foreach (var item in Result(document))
        {
            if (Text(item, "direct_download_uri") is { Length: > 0 } uri)
                return new FoojayPackageInfo(new Uri(uri), NullIfEmpty(Text(item, "checksum")), NullIfEmpty(Text(item, "checksum_type")));
        }

        return null;
    }

    /// <summary>Turns packages into the releases this machine can install: one archive per version, variant and processor, of the offered
    /// distributions that are still maintained, newest first.</summary>
    /// <param name="packages">The packages.</param>
    /// <param name="maintained">The ids of the maintained distributions.</param>
    /// <param name="windows">Whether this is Windows, which installs zip archives; other systems install tar.gz ones.</param>
    /// <param name="musl">Whether this Linux uses musl instead of glibc.</param>
    public static IReadOnlyList<SdkRelease> ToReleases(IEnumerable<FoojayPackage> packages, IReadOnlySet<string> maintained, bool windows, bool musl)
    {
        var archiveType = windows ? "zip" : "tar.gz";
        var releases = new List<SdkRelease>();
        var chosen = packages
            .Where(p => p.DirectlyDownloadable && maintained.Contains(p.Distribution) && JdkDistributions.ById(p.Distribution) is not null)
            .Where(p => p.ArchiveType is "zip" or "tar.gz")
            .Where(p => p.LibC is not ("glibc" or "musl") || p.LibC == (musl ? "musl" : "glibc"))
            .GroupBy(p => (p.Distribution, p.JavaVersion, p.JavaFx, p.Architecture))
            .Select(group => group.OrderByDescending(p => p.ArchiveType == archiveType).First());
        foreach (var package in chosen)
        {
            var distribution = JdkDistributions.ById(package.Distribution)!;
            releases.Add(new SdkRelease(JdkSdkProvider.SdkKind, package.JavaVersion, distribution.Name, RedirectUri(package.Id))
            {
                Line = package.MajorVersion.ToString(CultureInfo.InvariantCulture),
                Package = package.JavaFx ? JavaFxPackage : null,
                Architecture = JdkSdkEnvironment.Architecture(package.Architecture),
                IsLongTermSupport = string.Equals(package.TermOfSupport, "lts", StringComparison.OrdinalIgnoreCase),
                Size = package.Size,
            });
        }

        releases.Sort((a, b) => VersionText.Compare(b.Version, a.Version));
        return releases;
    }

    private static List<JsonElement> Result(JsonDocument document) =>
        document.RootElement.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Array ? [.. result.EnumerateArray()] : [];

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Bool(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? NullIfEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
}
