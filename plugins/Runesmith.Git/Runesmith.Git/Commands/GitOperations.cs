using System.Composition;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using HammerUI.Services;
using Runesmith.Git.Git;
using Runesmith.Git.Repositories;
using Runesmith.Git.Views.History;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Settings;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.Tasks;
using Runesmith.Sdk.ToolWindows;
using Runesmith.Sdk.Workspace;

namespace Runesmith.Git.Commands;

/// <summary>The Git actions as the user sees them: each one runs Git, shows long ones as background tasks with Cancel, asks before anything
/// that loses work, offers a way out of the failures it knows, and ends with a toast.</summary>
[Export]
[Shared]
internal sealed partial class GitOperations : IDisposable
{
    /// <summary>The id of the Commit tool window.</summary>
    public const string CommitWindowId = "git.commit";

    /// <summary>The id of the Git tool window with the log.</summary>
    public const string LogWindowId = "git.log";

    private readonly RepositoryService repositories;
    private readonly IWorkspace workspace;
    private readonly IBackgroundTasks tasks;
    private readonly INotificationService notifications;
    private readonly ISettingsService settings;
    private readonly Lazy<IToolWindowManager> toolWindows;
    private readonly IEditorService editors;
    private readonly HistoryNavigator history;
    private readonly IDialogService? dialogs;
    private readonly IDiffService? diffs;
    private readonly IOutputChannel? log;
    private readonly Timer autoFetch;
    private int busy;

    [ImportingConstructor]
    public GitOperations(RepositoryService repositories, IWorkspace workspace, IBackgroundTasks tasks, INotificationService notifications, ISettingsService settings,
        Lazy<IToolWindowManager> toolWindows, IEditorService editors, HistoryNavigator history, [Import(AllowDefault = true)] IOutputService? output,
        [Import(AllowDefault = true)] IDialogService? dialogs, [Import(AllowDefault = true)] IDiffService? diffs)
    {
        this.repositories = repositories;
        this.workspace = workspace;
        this.tasks = tasks;
        this.notifications = notifications;
        this.settings = settings;
        this.toolWindows = toolWindows;
        this.editors = editors;
        this.history = history;
        this.dialogs = dialogs;
        this.diffs = diffs;
        log = output?.GetChannel("Git");
        repositories.CommandRunning += (_, arguments) => log?.AppendLine("git " + string.Join(' ', arguments.Select(Quote)));
        autoFetch = new Timer(_ => Dispatcher.UIThread.Post(() => _ = FetchAsync(quiet: true)), null, Timeout.Infinite, Timeout.Infinite);
        settings.Changed += (_, e) =>
        {
            if (e.Key == GitSettings.AutoFetchMinutes)
                ScheduleAutoFetch();
        };
        ScheduleAutoFetch();
    }

    /// <summary>Gets the repository service the actions refresh after they run.</summary>
    public RepositoryService Repositories => repositories;

    /// <summary>Gets whether a fetch, pull or push runs now.</summary>
    public bool IsSyncing => Volatile.Read(ref busy) > 0;

    /// <summary>Raised on the UI thread when <see cref="IsSyncing"/> changes.</summary>
    public event EventHandler? SyncingChanged;

    private GitRepository? Repository => repositories.Repository;

    private string? CurrentBranch => repositories.Current?.Branch;

    /// <summary>Shows the Commit tool window and puts the keyboard focus in the message.</summary>
    public event EventHandler? FocusMessageRequested;

    /// <summary>Shows the Commit tool window with the message box focused.</summary>
    public void ShowCommitWindow()
    {
        toolWindows.Value.Show(CommitWindowId);
        Dispatcher.UIThread.Post(() => FocusMessageRequested?.Invoke(this, EventArgs.Empty), DispatcherPriority.Background);
    }

    /// <summary>Shows the Git tool window.</summary>
    public void ShowLog() => toolWindows.Value.Show(LogWindowId);

    /// <summary>Shows the Git tool window filtered, such as to a file's history.</summary>
    public void ShowHistory(HistoryRequest request)
    {
        toolWindows.Value.Show(LogWindowId);
        history.Show(request);
    }

    /// <summary>Makes the open folder a Git repository.</summary>
    public async Task InitializeAsync()
    {
        if (workspace.RootPath is not { } root)
            return;

        try
        {
            await GitRepository.InitAsync(root, CancellationToken.None);
            await repositories.ReopenAsync();
            notifications.Notify(NotificationKind.Success, "Created a Git repository", $"{Path.GetFileName(root)} is a Git repository now; its files show in the Commit window.",
                "Show Changes", ShowCommitWindow);
        }
        catch (GitException exception)
        {
            notifications.Notify(NotificationKind.Error, "Could not create a Git repository", exception.Message);
        }
    }

