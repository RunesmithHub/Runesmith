using System.Composition;
using Runesmith.Git.Views.Branches;
using Runesmith.Sdk.Plugins;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.ToolWindows;

namespace Runesmith.Git.Commands;

/// <summary>The Git half's startup work: the status bar items, and the Commit and Git windows, which show only inside a repository.</summary>
[Export(typeof(IPlugin))]
[method: ImportingConstructor]
internal sealed class GitPlugin(GitOperations operations, [Import(AllowDefault = true)] IStatusBar? statusBar, [Import(AllowDefault = true)] IToolWindowManager? toolWindows)
    : IPlugin
{
    private GitStatusItems? items;

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        GitIcons.Register();
        if (statusBar is not null)
            items = new GitStatusItems(statusBar, operations);
        operations.Repositories.Changed += (_, _) => UpdateWindows();
        // Until the folder's repository is found, the windows keep their place rather than blink.
        if (operations.Repositories.Opened.IsCompleted)
            UpdateWindows();
        return Task.CompletedTask;
    }

    public void Dispose() => items?.Dispose();

    private void UpdateWindows()
    {
        var inRepository = operations.Repositories.Current is not null;
        toolWindows?.SetAvailable(GitOperations.CommitWindowId, inRepository);
        toolWindows?.SetAvailable(GitOperations.LogWindowId, inRepository);
    }
}
