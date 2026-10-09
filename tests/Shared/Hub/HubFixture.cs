using System.Text.Json;
using Runesmith.Hub;
using Runesmith.Hub.Network;
using RunesmithHub.Protocol.Catalog;
using RunesmithHub.Protocol.Updating;
using RunesmithHub.Protocol.Versioning;

namespace Runesmith.Tests.Hub;

/// <summary>A hub client over the signed sample index, with its own plugins and data folders and a clock inside the index's validity.</summary>
/// <remarks>The index was written by <c>rshub dev fixture &lt;dir&gt; --base-url http://127.0.0.1:47613/</c>, without its test keys.</remarks>
internal sealed class HubFixture : IDisposable
{
    public static readonly Uri BaseUrl = new("http://127.0.0.1:47613/");

    public static readonly RunesmithHost Host = new(new SemanticVersion(0, 1, 0), new SemanticVersion(0, 1, 0), null);

    public HubFixture(string? index = null)
    {
        Index = index ?? SampleIndex;
        Time = new ManualTime(TimestampExpiry(Index).AddDays(-2));
        Paths = new HubPaths(Path.Combine(Home, "plugins"), Path.Combine(Home, "data"));
    }

    /// <summary>Gets the sample index as the build copied it.</summary>
    public static string SampleIndex => Path.Combine(AppContext.BaseDirectory, "HubIndex");

    public string Index { get; }

    public string Home { get; } = Directory.CreateTempSubdirectory("runesmith-hub-").FullName;

    public HubPaths Paths { get; }

    public ManualTime Time { get; }

    public HubConfiguration Configuration => new(File.ReadAllBytes(Path.Combine(Index, "root", "1.json")), [BaseUrl]);

    public HubClient CreateClient(IFileDownloader? downloader = null, Func<Uri, IIndexFetcher>? fetchers = null) =>
        new(Configuration, Paths, Host, "0.1.0", downloader ?? new FixtureDownloader(Index), fetchers ?? (_ => new DirectoryIndexFetcher(Index)), Time);

    /// <summary>Copies the sample index, so a test can change it.</summary>
    public static string CopyIndex()
    {
        var copy = Directory.CreateTempSubdirectory("runesmith-hub-index-").FullName;
        foreach (var file in Directory.EnumerateFiles(SampleIndex, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(copy, Path.GetRelativePath(SampleIndex, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        return copy;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Home, recursive: true);
            if (Index != SampleIndex)
                Directory.Delete(Index, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static DateTimeOffset TimestampExpiry(string index)
    {
        using var envelope = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(index, "timestamp.json")));
        using var payload = JsonDocument.Parse(Convert.FromBase64String(envelope.RootElement.GetProperty("payload").GetString()!));
        return payload.RootElement.GetProperty("expires").GetDateTimeOffset();
    }
}

/// <summary>A clock tests set.</summary>
internal sealed class ManualTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Serves package and image links from the sample index's folder, as the address they name would.</summary>
internal sealed class FixtureDownloader(string index) : IFileDownloader
{
    public List<Uri> Requested { get; } = [];

    public async Task<long> DownloadAsync(Uri url, long maxBytes, Stream destination, CancellationToken cancellationToken)
    {
        Requested.Add(url);
        var path = Path.Combine(index, HubFixture.BaseUrl.MakeRelativeUri(url).OriginalString);
        if (!url.AbsoluteUri.StartsWith(HubFixture.BaseUrl.AbsoluteUri, StringComparison.Ordinal) || !File.Exists(path))
            throw new DownloadException($"{url} answered 404.");

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        if (bytes.Length > maxBytes)
            throw new DownloadException($"{url} is larger than expected.");
        await destination.WriteAsync(bytes, cancellationToken);
        return bytes.Length;
    }
}