    /// <summary>Fetches every remote, as a background task; a quiet fetch, such as the automatic one, says nothing unless it fails.</summary>
    public async Task FetchAsync(bool quiet = false)
    {
        if (Repository is not { } repository || (quiet && IsSyncing))
            return;
        if (repositories.Current?.Remotes.Count == 0)
        {
            if (!quiet)
                NoRemote("fetch from");
            return;
        }

        var succeeded = await RunAsync("Fetching", (progress, token) => repository.FetchAsync(token, progress), quiet: quiet);
        if (!succeeded || quiet)
            return;

        var info = repositories.Current;
        if (info is { Behind: > 0, Branch: { } branch, Upstream: { } upstream })
            notifications.Notify(NotificationKind.Info, $"{Commits(info.Behind)} to pull", $"{upstream} has {Commits(info.Behind).ToLowerInvariant()} that {branch} does not have yet.",
                "Update Project", () => _ = PullAsync());
        else
            notifications.Notify(NotificationKind.Success, "Fetched", info?.Upstream is { } tracked ? $"{info.Branch} is up to date with {tracked}." : "Every remote is fetched.");
    }

    /// <summary>Pulls the current branch's upstream with the pull strategy setting, keeping local changes.</summary>
    public async Task<bool> PullAsync()
    {
        if (Repository is not { } repository || repositories.Current is not { } info)
            return false;
        if (info.Branch is not { } branch)
        {
            notifications.Notify(NotificationKind.Warning, "HEAD is detached", "Check out a branch to update it.");
            return false;
        }

        if (info.Upstream is not { } upstream)
        {
            if (info.Remotes.Count == 0)
                NoRemote("pull from");
            else
                notifications.Notify(NotificationKind.Warning, $"{branch} has no upstream branch", "Push it to set one, or track a remote branch from the branch widget.", "Push...", () => _ = ShowPushAsync());
            return false;
        }

        var before = info.Head;
        var strategy = GitSettings.ParsePullStrategy(settings.Get<string>(GitSettings.PullStrategy));
        var succeeded = await RunAsync($"Updating {branch}", (progress, token) => repository.PullAsync(strategy, token, progress), handle: PullFailedAsync);
        if (!succeeded)
            return false;

        var after = repositories.Current?.Head;
        var pulled = before is null || after is null || before == after ? 0 : await CountAsync(repository, $"{before}..{after}");
        notifications.Notify(NotificationKind.Success, pulled > 0 ? $"Updated {branch}" : $"{branch} is up to date",
            pulled > 0 ? $"{Commits(pulled)} from {upstream}." : $"Nothing new on {upstream}.");
        return true;
    }

    /// <summary>Shows the Push dialog with the commits to push and where they go, or pushes at once without a dialog service.</summary>
    public async Task ShowPushAsync()
    {
        if (Repository is not { } repository || repositories.Current is not { } info)
            return;
        if (info.Branch is not { } branch)
        {
            notifications.Notify(NotificationKind.Warning, "HEAD is detached", "Check out a branch, or create one here, to push.");
            return;
        }

        var target = await repository.GetPushTargetAsync(branch, CancellationToken.None);
        if (target is null)
        {
            NoRemote("push to");
            return;
        }

        if (dialogs is null)
        {
            await PushAsync();
            return;
        }

        var outgoing = await repository.GetLogAsync(new LogQuery { Revision = target.Value.HasUpstream ? $"{target.Value.Remote}/{target.Value.Branch}..HEAD" : "HEAD --not --remotes", Count = 100 },
            CancellationToken.None);
        var request = await Views.Branches.PushDialog.ShowAsync(dialogs, branch, info.Remotes.Select(r => r.Name).ToList(), target.Value.Remote, target.Value.Branch, outgoing,
            target.Value.HasUpstream, target.Value.HasUpstream ? info.Behind : 0);
        if (request is null)
            return;
        if (request.Force && !await notifications.ConfirmAsync("Force push?",
                $"This replaces {request.Remote}/{request.Branch} with {branch}. Commits there that {branch} does not have are lost, unless the remote moved since your last fetch, in which case the push stops.",
                "Force Push", isDestructive: true))
            return;

        await PushAsync(request.Remote, request.Branch, request.Force);
    }

    /// <summary>Pushes the current branch to where it pushes, setting the upstream on its first push.</summary>
    public async Task<bool> PushAsync(string? remote = null, string? remoteBranch = null, bool force = false)
    {
        if (Repository is not { } repository || repositories.Current is not { Branch: { } branch } info)
            return false;

        var target = await repository.GetPushTargetAsync(branch, CancellationToken.None);
        if (target is null)
        {
            NoRemote("push to");
            return false;
        }

        remote ??= target.Value.Remote;
        remoteBranch ??= target.Value.Branch;
        var isUpstream = target.Value.HasUpstream && remote == target.Value.Remote && remoteBranch == target.Value.Branch;
        var count = isUpstream ? info.Ahead : -1;
        var destination = $"{remote}/{remoteBranch}";
        var succeeded = await RunAsync($"Pushing {branch} to {destination}",
            (progress, token) => repository.PushAsync(branch, remote, remoteBranch, setUpstream: !target.Value.HasUpstream, force, token, progress),
            handle: exception => PushFailedAsync(exception, destination, branch));
        if (succeeded)
            notifications.Notify(NotificationKind.Success, count > 0 ? $"Pushed {Commits(count).ToLowerInvariant()} to {destination}" : $"Pushed {branch} to {destination}",
                target.Value.HasUpstream ? null : $"{branch} tracks {destination} from now on.");
        return succeeded;
    }

