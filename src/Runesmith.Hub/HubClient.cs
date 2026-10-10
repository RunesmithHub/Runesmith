using Runesmith.Hub.Catalog;
using Runesmith.Hub.Enforcement;
using Runesmith.Hub.Installing;
using Runesmith.Hub.Network;
using Runesmith.Hub.State;
using Runesmith.Hub.Updates;
using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Catalog;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Resolution;
using RunesmithHub.Protocol.Updating;
using RunesmithHub.Protocol.Versioning;

namespace Runesmith.Hub;

/// <summary>Runesmith's view of the plugin hub: keeps the verified index up to date, resolves install plans, installs, updates and removes hub
/// plugins, and says what the hub's states mean for what is installed.</summary>
/// <remarks>Installs and updates run only with an index verified in this session. Every change takes effect at the next start.</remarks>
public sealed class HubClient
{
    private readonly Lock gate = new();
    private readonly HubConfiguration configuration;
    private readonly RunesmithHost host;
    private readonly SemanticVersion? runesmithVersion;
    private readonly IFileDownloader downloader;
    private readonly Func<Uri, IIndexFetcher> fetcherFor;
    private readonly TimeProvider time;
    private readonly HubStateStore store;
    private readonly PackageInstaller installer;
    private readonly string? rootProblem;
    private IndexView? view;
    private HubCatalog? catalog;
    private HubState state;
    private HubStatus status;
    private bool enabled = true;

    /// <param name="runesmithVersion">Runesmith's own version, which the index's policy may require to be recent enough.</param>
    /// <param name="fetcherFor">Creates the fetcher for an index base URL; it throws <see cref="ArgumentException"/> for addresses it refuses.</param>
    public HubClient(
        HubConfiguration configuration, HubPaths paths, RunesmithHost host, string runesmithVersion, IFileDownloader downloader,
        Func<Uri, IIndexFetcher> fetcherFor, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(paths);
        this.configuration = configuration;
        Paths = paths;
        this.host = host;
        this.runesmithVersion = SemanticVersion.TryParse(runesmithVersion, out var version) ? version : null;
        this.downloader = downloader;
        this.fetcherFor = fetcherFor;
        this.time = time;
        store = new HubStateStore(paths.StateFile);
        state = store.Load();
        installer = new PackageInstaller(paths, downloader, store, time);

        if (configuration.TrustRoot is { } root)
        {
            try
            {
                view = Updater(new MirroredIndexFetcher([]), root).LoadSavedView();
            }
            catch (InvalidDataException exception)
            {
                rootProblem = $"The shipped trust root is not valid: {exception.Message}";
            }
        }

        catalog = view is null ? null : new HubCatalog(view);
        status = Initial();
    }

    /// <summary>Raised, on any thread, when the status, the catalog or the installed state changes.</summary>
    public event EventHandler? Changed;

    public HubPaths Paths { get; }

    public HubStatus Status
    {
        get
        {
            lock (gate)
                return status;
        }
    }

    /// <summary>Gets the verified catalog, fresh or saved, or null when there is none.</summary>
    public HubCatalog? Catalog
    {
        get
        {
            lock (gate)
                return status.HasCatalog ? catalog : null;
        }
    }

    /// <summary>Gets the policy's description of each capability, or the defaults.</summary>
    public string CapabilityLabel(string capability)
    {
        lock (gate)
        {
            return view?.Policy.Capabilities.TryGetValue(capability, out var label) == true ? label.Label
                : Capabilities.DefaultLabels.TryGetValue(capability, out var fallback) ? fallback : capability;
        }
    }

    /// <summary>Gets the policy's categories.</summary>
    public IReadOnlyList<string> Categories
    {
        get
        {
            lock (gate)
                return view?.Policy.Categories ?? [];
        }
    }

    public HubState State
    {
        get
        {
            lock (gate)
                return state;
        }
    }

    public RunesmithHost Host => host;

