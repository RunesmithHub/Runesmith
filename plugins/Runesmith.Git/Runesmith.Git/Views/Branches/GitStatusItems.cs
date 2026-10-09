using Runesmith.Git.Commands;
using Runesmith.Git.Repositories;
using Runesmith.Sdk.Shell;

namespace Runesmith.Git.Views.Branches;

/// <summary>The status bar's Git items: the branch, which opens the branch popup, and the sync state, which fetches.</summary>
internal sealed class GitStatusItems : IDisposable
{
    private readonly GitOperations operations;
    private readonly RepositoryService repositories;
    private readonly IStatusBarItem branch;
    private readonly IStatusBarItem sync;

    public GitStatusItems(IStatusBar statusBar, GitOperations operations)
    {
        ArgumentNullException.ThrowIfNull(statusBar);
        this.operations = operations;
        repositories = operations.Repositories;
        branch = statusBar.AddItem("git.branch", StatusBarAlignment.Right, priority: -10);
        branch.Icon = GitIcons.Branch;
        branch.CommandId = GitCommands.Branches;
        sync = statusBar.AddItem("git.sync", StatusBarAlignment.Right, priority: -11);
        sync.CommandId = GitCommands.Fetch;
        repositories.Changed += OnChanged;
        operations.SyncingChanged += OnChanged;
        Update();
    }

    public void Dispose()
    {
        repositories.Changed -= OnChanged;
        operations.SyncingChanged -= OnChanged;
        branch.Dispose();
        sync.Dispose();
    }

    private void OnChanged(object? sender, EventArgs e) => Update();

    private void Update()
    {
        var info = repositories.Current;
        branch.IsVisible = info is not null;
        sync.IsVisible = info is { Remotes.Count: > 0 };
        if (info is null)
            return;

        branch.Text = info.Branch ?? (info.Head is { } head ? head[..Math.Min(8, head.Length)] : "HEAD");
        branch.ToolTip = BranchWidget.Describe(info) + "\nClick for branches";
        var counts = BranchPopup.Sync(info.Ahead, info.Behind);
        sync.Text = counts;
        sync.Icon = operations.IsSyncing ? "refresh" : counts.Length == 0 ? "check" : "refresh";
        sync.State = operations.IsSyncing ? StatusBarState.Busy : StatusBarState.Normal;
        sync.ToolTip = operations.IsSyncing ? "Syncing with the remote..."
            : info.Upstream is null ? "No upstream branch. Click to fetch every remote"
            : counts.Length == 0 ? $"Up to date with {info.Upstream}. Click to fetch"
            : $"{BranchWidget.Describe(info)}\nClick to fetch";
    }
}