    /// <summary>Commits the staged changes and pushes when asked; throws <see cref="GitException"/> for the Commit window to show.</summary>
    public async Task CommitAsync(string message, CommitOptions options, bool push)
    {
        if (Repository is not { } repository)
            return;

        var files = repositories.Status?.Staged.Count() ?? 0;
        var sha = await repository.CommitAsync(message, options, CancellationToken.None);
        await repositories.RefreshAsync();
        var subject = message.Split('\n')[0];
        if (!push)
        {
            notifications.Notify(NotificationKind.Success, options.Amend ? "Amended the last commit" : files == 1 ? "Committed 1 file" : $"Committed {files} files",
                $"{sha[..8]} {subject}");
            return;
        }

        await PushAsync(force: options.Amend && repositories.Current is { Ahead: > 0, Behind: > 0 } && await ConfirmAmendPushAsync());
    }

    /// <summary>Checks out a branch or tag; when local changes would be overwritten, offers a smart checkout or a forced one.</summary>
    public async Task CheckoutAsync(GitRef target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (Repository is not { } repository)
            return;

        if (target.Kind == RefKind.RemoteBranch)
        {
            var refs = await repository.GetRefsAsync(CancellationToken.None);
            if (refs.FirstOrDefault(r => r.Kind == RefKind.LocalBranch && r.Name == target.BranchName) is { } local)
                target = local;
        }

        var name = target.Kind == RefKind.RemoteBranch ? target.BranchName : target.Name;
        await CheckoutCoreAsync(name, (force, progress, token) => repository.CheckoutAsync(target, force, token, progress));
    }

    /// <summary>Checks out a commit with a detached HEAD.</summary>
    public async Task CheckoutCommitAsync(GitCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        if (Repository is { } repository)
            await CheckoutCoreAsync(commit.ShortSha, (force, _, token) => repository.CheckoutCommitAsync(commit.Sha, force, token));
    }

    /// <summary>Asks for a name and creates a branch at a start point, HEAD when null, checking it out when asked.</summary>
    public async Task NewBranchAsync(string? startPoint = null, string? startLabel = null, string initialName = "")
    {
        if (Repository is not { } repository || dialogs is null)
            return;

        var refs = await repository.GetRefsAsync(CancellationToken.None);
        var answer = await GitDialogs.AskNameAsync(dialogs, "New Branch", $"From {startLabel ?? CurrentBranch ?? "HEAD"}", "Name", initialName,
            name => ValidateNewBranchAsync(repository, refs, name), "Create", "Check out the new branch");
        if (answer is null)
            return;

        await RunQuickAsync($"Could not create {answer.Name}", () => repository.CreateBranchAsync(answer.Name, startPoint, answer.Option, CancellationToken.None));
    }

    /// <summary>Asks for a new name for a local branch and renames it.</summary>
    public async Task RenameBranchAsync(GitRef branch)
    {
        ArgumentNullException.ThrowIfNull(branch);
        if (Repository is not { } repository || dialogs is null)
            return;

        var refs = await repository.GetRefsAsync(CancellationToken.None);
        var answer = await GitDialogs.AskNameAsync(dialogs, "Rename Branch", $"Rename {branch.Name}", "New name", branch.Name,
            name => name == branch.Name ? Task.FromResult<string?>("Type a different name.") : ValidateNewBranchAsync(repository, refs, name), "Rename");
        if (answer is not null)
            await RunQuickAsync($"Could not rename {branch.Name}", () => repository.RenameBranchAsync(branch.Name, answer.Name, CancellationToken.None));
    }

    /// <summary>Deletes a branch after asking; a local one that is not fully merged needs a second answer, and can be restored from the toast.</summary>
    public async Task DeleteBranchAsync(GitRef branch)
    {
        ArgumentNullException.ThrowIfNull(branch);
        if (Repository is not { } repository)
            return;

        if (branch.Kind == RefKind.RemoteBranch)
        {
            if (!await notifications.ConfirmAsync($"Delete {branch.Name}?", $"This deletes the branch {branch.BranchName} on {branch.Remote} for everyone who uses it.", "Delete", isDestructive: true))
                return;
            if (await RunAsync($"Deleting {branch.Name}", (progress, token) => repository.DeleteRemoteBranchAsync(branch.Remote!, branch.BranchName, token, progress)))
                notifications.Notify(NotificationKind.Success, $"Deleted {branch.Name}");
            return;
        }

        if (branch.IsHead)
        {
            notifications.Notify(NotificationKind.Warning, $"{branch.Name} is checked out", "Check out another branch before deleting this one.");
            return;
        }

        if (!await notifications.ConfirmAsync($"Delete {branch.Name}?", $"This deletes the local branch {branch.Name}; its commits stay where other branches have them.", "Delete", isDestructive: true))
            return;

        try
        {
            await repository.DeleteBranchAsync(branch.Name, force: false, CancellationToken.None);
        }
        catch (GitException exception) when (exception.Kind == GitErrorKind.BranchNotFullyMerged)
        {
            if (!await notifications.ConfirmAsync($"{branch.Name} is not fully merged",
                    "Some of its commits are on no other branch, and are lost when it is deleted. Delete it anyway?", "Delete Anyway", isDestructive: true))
                return;
            if (!await RunQuickAsync($"Could not delete {branch.Name}", () => repository.DeleteBranchAsync(branch.Name, force: true, CancellationToken.None)))
                return;
        }
        catch (GitException exception)
        {
            notifications.Notify(NotificationKind.Error, $"Could not delete {branch.Name}", exception.Message);
            return;
        }

        await repositories.RefreshAsync();
        notifications.Notify(NotificationKind.Success, $"Deleted {branch.Name}", $"It pointed at {branch.Sha[..Math.Min(8, branch.Sha.Length)]}.", "Restore",
            () => _ = RunQuickAsync($"Could not restore {branch.Name}", () => repository.CreateBranchAsync(branch.Name, branch.Sha, checkout: false, CancellationToken.None)));
    }

