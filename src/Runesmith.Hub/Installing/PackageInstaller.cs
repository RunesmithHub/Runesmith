using System.Globalization;
using Runesmith.Hub.Network;
using Runesmith.Hub.State;
using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Packaging;
using RunesmithHub.Protocol.Resolution;

namespace Runesmith.Hub.Installing;

/// <summary>Applies an install plan as one transaction: downloads every package, checks its size and SHA-256, unpacks it safely into the staging
/// area, checks its manifest against the version record, and then commits the installed state in one write. The new folders take their place
/// at the next start. If anything fails, nothing changes.</summary>
internal sealed class PackageInstaller(HubPaths paths, IFileDownloader downloader, HubStateStore store, TimeProvider time)
{
    public async Task<HubState> ApplyAsync(HubState state, InstallPlan plan, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        var installs = plan.Changes.Where(op => op.Kind is OperationKind.Install or OperationKind.Update or OperationKind.Downgrade).ToList();
        var removals = plan.Changes.Where(op => op.Kind == OperationKind.Remove).Select(op => op.PluginId).ToList();
        var transaction = time.GetUtcNow().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8];
        var root = Path.Combine(paths.Staging, transaction);
        try
        {
            var downloads = Directory.CreateDirectory(Path.Combine(root, ".downloads")).FullName;
            var packages = new List<(PlanOperation Operation, string File)>();
            foreach (var operation in installs)
            {
                progress?.Report(new InstallProgress(InstallStep.Downloading, operation.Name));
                packages.Add((operation, await DownloadAsync(operation, downloads, cancellationToken).ConfigureAwait(false)));
            }

            var entries = new List<InstalledHubPlugin>();
            foreach (var (operation, file) in packages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new InstallProgress(InstallStep.Unpacking, operation.Name));
                entries.Add(Unpack(state, operation, file, Path.Combine(root, operation.PluginId)));
            }

            Folders.TryDelete(downloads);
            progress?.Report(new InstallProgress(InstallStep.Committing));
            var next = Commit(state, entries, removals, transaction);
            store.Save(next);
            progress?.Report(new InstallProgress(InstallStep.Done));
            return next;
        }
        catch (IOException exception)
        {
            Folders.TryDelete(root);
            throw new InstallException($"Runesmith could not write the plugin files, so nothing changed: {exception.Message}", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            Folders.TryDelete(root);
            throw new InstallException($"Runesmith may not write to the plugins folder, so nothing changed: {exception.Message}", exception);
        }
        catch
        {
            Folders.TryDelete(root);
            throw;
        }
    }

    private async Task<string> DownloadAsync(PlanOperation operation, string folder, CancellationToken cancellationToken)
    {
        var package = operation.Record?.Package ?? throw new InstallException($"{operation.Name} {operation.To} has no package to download.");
        var path = Path.Combine(folder, $"{operation.PluginId}-{operation.To}.rsplugin");
        var urls = package.Urls.Select(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null).OfType<Uri>().ToList();
        DownloadException? failure = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            foreach (var url in urls)
            {
                long length;
                try
                {
                    await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                    length = await downloader.DownloadAsync(url, package.Length, file, cancellationToken).ConfigureAwait(false);
                }
                catch (DownloadException exception)
                {
                    failure = exception;
                    continue;
                }

                if (length != package.Length || !Matches(path, package.Sha256))
                {
                    File.Delete(path);
                    throw new InstallException(
                        $"The download of {operation.Name} {operation.To} did not match what the hub published, so nothing was installed. Try again later.",
                        $"{url}: expected {package.Length} bytes with SHA-256 {package.Sha256}, got {length} bytes with a different hash.");
                }

                return path;
            }
        }

        throw new InstallException($"{operation.Name} {operation.To} could not be downloaded, so nothing was installed. Check your connection and try again.", failure?.Message);
    }

    private static bool Matches(string path, string sha256)
    {
        using var stream = File.OpenRead(path);
        return string.Equals(Hashes.Sha256(stream), sha256, StringComparison.Ordinal);
    }

    private InstalledHubPlugin Unpack(HubState state, PlanOperation operation, string file, string folder)
    {
        var record = operation.Record!;
        var problems = new List<string>();
        try
        {
            using var stream = File.OpenRead(file);
            using var archive = PackageArchive.Open(stream);
            var manifest = archive.ReadManifest();
            archive.ExtractTo(folder);
            problems.AddRange(PackageVerifier.CheckAssemblies(folder, manifest));
            problems.AddRange(PackageVerifier.CheckAgainstRecord(manifest, operation.PluginId, record));
            if (!string.Equals(manifest.Assemblies.Contracts.Sha256, record.Contracts.Sha256, StringComparison.Ordinal))
                problems.Add("The contracts assembly differs from the record's.");
        }
        catch (PackageException exception)
        {
            problems.AddRange(exception.Problems);
        }

        if (problems.Count > 0)
            throw new InstallException($"{operation.Name} {operation.To} does not match what the hub published, so nothing was installed.", string.Join(" ", problems));

        var previous = state.Find(operation.PluginId);
        return new InstalledHubPlugin(operation.PluginId, operation.To!.ToString(), record.Package!.Sha256, (operation.Tier ?? default).ToString().ToLowerInvariant(),
            Requested: operation.Reason == OperationReason.Requested || previous?.Requested == true)
        {
            AutoUpdate = previous?.AutoUpdate ?? true,
            InstalledAt = time.GetUtcNow(),
            Files = PluginFiles.Record(folder),
        };
    }

    private HubState Commit(HubState state, List<InstalledHubPlugin> entries, List<string> removals, string transaction)
    {
        var installed = entries.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
        var touched = installed.Concat(removals).ToHashSet(StringComparer.Ordinal);
        var earlier = state.Pending ?? new PendingChange([], []);
        foreach (var dropped in earlier.Installed.Where(folder => touched.Contains(folder.Id)))
            Folders.TryDelete(Path.Combine(paths.Staging, dropped.Transaction, dropped.Id));

        var pendingInstalled = earlier.Installed.Where(folder => !touched.Contains(folder.Id))
            .Concat(entries.Select(entry => new PendingFolder(entry.Id, entry.Version, transaction)))
            .ToList();
        var pendingRemoved = earlier.Removed.Where(id => !installed.Contains(id))
            .Concat(removals.Where(id => Directory.Exists(paths.PluginFolder(id))))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return state with
        {
            Plugins = [.. state.Plugins.Where(plugin => !touched.Contains(plugin.Id)), .. entries],
            Pending = pendingInstalled.Count == 0 && pendingRemoved.Count == 0 ? null : new PendingChange(pendingInstalled, pendingRemoved),
        };
    }
}
