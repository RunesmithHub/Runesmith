using System.Composition;
using Avalonia.Threading;
using Runesmith.Git.Git;
using Runesmith.Sdk.Messaging;
using Runesmith.Sdk.VersionControl;
using Runesmith.Sdk.Workspace;

namespace Runesmith.Git.Repositories;

/// <summary>Follows the Git repository of the open folder: finds it when a folder opens, watches its files and Git folder, and reads its
/// status again 300 ms after they stop changing, one refresh at a time and never on the UI thread.</summary>
[Export(typeof(IRepositoryService))]
[Export]
[Shared]
internal sealed class RepositoryService : IRepositoryService, IDisposable
{
    private static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(300);
    private const int UnfinishedRepositoryRetries = 20;

    private readonly IWorkspace workspace;
    private readonly GitCredentialResolver? credentials;
    private readonly Action<Action> post;
    private readonly TimeSpan debounce;
    private readonly SemaphoreSlim refreshing = new(1, 1);
    private readonly Timer debounceTimer;
    private readonly IDisposable? filesSubscription;
    private readonly Lock gate = new();
    private RepositoryWatcher? watcher;
    private CancellationTokenSource? opening;
    private long requested;
    private long completed;
    private int unfinishedRetries;
    private bool disposed;

    /// <summary>Creates the service for Runesmith, raising <see cref="Changed"/> on the UI thread.</summary>
    [ImportingConstructor]
    public RepositoryService(IWorkspace workspace, IMessageBus messageBus, GitCredentialResolver credentials)
        : this(workspace, messageBus, credentials, action => Dispatcher.UIThread.Post(action), DefaultDebounce)
    {
    }

    /// <summary>Creates the service with the way it reaches the UI thread and its debounce delay, for tests.</summary>
    internal RepositoryService(IWorkspace workspace, IMessageBus? messageBus, GitCredentialResolver? credentials, Action<Action> post, TimeSpan debounce)
    {
        this.workspace = workspace;
        this.credentials = credentials;
        this.post = post;
        this.debounce = debounce;
        debounceTimer = new Timer(_ => _ = RefreshQuietlyAsync(), null, Timeout.Infinite, Timeout.Infinite);
        filesSubscription = messageBus?.Subscribe<FilesChangedMessage>(_ => ScheduleRefresh());
        workspace.Changed += (_, _) => _ = OpenAsync(workspace.RootPath);
        if (workspace.RootPath is { } root)
            _ = OpenAsync(root);
    }

    public RepositoryInfo? Current { get; private set; }

    /// <summary>Gets the repository of the open folder, or null.</summary>
    public GitRepository? Repository { get; private set; }

    /// <summary>Gets the repository's full status from the last refresh, or null.</summary>
    public StatusSnapshot? Status { get; private set; }

    /// <summary>Gets the operation that stopped part way, such as a merge with conflicts, as of the last refresh.</summary>
    public OngoingOperation Ongoing { get; private set; }

    /// <summary>Gets a task that completes when the folder opened last has been looked at, for tests and for commands run right after.</summary>
    public Task Opened { get; private set; } = Task.CompletedTask;

    public event EventHandler? Changed;

    /// <summary>Raised, on any thread, when HEAD moved to another commit, such as after a commit, checkout, reset or pull.</summary>
    public event EventHandler? HeadMoved;

    /// <summary>Raised, on any thread, before a command that changes the repository runs, with its arguments.</summary>
    public event EventHandler<IReadOnlyList<string>>? CommandRunning;

    /// <summary>Raised, on any thread, when the index or HEAD changed on disk.</summary>
    public event EventHandler? IndexChanged;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var request = Interlocked.Increment(ref requested);
        await refreshing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // A refresh that started after this request was made has answered it already.
            if (Interlocked.Read(ref completed) >= request)
                return;