    /// <summary>Pushes a local branch that is not checked out to where it pushes.</summary>
    public async Task PushBranchAsync(GitRef branch)
    {
        ArgumentNullException.ThrowIfNull(branch);
        if (branch.IsHead)
        {
            await ShowPushAsync();
            return;
        }

        if (Repository is not { } repository || await repository.GetPushTargetAsync(branch.Name, CancellationToken.None) is not { } target)
        {
            NoRemote("push to");
            return;
        }

        var destination = $"{target.Remote}/{target.Branch}";
        if (await RunAsync($"Pushing {branch.Name} to {destination}",
                (progress, token) => repository.PushAsync(branch.Name, target.Remote, target.Branch, !target.HasUpstream, false, token, progress),
                handle: exception => PushFailedAsync(exception, destination, branch.Name)))
            notifications.Notify(NotificationKind.Success, $"Pushed {branch.Name} to {destination}");
    }

    /// <summary>Makes a local branch track a remote branch with the same name, creating the local branch when there is none.</summary>
    public async Task TrackAsync(GitRef remoteBranch)
    {
        ArgumentNullException.ThrowIfNull(remoteBranch);
        if (Repository is not { } repository)
            return;

        var refs = await repository.GetRefsAsync(CancellationToken.None);
        var exists = refs.Any(r => r.Kind == RefKind.LocalBranch && r.Name == remoteBranch.BranchName);
        if (await RunQuickAsync($"Could not track {remoteBranch.Name}", () => repository.TrackAsync(remoteBranch.BranchName, remoteBranch.Name, create: !exists, CancellationToken.None)))
            notifications.Notify(NotificationKind.Success, $"{remoteBranch.BranchName} tracks {remoteBranch.Name}");
    }

    /// <summary>Merges a branch into the current one.</summary>
    public async Task MergeAsync(GitRef branch)
    {
        ArgumentNullException.ThrowIfNull(branch);
        if (Repository is { } repository &&
            await RunAsync($"Merging {branch.Name} into {CurrentBranch ?? "HEAD"}", (_, token) => repository.MergeAsync(branch.FullName, token), handle: ConflictAsync))
            notifications.Notify(NotificationKind.Success, $"Merged {branch.Name} into {CurrentBranch ?? "HEAD"}");
    }

    /// <summary>Shows the commits that two branches do not share.</summary>
    public void Compare(GitRef branch)
    {
        ArgumentNullException.ThrowIfNull(branch);
        var current = CurrentBranch ?? "HEAD";
        ShowHistory(new HistoryRequest(new LogQuery { Revision = $"HEAD...{branch.FullName}" }, $"Commits {current} and {branch.Name} do not share"));
    }

    /// <summary>Applies a commit's change on the current branch.</summary>
    public async Task CherryPickAsync(GitCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        if (Repository is { } repository && await RunAsync($"Cherry-picking {commit.ShortSha}", (_, token) => repository.CherryPickAsync(commit, token), handle: ConflictAsync))
            notifications.Notify(NotificationKind.Success, $"Cherry-picked {commit.ShortSha}", commit.Subject);
    }

    /// <summary>Adds a commit that undoes a commit's change.</summary>
    public async Task RevertAsync(GitCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        if (Repository is { } repository && await RunAsync($"Reverting {commit.ShortSha}", (_, token) => repository.RevertAsync(commit, token), handle: ConflictAsync))
            notifications.Notify(NotificationKind.Success, $"Reverted {commit.ShortSha}", commit.Subject);
    }

    /// <summary>Moves the current branch to a commit, asking how far; a hard reset asks again.</summary>
    public async Task ResetAsync(GitCommit commit, ResetMode? mode = null)
    {
        ArgumentNullException.ThrowIfNull(commit);
        if (Repository is not { } repository)
            return;

        var branch = CurrentBranch ?? "HEAD";
        if (mode is null)
        {
            var choice = await notifications.ChooseAsync($"Reset {branch} to {commit.ShortSha}",
                "Soft keeps the changes after it staged. Mixed keeps them in the working tree, not staged. Hard throws them and every local change away.",
                ["Hard", "Soft", "Mixed"]);
            if (choice is null)
                return;
            mode = choice.Value switch { 0 => ResetMode.Hard, 1 => ResetMode.Soft, _ => ResetMode.Mixed };
        }

        if (mode == ResetMode.Hard && !await notifications.ConfirmAsync($"Hard reset {branch}?",
                $"Every local change and the commits after {commit.ShortSha} on {branch} are thrown away.", "Reset", isDestructive: true))
            return;

        if (await RunQuickAsync($"Could not reset {branch}", () => repository.ResetAsync(commit.Sha, mode.Value, CancellationToken.None)))
            notifications.Notify(NotificationKind.Success, $"Reset {branch} to {commit.ShortSha}", commit.Subject);
    }

