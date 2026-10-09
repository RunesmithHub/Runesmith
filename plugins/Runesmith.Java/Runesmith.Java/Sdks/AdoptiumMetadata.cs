using System.Globalization;
using System.Text.Json;
using Runesmith.Sdk.Sdks;

namespace Runesmith.Java.Sdks;

/// <summary>The Temurin feature releases Adoptium offers.</summary>
/// <param name="LongTermSupport">The feature versions with long-term support.</param>
/// <param name="MostRecent">The newest feature release.</param>
internal sealed record AdoptiumReleases(IReadOnlyList<int> LongTermSupport, int MostRecent)
{
    /// <summary>Gets the feature versions worth offering: those with long-term support and the newest one.</summary>
    public IReadOnlyList<int> Offered => [.. LongTermSupport.Append(MostRecent).Distinct().Order()];
}

/// <summary>Reads the Adoptium API, the source of Temurin builds; it lists every Temurin release, including recent ones foojay does not.</summary>
internal static class AdoptiumMetadata
{
    private const string Api = "https://api.adoptium.net/v3/";

    /// <summary>The feature releases Adoptium offers.</summary>
    public static readonly Uri AvailableReleasesUri = new(Api + "info/available_releases");

    /// <summary>Gets the address of the newest JDK builds of a feature version for a system and processor.</summary>
    /// <param name="feature">The feature version, such as 25.</param>
    /// <param name="os">The system as Adoptium names it: <c>linux</c>, <c>alpine-linux</c>, <c>mac</c> or <c>windows</c>.</param>
    /// <param name="architecture">The processor as Adoptium names it, such as <c>x64</c> or <c>aarch64</c>.</param>
    public static Uri LatestAssetsUri(int feature, string os, string architecture) =>
        new(FormattableString.Invariant($"{Api}assets/latest/{feature}/hotspot?os={os}&architecture={architecture}&image_type=jdk&vendor=eclipse"));

    /// <summary>Gets Adoptium's name for a system, from foojay's.</summary>
    public static string Os(string os, bool musl) => os switch
    {
        "macos" => "mac",
        "windows" => "windows",
        _ => musl ? "alpine-linux" : "linux",
    };

    /// <summary>Reads the feature releases on offer.</summary>
    public static AdoptiumReleases ParseAvailableReleases(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        int[] lts = root.TryGetProperty("available_lts_releases", out var list) && list.ValueKind == JsonValueKind.Array
            ? [.. list.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.Number).Select(v => v.GetInt32())]
            : [];
        var mostRecent = root.TryGetProperty("most_recent_feature_release", out var recent) && recent.ValueKind == JsonValueKind.Number ? recent.GetInt32() : lts.DefaultIfEmpty(0).Max();
        return new AdoptiumReleases(lts, mostRecent);
    }

    /// <summary>Reads the newest builds of a feature version as releases: the platform's archive of each build.</summary>
    /// <param name="json">The assets.</param>
    /// <param name="isLongTermSupport">Whether the feature version has long-term support.</param>
    /// <param name="windows">Whether this is Windows, which installs zip archives; other systems install tar.gz ones.</param>
    public static IReadOnlyList<SdkRelease> ParseLatestAssets(string json, bool isLongTermSupport, bool windows)
    {
        var extension = windows ? ".zip" : ".tar.gz";
        var vendor = JdkDistributions.ById("temurin")!.Name;
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            return [];

        var releases = new List<SdkRelease>();
        foreach (var asset in document.RootElement.EnumerateArray())
        {
            if (!asset.TryGetProperty("binary", out var binary) || !binary.TryGetProperty("package", out var package)
                || Text(package, "link") is not { } link || Text(package, "name") is not { } name || !name.EndsWith(extension, StringComparison.Ordinal)
                || !asset.TryGetProperty("version", out var version) || Number(version, "major") is not { } major)
            {
                continue;
            }

            releases.Add(new SdkRelease(JdkSdkProvider.SdkKind, VersionOf(version, major), vendor, new Uri(link))
            {
                Line = major.ToString(CultureInfo.InvariantCulture),
                Architecture = Text(binary, "architecture") is { } architecture ? JdkSdkEnvironment.Architecture(architecture) : null,
                IsLongTermSupport = isLongTermSupport,
                Checksum = Text(package, "checksum"),
                ChecksumAlgorithm = Text(package, "checksum") is null ? null : "SHA-256",
                Size = package.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number ? size.GetInt64() : null,
            });
        }

        return releases;
    }

    // The same form foojay uses, such as 21.0.12.1+1, so versions from both sources compare.
    private static string VersionOf(JsonElement version, int major)
    {
        var text = FormattableString.Invariant($"{major}.{Number(version, "minor") ?? 0}.{Number(version, "security") ?? 0}");
        if (Number(version, "patch") is > 0 and var patch)
            text += FormattableString.Invariant($".{patch}");
        return Number(version, "build") is { } build ? text + FormattableString.Invariant($"+{build}") : text;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;

    private static int? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;
}
