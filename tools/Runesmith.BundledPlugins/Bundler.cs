using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Catalog;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Packaging;
using RunesmithHub.Protocol.Updating;
using RunesmithHub.Protocol.Versioning;

namespace Runesmith.BundledPlugins;

/// <summary>
/// Brings each pinned plugin into <c>plugins/&lt;id&gt;</c> of the cache folder, unpacked as the hub installs it. The pin must name a published
/// version of an official plugin whose package the signed index pins with the same SHA-256. Packages are kept by hash, so a build with every
/// package at hand needs the network only to look at the index, and works from the index verified last time when it can't reach it.
/// </summary>
/// <param name="required">Whether a plugin that can't be fetched fails the run, as release builds need, instead of being left out.</param>
internal sealed class Bundler(HttpClient indexClient, HttpClient packageClient, TextWriter output, bool required)
{
    private string _origin = "bundled-plugins.json";
    private int _errors;

    public async Task<int> RunAsync(string pinsFile, string rootFile, string cacheFolder)
    {
        _origin = Path.GetFullPath(pinsFile);
        var cache = new CacheFolder(cacheFolder);
        try
        {
            var pins = PinList.Read(pinsFile);
            var view = await VerifiedIndexAsync(pins.Index, File.ReadAllBytes(rootFile), cache.TrustStore).ConfigureAwait(false);
            cache.RemoveAllBut(pins.Plugins.Select(pin => pin.Id));
            foreach (var pin in pins.Plugins)
            {
                try
                {
                    await BundleAsync(pin, view, cache).ConfigureAwait(false);
                }
                catch (UnavailableException exception)
                {
                    cache.Remove(pin.Id);
                    Unavailable($"{pin.Id} {pin.Version} is not bundled: {exception.Message}");
                }
                catch (BundleException exception)
                {
                    cache.Remove(pin.Id);
                    Report("error", exception.Message);
                }
            }
        }
        catch (UnavailableException exception)
        {
            cache.RemoveAllBut([]);
            Unavailable($"No plugins are bundled: {exception.Message}");
        }
        catch (Exception exception) when (exception is BundleException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Report("error", exception.Message);
        }

        return _errors == 0 ? 0 : 1;
    }

    private async Task<IndexView> VerifiedIndexAsync(Uri index, byte[] root, string trustStore)
    {
        var updater = new IndexUpdater(new HttpIndexFetcher(indexClient, index), new FileTrustStore(trustStore), root, TimeProvider.System);
        var result = await updater.UpdateAsync().ConfigureAwait(false);
        if (result.Succeeded)
            return result.View;
        if (result.Failure.Kind != IndexUpdateFailureKind.Offline)
            throw new BundleException($"The plugin hub's index failed verification: {result.Failure.Message}");
        if (result.View is null)
            throw new UnavailableException(result.Failure.Message);

        output.WriteLine($"The plugin hub's index is out of reach ({result.Failure.Message}); using the copy verified by an earlier build.");
        return result.View;
    }

    private async Task BundleAsync(Pin pin, IndexView view, CacheFolder cache)
    {
        var record = Check(pin, view);
        if (cache.HasUnpacked(pin))
            return;

        var package = await PackageAsync(pin, record.Package!, cache).ConfigureAwait(false);
        output.WriteLine($"Unpacking {pin.Id} {pin.Version}.");
        cache.Unpack(pin, staging => Verify(pin, record, package, staging));
    }

    private static VersionRecord Check(Pin pin, IndexView view)
    {
        if (!view.Plugins.TryGetValue(pin.Id, out var plugin))
            throw new BundleException($"{pin.Id} is not in the plugin hub's index.");
        if (plugin.Tier != PluginTier.Official)
            throw new BundleException($"{pin.Id} is not an official plugin.");
        var record = plugin.FindVersion(SemanticVersion.Parse(pin.Version)) ?? throw new BundleException($"{pin.Id} has no version {pin.Version} in the plugin hub's index.");

        var state = EffectiveState.Of(record, plugin, view.Publishers.Find(plugin.Publisher), view.Moderation.Entries);
        if (!state.IsInstallable)
            throw new BundleException($"{pin.Id} {pin.Version} is {state.State.ToString().ToLowerInvariant()} in the plugin hub's index{(state.Moderation is { } entry ? $": {entry.Note}" : "")}.");
        if (record.Package is not { } package || !string.Equals(package.Sha256, pin.Sha256, StringComparison.Ordinal))
            throw new BundleException($"{pin.Id} {pin.Version} is published with SHA-256 {record.Package?.Sha256 ?? "(none)"}, not the {pin.Sha256} pinned in bundled-plugins.json.");
        return record;
    }

    private async Task<string> PackageAsync(Pin pin, HostedFile package, CacheFolder cache)
    {
        var path = cache.PackagePath(package.Sha256);
        if (File.Exists(path) && Matches(path, package))
            return path;

        var partial = $"{path}.{Guid.NewGuid():N}.partial";
        string? failure = null;
        foreach (var url in package.Urls.Select(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri : null).OfType<Uri>())
        {
            output.WriteLine($"Downloading {pin.Id} {pin.Version} from {url}.");
            try
            {
                await DownloadAsync(url, package.Length, partial).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException)
            {
                File.Delete(partial);
                failure = exception.Message;
                continue;
            }

            if (!Matches(partial, package))
            {
                File.Delete(partial);
                throw new BundleException($"The download of {pin.Id} {pin.Version} from {url} does not match the length and SHA-256 the plugin hub's index pins.");
            }

            File.Move(partial, path, overwrite: true);
            return path;
        }

        throw new UnavailableException(failure is null ? "the plugin hub's index lists no HTTPS download for it." : $"it could not be downloaded: {failure}");
    }

    private async Task DownloadAsync(Uri url, long length, string path)
    {
        using var response = await packageClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        long written = 0;
        int read;
        while (written <= length && (read = await input.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
            written += read;
        }
    }

    private static bool Matches(string path, HostedFile package)
    {
        using var stream = File.OpenRead(path);
        return stream.Length == package.Length && string.Equals(Hashes.Sha256(stream), package.Sha256, StringComparison.Ordinal);
    }

    private static void Verify(Pin pin, VersionRecord record, string package, string staging)
    {
        var problems = new List<string>();
        try
        {
            using var stream = File.OpenRead(package);
            using var archive = PackageArchive.Open(stream);
            var manifest = archive.ReadManifest();
            archive.ExtractTo(staging);
            problems.AddRange(PackageVerifier.CheckAssemblies(staging, manifest));
            problems.AddRange(PackageVerifier.CheckAgainstRecord(manifest, pin.Id, record));
            if (!string.Equals(manifest.Assemblies.Contracts.Sha256, record.Contracts.Sha256, StringComparison.Ordinal))
                problems.Add("The contracts assembly differs from the record's.");
        }
        catch (PackageException exception)
        {
            problems.AddRange(exception.Problems);
        }

        if (problems.Count > 0)
            throw new BundleException($"{pin.Id} {pin.Version} does not match what the plugin hub published: {string.Join(" ", problems)}");
    }

    private void Unavailable(string message) => Report(required ? "error" : "warning", message);

    // MSBuild turns lines in this form into build errors and warnings.
    private void Report(string category, string message)
    {
        if (category == "error")
            _errors++;
        output.WriteLine($"{_origin} : {category} RSBUNDLE : {message}");
    }
}

/// <summary>A check failed: the pin, the index or the package is not what it must be.</summary>
internal sealed class BundleException(string message) : Exception(message);

/// <summary>The plugin could not be fetched, as without a network connection.</summary>
internal sealed class UnavailableException(string message) : Exception(message);
