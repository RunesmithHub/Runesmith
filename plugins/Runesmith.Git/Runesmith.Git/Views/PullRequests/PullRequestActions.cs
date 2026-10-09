using System.Composition;
using HammerUI.Services;
using Runesmith.Git.Git;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.Tasks;
using Runesmith.Sdk.VersionControl;

namespace Runesmith.Git.Views.PullRequests;

/// <summary>Creating and checking out pull requests, for the commands and the tool window alike.</summary>
[Export]
[Shared]
[method: ImportingConstructor]
internal sealed class PullRequestActions(
    PullRequestService service,
    [Import(AllowDefault = true)] IDialogService? dialogs,
    [Import(AllowDefault = true)] ILauncher? launcher,
    [Import(AllowDefault = true)] INotificationService? notifications,
    [Import(AllowDefault = true)] IBackgroundTasks? tasks)
{
    /// <summary>Raised on the UI thread after a pull request was created, so lists can show it.</summary>
    public event EventHandler? Created;

    /// <summary>Shows the Create Pull Request dialog for the current branch, after signing in to the host when needed.</summary>
    public async Task CreateAsync()
    {
        if (service.Current is not { } context)
        {
            Notify(NotificationKind.Info, "No hosted repository", "Open a folder whose Git repository has a remote on a hosting service, such as GitHub, to create a pull request.");
            return;
        }

        if (context.Repository.Branch is null)
        {
            Notify(NotificationKind.Info, "No branch to propose", "HEAD is detached. Switch to a branch, or create one, first.");
            return;
        }

        if (dialogs is null)
        {
            Notify(NotificationKind.Error, "Pull requests need a newer Runesmith", "This version of Runesmith does not let plugins show dialogs.");
            return;
        }

        if (!await SignInAsync(context.Host))
            return;

        if (await dialogs.ShowAsync(new CreatePullRequestDialog(service, dialogs, context)) is PullRequest created)
        {
            Notify(NotificationKind.Success, FormattableString.Invariant($"Pull request #{created.Number} created"), created.Title, "Open in Browser", () => OpenUrl(created.WebUrl));
            Created?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Fetches a pull request into a local branch and switches to it, as a background task.</summary>
    public async Task CheckoutAsync(PullRequest pullRequest)
    {
        ArgumentNullException.ThrowIfNull(pullRequest);
        if (service.Current is not { } context)
            return;

        using var cancel = new CancellationTokenSource();
        using var task = tasks?.Start(FormattableString.Invariant($"Checking out pull request #{pullRequest.Number}"), cancel.Cancel);
        try
        {
            var branch = await service.CheckoutAsync(context, pullRequest, cancel.Token);
            Notify(NotificationKind.Success, $"Switched to {branch}", FormattableString.Invariant($"Pull request #{pullRequest.Number}: {pullRequest.Title}"));
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
        }
        catch (GitException exception)
        {
            Notify(NotificationKind.Error, FormattableString.Invariant($"Pull request #{pullRequest.Number} could not be checked out"), exception.Message);
        }
    }

    /// <summary>Asks the user to sign in to a host when it is signed out; returns whether it is signed in afterwards.</summary>
    public static async Task<bool> SignInAsync(IRepositoryHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (host.Account is not null)
            return true;

        try
        {
            return await host.SignInAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Opens a page in the browser, or shows its address when Runesmith cannot open one.</summary>
    public void OpenUrl(Uri url)
    {
        if (launcher is not null)
            launcher.OpenUrl(url);
        else
            notifications?.Notify(NotificationKind.Info, "Open this address in a browser", url.AbsoluteUri);
    }

    private void Notify(NotificationKind kind, string title, string? message, string? actionText = null, Action? action = null) =>
        notifications?.Notify(kind, title, message, actionText, action);
}
