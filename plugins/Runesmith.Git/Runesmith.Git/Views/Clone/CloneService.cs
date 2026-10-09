using System.Composition;
using Avalonia.Threading;
using Runesmith.Git.Clone;
using Runesmith.Git.Git;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.Tasks;
using Runesmith.Sdk.Workspace;

namespace Runesmith.Git.Views.Clone;

/// <summary>A clone in progress. It goes on when the dialog that started it closes, shown as a background task, and opens the folder when it
/// is done.</summary>
internal sealed class CloneOperation : IDisposable
{
    private readonly CancellationTokenSource cancel = new();

    /// <summary>Raised on the UI thread for each line of Git's progress.</summary>
    public event Action<CloneStep>? Progress;

    /// <summary>Gets the task that ends when the folder is cloned and opened; it fails with Git's message.</summary>
    public Task Completion { get; internal set; } = Task.CompletedTask;

    /// <summary>Gets or sets whether a dialog shows the clone, which then reports a failure itself.</summary>
    public bool IsWatched { get; set; } = true;

    public CancellationToken Token => cancel.Token;

    public void Cancel()
    {
        try
        {
            cancel.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose() => cancel.Dispose();

    internal void Report(CloneStep step) => Dispatcher.UIThread.Post(() => Progress?.Invoke(step));
}

/// <summary>Clones repositories, with the hosting plugins' credentials for their servers, and opens them.</summary>
[Export]
[Shared]
[method: ImportingConstructor]
internal sealed class CloneService(
    GitCredentialResolver credentials,
    [Import(AllowDefault = true)] IWorkspace? workspace,
    [Import(AllowDefault = true)] IBackgroundTasks? tasks,
    [Import(AllowDefault = true)] INotificationService? notifications)
{
    /// <summary>Starts cloning <paramref name="url"/> into <paramref name="target"/>.</summary>
    public CloneOperation Start(string url, string target)
    {
        var operation = new CloneOperation();
        operation.Completion = RunAsync(operation, url, target);
        return operation;
    }

    private async Task RunAsync(CloneOperation operation, string url, string target)
    {
        var name = Path.GetFileName(target);
        using var task = tasks?.Start($"Cloning {name}", operation.Cancel);
        try
        {
            var credentialsForUrl = await credentials.GetAsync(url, operation.Token).ConfigureAwait(false);
            var progress = new Progress(step =>
            {
                task?.Report(step.Text, step.Fraction);
                operation.Report(step);
            });
            await RepositoryCloner.CloneAsync(url, target, credentialsForUrl, progress, operation.Token).ConfigureAwait(false);
        }
        catch (GitException exception) when (!operation.IsWatched)
        {
            notifications?.Notify(NotificationKind.Error, $"{name} could not be cloned", exception.Message);
            throw;
        }

        if (workspace is not null)
            await Dispatcher.UIThread.InvokeAsync(() => workspace.OpenAsync(target));
    }

    private sealed class Progress(Action<CloneStep> report) : IProgress<CloneStep>
    {
        public void Report(CloneStep value) => report(value);
    }
}