    /// <summary>Asks for a description and stashes every local change.</summary>
    public async Task StashAsync()
    {
        if (Repository is not { } repository || dialogs is null)
            return;
        if (repositories.Status?.ChangedCount == 0)
        {
            notifications.Notify(NotificationKind.Info, "Nothing to stash", "There are no local changes.");
            return;
        }

        var answer = await GitDialogs.AskNameAsync(dialogs, "Stash Changes", "Put every local change, untracked files included, aside", "Description (optional)", "",
            _ => Task.FromResult<string?>(null), "Stash");
        if (answer is not null && await RunQuickAsync("Could not stash", () => repository.StashAsync(answer.Name, CancellationToken.None)))
            notifications.Notify(NotificationKind.Success, "Stashed the local changes", answer.Name.Length > 0 ? answer.Name : null, "Unstash", () => _ = UnstashAsync());
    }

    /// <summary>Brings back a stash entry, asking which when there are several.</summary>
    public async Task UnstashAsync()
    {
        if (Repository is not { } repository)
            return;

        var stashes = await repository.GetStashesAsync(CancellationToken.None);
        if (stashes.Count == 0)
        {
            notifications.Notify(NotificationKind.Info, "The stash is empty");
            return;
        }

        var index = 0;
        if (stashes.Count > 1)
        {
            var choice = await notifications.ChooseAsync("Unstash", "Which changes should come back?", [.. stashes.Select(s => $"{s.Message} ({Relative(s.Date)})")]);
            if (choice is null)
                return;
            index = choice.Value;
        }

        if (await RunAsync("Unstashing", (_, token) => repository.UnstashAsync(index, token), handle: ConflictAsync))
            notifications.Notify(NotificationKind.Success, "Brought back the stashed changes", stashes[index].Message);
    }

    /// <summary>Stops a merge, rebase, cherry-pick or revert that stopped at conflicts.</summary>
    public async Task AbortAsync()
    {
        if (Repository is { } repository && repositories.Ongoing is var ongoing and not OngoingOperation.None &&
            await notifications.ConfirmAsync($"Abort the {Describe(ongoing)}?", "Everything goes back to how it was before it started; conflict resolutions are lost.", "Abort", isDestructive: true))
            await RunQuickAsync("Could not abort", () => repository.AbortAsync(ongoing, CancellationToken.None));
    }

    /// <summary>Goes on with a rebase, cherry-pick or revert once its conflicts are resolved and staged.</summary>
    public async Task ContinueAsync()
    {
        if (Repository is { } repository && repositories.Ongoing is var ongoing and not OngoingOperation.None)
            await RunAsync($"Continuing the {Describe(ongoing)}", (_, token) => repository.ContinueAsync(ongoing, token), handle: ConflictAsync);
    }

    /// <summary>Opens a changed file's diff: HEAD against the staged version, or the staged version against the working copy, which can be
    /// edited.</summary>
    public async Task ShowDiffAsync(StatusEntry entry, bool staged)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (Repository is not { } repository)
            return;

        var full = repository.FullPath(entry.Path);
        if (diffs is null)
        {
            if (File.Exists(full))
                await editors.OpenAsync(full);
            return;
        }

        var name = Path.GetFileName(entry.Path);
        var original = entry.OriginalPath ?? entry.Path;
        var head = await repository.GetFileAsync("HEAD", original, CancellationToken.None) ?? "";
        if (staged)
        {
            var index = await repository.GetFileAsync("", entry.Path, CancellationToken.None) ?? "";
            diffs.Open($"{name} (HEAD and staged)", new DiffSide("HEAD", head) { FilePath = full }, new DiffSide("Staged", index) { FilePath = full });
            return;
        }

