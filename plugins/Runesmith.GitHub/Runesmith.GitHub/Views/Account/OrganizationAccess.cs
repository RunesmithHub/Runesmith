using System.Composition;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Runesmith.GitHub.GitHub;
using Runesmith.Sdk.Shell;

namespace Runesmith.GitHub.Views.Account;

/// <summary>Adding organizations to the GitHub App: the dialog that explains it, GitHub's install page, and reloading the installations when
/// the user comes back to Runesmith, with a toast for each new one.</summary>
[Export]
[Shared]
internal sealed class OrganizationAccess
{
    private readonly AccountActions actions;
    private readonly InstallReturn installReturn;
    private Window? watched;

    [ImportingConstructor]
    public OrganizationAccess(AccountActions actions, GitHubInstallations installations)
    {
        this.actions = actions;
        Installations = installations;
        installReturn = new InstallReturn(installations);
    }

    public GitHubInstallations Installations { get; }

    /// <summary>Gets whether organizations can be added: signed in with a GitHub App whose name is known.</summary>
    public bool CanAdd => actions.Account.Kind == GitHubSignInKind.App && actions.Account.InstallUri is not null;

    /// <summary>Shows the Add an organization dialog; Continue on GitHub opens GitHub's install page and watches for the user's return.</summary>
    public async Task AddAsync()
    {
        if (!CanAdd || actions.Account.InstallUri is not { } install || actions.Dialogs is not { } dialogs)
            return;

        if (await dialogs.ShowAsync(new AddOrganizationDialog(actions.Account.App.App?.Name)) is not true)
            return;

        await installReturn.BeginAsync(CancellationToken.None);
        WatchReturn();
        actions.OpenUrl(install);
    }

    /// <summary>Opens an installation's settings page, where its repositories are chosen, and reloads when the user comes back.</summary>
    public void Configure(InstallationRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        _ = installReturn.BeginAsync(CancellationToken.None);
        WatchReturn();
        actions.OpenUrl(row.ConfigureUri);
    }

    private void WatchReturn()
    {
        if (watched is not null || (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is not { } window)
            return;

        watched = window;
        window.Activated += OnActivated;
    }

    private void OnActivated(object? sender, EventArgs e) => _ = OnReturnAsync();

    private async Task OnReturnAsync()
    {
        if (!installReturn.IsWaiting)
        {
            watched?.Activated -= OnActivated;
            watched = null;
            return;
        }

        try
        {
            var added = await installReturn.ReturnAsync(CancellationToken.None);
            if (InstallReturn.Announcement(added) is { } announcement)
                actions.Notifications?.Notify(NotificationKind.Success, announcement, "Its repositories are in the clone dialog now.");
        }
        catch (GitHubException)
        {
        }
    }
}