            var target = Interlocked.Read(ref requested);
            await ReadAsync(cancellationToken).ConfigureAwait(false);
            Interlocked.Exchange(ref completed, target);
        }
        finally
        {
            refreshing.Release();
        }
    }

    /// <summary>Looks for a repository in the open folder again, such as after one was created in it.</summary>
    public Task ReopenAsync() => OpenAsync(workspace.RootPath);

    /// <summary>Schedules a refresh 300 ms from now, or from the latest of the calls before it runs.</summary>
    public void ScheduleRefresh()
    {
        lock (gate)
        {
            if (!disposed)
                debounceTimer.Change(debounce, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            debounceTimer.Dispose();
        }

        filesSubscription?.Dispose();
        watcher?.Dispose();
        opening?.Cancel();
        opening?.Dispose();
    }

    private Task OpenAsync(string? root)
    {
        opening?.Cancel();
        var cancellation = opening = new CancellationTokenSource();
        return Opened = Task.Run(() => OpenCoreAsync(root, cancellation.Token));
    }

    private async Task OpenCoreAsync(string? root, CancellationToken cancellationToken)
    {
        var repository = root is null ? null : await GitRepository.DiscoverAsync(root, credentials, cancellationToken).ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested)
            return;

        watcher?.Dispose();
        watcher = null;
        Repository = repository;
        if (repository is null)
        {
            Status = null;
            Ongoing = OngoingOperation.None;
            Publish(null);
            if (root is not null && Directory.Exists(root))
            {
                watcher = RepositoryWatcher.ForMissingRepository(root, () => _ = ReopenAsync());
                // A .git that is not a repository yet is usually one git is still writing, which the folder watcher will not report again.
                if (Path.Exists(Path.Combine(root, ".git")) && unfinishedRetries++ < UnfinishedRepositoryRetries)
                    _ = ReopenAfterDebounceAsync(cancellationToken);
            }

            return;
        }

        unfinishedRetries = 0;

        repository.Running += (_, arguments) => CommandRunning?.Invoke(this, arguments);
        watcher = new RepositoryWatcher(repository.GitDirectory, ScheduleRefresh, () => IndexChanged?.Invoke(this, EventArgs.Empty));
        await RefreshQuietlyAsync().ConfigureAwait(false);
    }

    private async Task ReopenAfterDebounceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(debounce, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await ReopenAsync().ConfigureAwait(false);
    }

    private async Task RefreshQuietlyAsync()
    {
        try
        {
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is GitException or IOException or OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    private async Task ReadAsync(CancellationToken cancellationToken)
    {
        if (Repository is not { } repository)
            return;

        var statusTask = repository.GetStatusAsync(cancellationToken);
        var remotesTask = repository.GetRemotesAsync(cancellationToken);
        var status = await statusTask.ConfigureAwait(false);
        var remotes = await remotesTask.ConfigureAwait(false);
        if (!ReferenceEquals(repository, Repository))
            return;

        var branch = status.Branch;
        var info = new RepositoryInfo(repository.Root, branch.Head, branch.Oid, branch.Upstream, branch.Ahead, branch.Behind, remotes);
        var ongoing = repository.GetOngoingOperation();
        var previous = Current;
        var changed = !SameInfo(previous, info) || !status.IsSameAs(Status) || ongoing != Ongoing;
        Status = status;
        Ongoing = ongoing;
        if (changed)
            Publish(info);
        if (previous?.Head != info.Head || previous?.Root != info.Root)
            HeadMoved?.Invoke(this, EventArgs.Empty);
    }

    private void Publish(RepositoryInfo? info)
    {
        Current = info;
        post(() => Changed?.Invoke(this, EventArgs.Empty));
    }

    private static bool SameInfo(RepositoryInfo? a, RepositoryInfo? b) =>
        a is not null && b is not null && a.Root == b.Root && a.Branch == b.Branch && a.Head == b.Head && a.Upstream == b.Upstream && a.Ahead == b.Ahead
        && a.Behind == b.Behind && a.Remotes.SequenceEqual(b.Remotes);
}