        var hasStaged = entry.IsStaged;
        var left = hasStaged ? await repository.GetFileAsync("", entry.Path, CancellationToken.None) ?? head : entry.Kind == StatusKind.Untracked ? "" : head;
        var working = File.Exists(full) ? await File.ReadAllTextAsync(full) : "";
        diffs.Open($"{name} ({(hasStaged ? "staged" : "HEAD")} and working tree)", new DiffSide(hasStaged ? "Staged" : "HEAD", left) { FilePath = full },
            new DiffSide("Working tree", working) { FilePath = full, IsEditable = File.Exists(full) });
    }

    /// <summary>Opens the diff of a file a commit changed, against the commit's first parent.</summary>
    public async Task ShowCommitDiffAsync(GitCommit commit, FileChange change)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(change);
        if (Repository is not { } repository || diffs is null)
            return;

        var parent = commit.Parents.Count > 0 ? commit.Parents[0] : null;
        var before = parent is null || change.Status == 'A' ? "" : await repository.GetFileAsync(parent, change.OldPath ?? change.Path, CancellationToken.None) ?? "";
        var after = change.Status == 'D' ? "" : await repository.GetFileAsync(commit.Sha, change.Path, CancellationToken.None) ?? "";
        var full = repository.FullPath(change.Path);
        diffs.Open($"{Path.GetFileName(change.Path)} ({commit.ShortSha})", new DiffSide(parent is null ? "Empty" : parent[..8], before) { FilePath = full },
            new DiffSide(commit.ShortSha, after) { FilePath = full });
    }

    /// <summary>Opens the diff of a file of the open folder, if it changed.</summary>
    public async Task ShowFileDiffAsync(string fullPath)
    {
        if (Find(fullPath) is not { } entry)
        {
            notifications.Notify(NotificationKind.Info, $"{Path.GetFileName(fullPath)} has no changes");
            return;
        }

        await ShowDiffAsync(entry, staged: !entry.IsUnstaged && entry.IsStaged);
    }

    /// <summary>Rolls back files to HEAD after asking.</summary>
    public async Task RollbackAsync(IReadOnlyList<StatusEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (Repository is not { } repository || entries.Count == 0)
            return;

        var names = string.Join(", ", entries.Take(4).Select(e => Path.GetFileName(e.Path))) + (entries.Count > 4 ? $" and {entries.Count - 4} more" : "");
        var untracked = entries.Count(e => e.Kind == StatusKind.Untracked);
        var title = entries.Count == 1 ? $"Roll back {Path.GetFileName(entries[0].Path)}?" : $"Roll back {entries.Count} files?";
        var message = $"{names} go back to how they are in the last commit; their changes are lost." + (untracked > 0 ? " Untracked files are deleted." : "");
        if (!await notifications.ConfirmAsync(title, message, "Roll Back", isDestructive: true))
            return;

        if (await RunQuickAsync("Could not roll back", () => repository.DiscardAsync(entries, CancellationToken.None)))
            notifications.Notify(NotificationKind.Success, entries.Count == 1 ? $"Rolled back {Path.GetFileName(entries[0].Path)}" : $"Rolled back {entries.Count} files");
    }

    /// <summary>Rolls back a file of the open folder, or every changed file in a folder, after asking.</summary>
    public Task RollbackFileAsync(string fullPath, bool isDirectory = false) =>
        ChangesAt(fullPath, isDirectory) is { Count: > 0 } entries ? RollbackAsync(entries) : Task.CompletedTask;

    /// <summary>Adds paths to the repository's top <c>.gitignore</c>.</summary>
    public async Task AddToGitignoreAsync(IReadOnlyList<string> relativePaths)
    {
        ArgumentNullException.ThrowIfNull(relativePaths);
        if (Repository is not { } repository || relativePaths.Count == 0)
            return;

        var file = Path.Combine(repository.Root, ".gitignore");
        var existing = File.Exists(file) ? await File.ReadAllTextAsync(file) : "";
        var lines = relativePaths.Select(p => "/" + p).Where(line => !existing.Split('\n').Contains(line, StringComparer.Ordinal)).ToList();
        if (lines.Count == 0)
            return;

        var separator = existing.Length > 0 && !existing.EndsWith('\n') ? "\n" : "";
        await File.AppendAllTextAsync(file, separator + string.Join('\n', lines) + "\n");
        await repositories.RefreshAsync();
    }

    /// <summary>Shows the history of a file of the open folder, following renames.</summary>
    public void ShowFileHistory(string fullPath)
    {
        if (Repository is not { } repository)
            return;
        var relative = repository.RelativePath(fullPath);
        ShowHistory(new HistoryRequest(new LogQuery { Path = relative, Follow = File.Exists(fullPath) }, $"History of {relative}"));
    }

    /// <summary>Finds the status of a file of the open folder, or null when it has no changes.</summary>
    public StatusEntry? Find(string fullPath)
    {
        if (Repository is not { } repository || repositories.Status is not { } status)
            return null;
        var relative = repository.RelativePath(fullPath);
        return status.Entries.FirstOrDefault(e => e.Path == relative && e.Kind != StatusKind.Ignored);
    }

    /// <summary>Gets the changes at a path: the file's own, or every changed file in a folder; ignored files are left out.</summary>
    public IReadOnlyList<StatusEntry> ChangesAt(string fullPath, bool isDirectory) =>
        Repository is { } repository && repositories.Status is { } status && Contains(fullPath) ? ChangesAt(status, repository.RelativePath(fullPath), isDirectory) : [];

    /// <summary>Whether a path is in the working tree of the open folder's repository, outside its Git folder.</summary>
    public bool Contains(string fullPath) => Repository is { } repository && IsInWorkingTree(repository.RelativePath(fullPath));

    /// <summary>Gets the changes at a path relative to the repository, as <see cref="GitRepository.RelativePath"/> gives it.</summary>
    internal static IReadOnlyList<StatusEntry> ChangesAt(StatusSnapshot status, string relativePath, bool isDirectory) =>
        [.. status.Entries.Where(e => e.Kind != StatusKind.Ignored && (isDirectory ? e.Path.StartsWith(relativePath + "/", StringComparison.Ordinal) : e.Path == relativePath))];

    /// <summary>Whether a path relative to the repository is in its working tree: not the top folder, outside it, or in the Git folder.</summary>
    internal static bool IsInWorkingTree(string relativePath) =>
        relativePath is not ("." or ".." or ".git") && !Path.IsPathRooted(relativePath)
        && !relativePath.StartsWith("../", StringComparison.Ordinal) && !relativePath.StartsWith(".git/", StringComparison.Ordinal);

    public void Dispose() => autoFetch.Dispose();

    // Commits as a count for people: "1 commit", "3 commits".
    internal static string Commits(int count) => count == 1 ? "1 commit" : string.Create(CultureInfo.CurrentCulture, $"{count} commits");

    /// <summary>Says how long ago a date was, such as "5 minutes ago" or "Mar 3".</summary>
    internal static string Relative(DateTimeOffset date, DateTimeOffset? now = null)
    {
        var span = (now ?? DateTimeOffset.Now) - date;
        return span.TotalMinutes < 1 ? "just now"
            : span.TotalHours < 1 ? Plural((int)span.TotalMinutes, "minute") + " ago"
            : span.TotalDays < 1 ? Plural((int)span.TotalHours, "hour") + " ago"
            : span.TotalDays < 7 ? Plural((int)span.TotalDays, "day") + " ago"
            : date.ToLocalTime().Year == (now ?? DateTimeOffset.Now).ToLocalTime().Year ? date.ToLocalTime().ToString("MMM d", CultureInfo.CurrentCulture)
            : date.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.CurrentCulture);

        static string Plural(int value, string unit) => string.Create(CultureInfo.CurrentCulture, $"{value} {unit}{(value == 1 ? "" : "s")}");
    }

    private async Task CheckoutCoreAsync(string name, Func<bool, IProgress<string>, CancellationToken, Task> checkout)
    {
        if (Repository is not { } repository)
            return;

        var succeeded = await RunAsync($"Checking out {name}", (progress, token) => checkout(false, progress, token), handle: async exception =>
        {
            if (exception.Kind != GitErrorKind.LocalChangesWouldBeOverwritten)
                return false;

            var choice = await notifications.ChooseAsync("Local changes would be overwritten",
                $"{exception.Message}\n\nSmart Checkout stashes your changes, checks out {name} and brings them back. Force Checkout throws away the changes that are in the way.",
                ["Force Checkout", "Smart Checkout"]);
            if (choice == 1)
                await SmartCheckoutAsync(repository, name, checkout);
            else if (choice == 0)
                await RunAsync($"Checking out {name}", (progress, token) => checkout(true, progress, token));
            return true;
        });
        if (succeeded)
            notifications.Notify(NotificationKind.Success, $"Checked out {name}");
    }

    private async Task SmartCheckoutAsync(GitRepository repository, string name, Func<bool, IProgress<string>, CancellationToken, Task> checkout)
    {
        if (!await RunQuickAsync("Could not stash the local changes", () => repository.StashAsync($"Smart checkout of {name}", CancellationToken.None)))
            return;
        if (!await RunAsync($"Checking out {name}", (progress, token) => checkout(false, progress, token)))
        {
            await RunQuickAsync("Could not bring back the local changes", () => repository.UnstashAsync(0, CancellationToken.None));
            return;
        }

        try
        {
            await repository.UnstashAsync(0, CancellationToken.None);
            notifications.Notify(NotificationKind.Success, $"Checked out {name}", "Your local changes came along.");
        }
        catch (GitException exception)
        {
            notifications.Notify(NotificationKind.Warning, $"Checked out {name}, with conflicts",
                $"Your local changes conflict with {name}; resolve the conflicts. The changes stay in the stash until you drop it.\n\n{exception.Message}", "Show Changes", ShowCommitWindow);
        }
        finally
        {
            await repositories.RefreshAsync();
        }
    }

    private async Task<bool> RunAsync(string title, Func<IProgress<string>, CancellationToken, Task> work, Func<GitException, Task<bool>>? handle = null, bool quiet = false)
    {
        using var cancellation = new CancellationTokenSource();
        var task = quiet ? null : tasks.Start(title, cancellation.Cancel);
        SetBusy(+1);
        try
        {
            await work(new TaskProgress(task), cancellation.Token);
            return true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            notifications.Notify(NotificationKind.Info, $"{title}: cancelled");
            return false;
        }
        catch (GitException exception)
        {
            log?.AppendLine(exception.Message);
            if (handle is not null && await handle(exception))
                return false;
            if (!quiet)
                ShowError(title, exception);
            return false;
        }
        finally
        {
            task?.Dispose();
            SetBusy(-1);
            await RefreshQuietlyAsync();
        }
    }

    private async Task<bool> RunQuickAsync(string failure, Func<Task> work)
    {
        try
        {
            await work();
            return true;
        }
        catch (GitException exception)
        {
            notifications.Notify(NotificationKind.Error, failure, exception.Message);
            return false;
        }
        catch (IOException exception)
        {
            notifications.Notify(NotificationKind.Error, failure, exception.Message);
            return false;
        }
        finally
        {
            await RefreshQuietlyAsync();
        }
    }

    private async Task RefreshQuietlyAsync()
    {
        try
        {
            await repositories.RefreshAsync();
        }
        catch (GitException)
        {
        }
    }

    private void ShowError(string title, GitException exception)
    {
        var verb = title.Split(' ')[0];
        var headline = verb.EndsWith("ing", StringComparison.Ordinal) ? $"{title} failed" : title;
        if (exception.Kind == GitErrorKind.AuthenticationFailed)
            notifications.Notify(NotificationKind.Error, headline, $"The remote did not accept your credentials.\n\n{exception.Message}");
        else
            notifications.Notify(NotificationKind.Error, headline, exception.Message, "Show Git Output", () => log?.Show());
    }

    private Task<bool> PullFailedAsync(GitException exception)
    {
        if (exception.Kind == GitErrorKind.Conflict)
            return ConflictAsync(exception);
        return Task.FromResult(false);
    }

    private Task<bool> PushFailedAsync(GitException exception, string destination, string branch)
    {
        switch (exception.Kind)
        {
            case GitErrorKind.PushRejected:
                notifications.Notify(NotificationKind.Error, "Push rejected",
                    $"{destination} has commits that {branch} does not have. Update the project to bring them in, then push again.", "Update and Push", () => _ = PullThenPushAsync());
                return Task.FromResult(true);
            case GitErrorKind.StaleRemote:
                notifications.Notify(NotificationKind.Error, "Force push stopped",
                    $"{destination} moved since the last fetch, so forcing could lose someone's commits. Fetch, look at what changed, and try again.", "Fetch", () => _ = FetchAsync());
                return Task.FromResult(true);
            default:
                return Task.FromResult(false);
        }
    }

    private async Task PullThenPushAsync()
    {
        if (await PullAsync())
            await PushAsync();
    }

    private Task<bool> ConflictAsync(GitException exception)
    {
        if (exception.Kind != GitErrorKind.Conflict)
            return Task.FromResult(false);

        var count = repositories.Status?.Conflicts.Count() ?? 0;
        notifications.Notify(NotificationKind.Warning, count == 1 ? "1 file has conflicts" : count > 1 ? $"{count} files have conflicts" : "Conflicts to resolve",
            "Resolve them in the files, stage them, and commit; or abort from the Commit window.", "Show Changes", ShowCommitWindow);
        return Task.FromResult(true);
    }

    private async Task<bool> ConfirmAmendPushAsync() =>
        await notifications.ConfirmAsync("Force push the amended commit?",
            "The commit you amended was pushed already, so pushing it again replaces it on the remote.", "Force Push", isDestructive: true);

    private void NoRemote(string verb) =>
        notifications.Notify(NotificationKind.Warning, "No remote", $"The repository has no remote to {verb}. Add one with git remote add, such as origin with the address of the repository on its server.");

    private static async Task<string?> ValidateNewBranchAsync(GitRepository repository, IReadOnlyList<GitRef> refs, string name)
    {
        if (name.Length == 0)
            return "Type a name.";
        if (refs.Any(r => r.Kind == RefKind.LocalBranch && r.Name == name))
            return $"A branch named {name} exists already.";
        return await repository.IsValidBranchNameAsync(name, CancellationToken.None) ? null : $"{name} is not a valid branch name; names cannot contain spaces, ~, ^, :, ?, * or [, or two dots in a row.";
    }

    private static async Task<int> CountAsync(GitRepository repository, string range)
    {
        try
        {
            return int.Parse((await repository.ReadAsync(["rev-list", "--count", range], CancellationToken.None)).Trim(), CultureInfo.InvariantCulture);
        }
        catch (GitException)
        {
            return 0;
        }
    }

    private void ScheduleAutoFetch()
    {
        var minutes = settings.Get<int>(GitSettings.AutoFetchMinutes);
        var period = minutes > 0 ? TimeSpan.FromMinutes(minutes) : Timeout.InfiniteTimeSpan;
        autoFetch.Change(period, period);
    }

    private void SetBusy(int change)
    {
        var was = Volatile.Read(ref busy) > 0;
        var now = Interlocked.Add(ref busy, change) > 0;
        if (was != now)
            SyncingChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string Describe(OngoingOperation operation) => operation switch
    {
        OngoingOperation.CherryPick => "cherry-pick",
        _ => operation.ToString().ToLowerInvariant(),
    };

    private static string Quote(string argument) => argument.Contains(' ', StringComparison.Ordinal) ? $"\"{argument}\"" : argument;

    [GeneratedRegex(@"(\d{1,3})%")]
    private static partial Regex Percent();

    // Passes Git's progress lines to the background task, with the percentage as its fraction.
    private sealed class TaskProgress(IBackgroundTask? task) : IProgress<string>
    {
        public void Report(string value)
        {
            if (task is null)
                return;
            var match = Percent().Match(value);
            task.Report(value.Trim(), match.Success ? double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) / 100 : null);
        }
    }
}
