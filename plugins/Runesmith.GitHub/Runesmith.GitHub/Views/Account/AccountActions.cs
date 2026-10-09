using System.Composition;
using HammerUI.Services;
using Runesmith.GitHub.GitHub;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Shell;

namespace Runesmith.GitHub.Views.Account;

/// <summary>What the account's buttons and commands do: sign in with the dialog, sign out, manage access, open links.</summary>
[Export]
[Shared]
[method: ImportingConstructor]
internal sealed class AccountActions(
    GitHubAccount account,
    [Import(AllowDefault = true)] IDialogService? dialogs,
    [Import(AllowDefault = true)] ILauncher? launcher,
    [Import(AllowDefault = true)] INotificationService? notifications,
    [Import(AllowDefault = true)] Lazy<ICommandService>? commands)
{
    public GitHubAccount Account => account;

    /// <summary>Gets the service that shows dialogs, or null when this Runesmith does not offer one to plugins.</summary>
    public IDialogService? Dialogs => dialogs;

    public INotificationService? Notifications => notifications;

    /// <summary>Shows the sign-in dialog, at the ways to sign in or at one of them; returns whether someone is signed in afterwards.</summary>
    public async Task<bool> SignInAsync(SignInStart start = SignInStart.Choices)
    {
        if (dialogs is null)
        {
            notifications?.Notify(NotificationKind.Error, "Signing in needs a newer Runesmith", "This version of Runesmith does not let plugins show dialogs.");
            return false;
        }

        if (await dialogs.ShowAsync(new SignInDialog(account, OpenUrl, OpenSettings, start)) is not GitHubUser user)
            return account.IsSignedIn;

        var isApp = account.Kind == GitHubSignInKind.App;
        notifications?.Notify(NotificationKind.Success, $"Signed in to GitHub as {user.Login}",
            isApp ? "Choose the repositories and organizations Runesmith may use with Manage Access." : null,
            isApp ? "Manage Access" : null, isApp ? ManageAccess : null);
        return true;
    }

    /// <summary>Makes sure someone is signed in, showing the sign-in dialog when no one is.</summary>
    public async Task<bool> EnsureSignedInAsync()
    {
        await account.LoadAsync();
        return account.IsSignedIn || await SignInAsync();
    }

    /// <summary>Asks, then signs out and deletes the tokens.</summary>
    public async Task SignOutAsync()
    {
        if (account.User is not { } user)
            return;

        var confirmed = dialogs is null || await dialogs.ConfirmAsync("Sign out of GitHub?",
            $"Runesmith forgets the sign-in of {user.Login}. Cloning and pushing to github.com then use your own Git setup.", "Sign Out", isDestructive: true);
        if (confirmed)
            await account.SignOutAsync();
    }

    /// <summary>Opens the page where the user installs the GitHub App and picks repositories, or the token settings for a pasted token.</summary>
    public void ManageAccess() => OpenUrl(account.Kind switch
    {
        GitHubSignInKind.PersonalToken => new Uri("https://github.com/settings/personal-access-tokens"),
        _ => account.ManageAccessUri,
    });

    public void OpenUrl(Uri url)
    {
        if (launcher is not null)
            launcher.OpenUrl(url);
        else
            notifications?.Notify(NotificationKind.Info, "Open this address in a browser", url.AbsoluteUri);
    }

    private void OpenSettings()
    {
        if (commands is not null)
            _ = commands.Value.ExecuteAsync(CommandIds.Settings, GitHubSettings.Category);
    }
}