    /// <summary>Turns the hub on or off, as the user's setting says.</summary>
    public void SetEnabled(bool value)
    {
        lock (gate)
        {
            enabled = value;
            status = Initial();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Brings the verified index up to date from the hub and its mirrors.</summary>
    public async Task<HubStatus> RefreshAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Uri> urls;
        lock (gate)
        {
            if (!enabled || configuration.TrustRoot is null || rootProblem is not null)
                return status;
            urls = [.. configuration.IndexUrls, .. (view?.Policy.Mirrors ?? []).Select(m => Uri.TryCreate(m, UriKind.Absolute, out var url) ? url : null).OfType<Uri>()];
        }

        var fetchers = new List<IIndexFetcher>();
        foreach (var url in urls.Distinct())
        {
            try
            {
                fetchers.Add(fetcherFor(url));
            }
            catch (ArgumentException)
            {
            }
        }

        var result = await Updater(new MirroredIndexFetcher(fetchers), configuration.TrustRoot.Value).UpdateAsync(cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow();
        lock (gate)
        {
            if (result.View is { } fresh)
            {
                view = fresh;
                catalog = new HubCatalog(fresh);
            }

            status = !enabled ? new HubStatus(HubStatusKind.TurnedOff)
                : result.Succeeded ? Checked(result.View, now)
                : Failed(result.Failure!, now);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return Status;
    }

    /// <summary>Resolves a request against what the hub installed.</summary>
    public ResolutionResult Resolve(ResolutionRequest request, HubPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(preferences);
        lock (gate)
        {
            if (catalog is null)
                return new ResolutionResult { Failure = new ResolutionFailure(ResolutionFailureKind.NotFound, status.Describe(), [], []) };

            var settings = new ResolutionSettings(host) { AllowedTiers = preferences.AllowedTiers, PreReleaseOptIns = preferences.PreReleases };
            return new Resolver(catalog, settings).Resolve(Installed(state), request);
        }
    }

    /// <summary>Gets a plan that installs the same version of a plugin again, such as after its files changed.</summary>
    public InstallPlan? ReinstallPlan(string id)
    {
        lock (gate)
        {
            if (catalog is null || state.Find(id) is not { } plugin || Moderation.Record(catalog, plugin) is not { } record
                || catalog.GetEffectiveState(id, record.Version) is not { } effective || effective.State is VersionState.Blocked or VersionState.Review or VersionState.Held)
                return null;

            var listing = catalog.FindPlugin(id)!;
            return new InstallPlan([new PlanOperation(OperationKind.Install, id, listing.Name, null, record.Version, listing.Tier,
                plugin.Requested ? OperationReason.Requested : OperationReason.Dependency, record)]);
        }
    }

    /// <summary>Applies an install plan; the changes take effect at the next start.</summary>
    /// <exception cref="InstallException">The plan could not be applied; nothing changed.</exception>
    public async Task ApplyAsync(InstallPlan plan, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        HubState current;
        lock (gate)
        {
            if (!status.CanInstall)
                throw new InstallException(status.Describe());
            current = state;
        }

        var next = await Task.Run(() => installer.ApplyAsync(current, plan, progress, cancellationToken), cancellationToken).ConfigureAwait(false);
        lock (gate)
            state = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Turns automatic updates of one plugin on or off.</summary>
    public void SetAutoUpdate(string id, bool value) => Update(s => s with { Plugins = [.. s.Plugins.Select(p => p.Id == id ? p with { AutoUpdate = value } : p)] });

    /// <summary>Forgets a quarantined plugin and deletes its files.</summary>
    public void Dismiss(string id)
    {
        var folders = State.Quarantined.Where(q => q.Id == id).Select(q => q.Folder).ToList();
        Update(s => s with { Quarantined = [.. s.Quarantined.Where(q => q.Id != id)] });
        foreach (var folder in folders)
            Folders.TryDelete(folder);
    }

    /// <summary>Deletes every quarantined plugin's files.</summary>
    public void EmptyQuarantine()
    {
        Update(s => s with { Quarantined = [] });
        Folders.TryDelete(Paths.Quarantine);
    }

    /// <summary>Gets the installed hub plugins whose version the hub now blocks; they stop at the next start.</summary>
    public IReadOnlyList<InstalledHubPlugin> Blocked()
    {
        lock (gate)
            return catalog is null ? [] : [.. state.Plugins.Where(p => Moderation.StateOf(catalog, p)?.State == VersionState.Blocked)];
    }

    /// <summary>Gets the notices about installed plugins, problems first.</summary>
    /// <param name="changedFiles">The hub plugins whose files changed since they were installed.</param>
    public IReadOnlyList<PluginNotice> Notices(IReadOnlyDictionary<string, IReadOnlyList<string>> changedFiles, HubPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(changedFiles);
        lock (gate)
            return NoticeBuilder.Build(catalog, state, changedFiles, host, preferences);
    }

    /// <summary>Gets the available updates of hub plugins.</summary>
    public IReadOnlyList<UpdateCandidate> Updates(HubPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        HubCatalog? current;
        HubState installed;
        lock (gate)
        {
            current = status.HasCatalog ? catalog : null;
            installed = state;
        }

        return current is null ? [] : UpdateFinder.Find(current, request => Resolve(request, preferences), installed, preferences, host);
    }

    /// <summary>Searches the catalog.</summary>
    public CatalogResults Search(CatalogQuery query, HubPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        return Catalog is { } current ? CatalogSearch.Search(current, query, preferences.AllowedTiers, host) : new CatalogResults([], 0);
    }

    /// <summary>Gets a plugin's icon from the cache, downloading and checking it the first time; null when it can't be had.</summary>
    public async Task<string?> GetIconAsync(HostedImage image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!Hashes.IsSha256(image.Sha256) || image.Length is <= 0 or > 1024 * 1024 || !Uri.TryCreate(image.Url, UriKind.Absolute, out var url))
            return null;

        var path = Path.Combine(Paths.Icons, image.Sha256 + ".png");
        if (File.Exists(path))
            return path;

        try
        {
            using var buffer = new MemoryStream();
            await downloader.DownloadAsync(url, image.Length, buffer, cancellationToken).ConfigureAwait(false);
            if (!Hashes.Matches(buffer.ToArray(), image.Sha256))
                return null;

            Directory.CreateDirectory(Paths.Icons);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(temporary, buffer.ToArray(), cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
            return path;
        }
        catch (Exception exception) when (exception is DownloadException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void Update(Func<HubState, HubState> change)
    {
        lock (gate)
        {
            var next = change(state);
            store.Save(next);
            state = next;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private IndexUpdater Updater(IIndexFetcher fetcher, ReadOnlyMemory<byte> root) =>
        new(fetcher, new FileTrustStore(Paths.TrustStore), root, time);

    private static List<InstalledPlugin> Installed(HubState state) =>
        [.. state.Plugins.Select(p => SemanticVersion.TryParse(p.Version, out var version) ? new InstalledPlugin(p.Id, version, p.Requested) : null).OfType<InstalledPlugin>()];

    private HubStatus Initial()
    {
        if (configuration.TrustRoot is null || rootProblem is not null)
            return new HubStatus(HubStatusKind.NotSetUp, Detail: rootProblem);
        if (!enabled)
            return new HubStatus(HubStatusKind.TurnedOff);

        var checkedAt = SavedAt();
        if (view is not null && view.IsExpired(time.GetUtcNow()))
            return new HubStatus(HubStatusKind.Unverifiable, checkedAt, view.ExpiresAt);
        return new HubStatus(HubStatusKind.Checking, checkedAt);
    }

    private HubStatus Checked(IndexView fresh, DateTimeOffset now) =>
        fresh.Policy.OldestRunesmith is { } oldest && runesmithVersion is { } current && current < oldest
            ? new HubStatus(HubStatusKind.RunesmithTooOld, now)
            : new HubStatus(HubStatusKind.Verified, now);

    private HubStatus Failed(IndexUpdateFailure failure, DateTimeOffset now)
    {
        var checkedAt = status.Kind == HubStatusKind.Verified ? status.CheckedAt : SavedAt();
        return failure.Kind switch
        {
            IndexUpdateFailureKind.Offline when view is not null && view.IsExpired(now) => new HubStatus(HubStatusKind.Unverifiable, checkedAt, view.ExpiresAt, failure.Message),
            IndexUpdateFailureKind.Offline => new HubStatus(HubStatusKind.Offline, checkedAt, Detail: failure.Message),
            IndexUpdateFailureKind.RootExpired or IndexUpdateFailureKind.MetadataExpired =>
                new HubStatus(HubStatusKind.Unverifiable, checkedAt, failure.ExpiredAt ?? view?.ExpiresAt, failure.Message),
            _ => new HubStatus(HubStatusKind.VerificationFailed, checkedAt, Detail: failure.Message),
        };
    }

    private DateTimeOffset? SavedAt()
    {
        var current = Path.Combine(Paths.TrustStore, "current");
        return view is not null && Directory.Exists(current) ? new DateTimeOffset(Directory.GetLastWriteTimeUtc(current), TimeSpan.Zero) : null;
    }
}
