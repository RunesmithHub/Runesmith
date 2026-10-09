using System.Composition;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using HammerUI;
using HammerUI.Controls;
using Runesmith.GitHub.GitHub;
using Runesmith.Plugins.Views;
using Runesmith.Sdk.Settings;
using static Runesmith.Plugins.Views.ViewHelpers;

namespace Runesmith.GitHub.Views.Account;

/// <summary>Puts the GitHub account, the GitHub App in use and the repository access at the top of the GitHub settings.</summary>
[Export(typeof(ISettingsSectionProvider))]
[method: ImportingConstructor]
internal sealed class GitHubSettingsSection(AccountActions actions, OrganizationAccess access, GitHubAvatarCache avatars,
    [Import(AllowDefault = true)] ISettingsService? settings) : ISettingsSectionProvider
{
    public string Category => GitHubSettings.Category;

    public Control CreateSection() => new StackPanel { Spacing = 12, Children = { new GitHubAccountCard(actions, avatars, settings), new RepositoryAccessCard(actions, access, avatars) } };
}

/// <summary>A settings card that follows the account: it rebuilds when someone signs in or out, and loads the session when it first shows.</summary>
internal abstract class AccountCard : Border
{
    protected AccountCard(AccountActions actions)
    {
        Actions = actions;
        GitHubIcons.EnsureRegistered();
        Classes.Add("card");
        AttachedToVisualTree += (_, _) =>
        {
            actions.Account.Changed += OnChanged;
            _ = LoadAsync();
        };
        DetachedFromVisualTree += (_, _) => actions.Account.Changed -= OnChanged;
    }

    protected override Type StyleKeyOverride => typeof(Border);

    protected AccountActions Actions { get; }

    protected GitHubAccount Account => Actions.Account;

    /// <summary>Builds the card's content for the account as it is now.</summary>
    protected abstract void Update();

    protected void OnChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Update);

    private async Task LoadAsync()
    {
        try
        {
            await Account.LoadAsync();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
        }

        Update();
    }

    protected static Border Centered(Control icon)
    {
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        return new Border { Width = 40, Child = icon, VerticalAlignment = VerticalAlignment.Center };
    }

    protected static Grid Row(Control leading, Control text, Control trailing)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,12,*,16,Auto"), Children = { leading, text, trailing } };
        Grid.SetColumn(text, 2);
        Grid.SetColumn(trailing, 4);
        return grid;
    }

    protected static StackPanel Text(string title, string hint) => new()
    {
        Spacing = 1,
        VerticalAlignment = VerticalAlignment.Center,
        Children = { Label(title, weight: FontWeight.Medium), Label(hint, "TextMutedBrush", 12, wrap: true) },
    };
}

/// <summary>The account with Manage Access and Sign Out, or the ways to sign in; then the GitHub App signing in uses, with your own app
/// under Advanced, or the short way to register one when there is none.</summary>
internal sealed class GitHubAccountCard : AccountCard
{
    /// <summary>The page that explains how to register a GitHub App.</summary>
    public static readonly Uri RegistrationGuideUri = new("https://github.com/RunesmithHub/Runesmith/blob/main/docs/github-app.md");

    private static readonly Uri NewAppUri = new("https://github.com/settings/apps/new");

    private readonly AvatarCache avatars;
    private bool showAdvanced;

    public GitHubAccountCard(AccountActions actions, AvatarCache avatars, ISettingsService? settings)
        : base(actions)
    {
        this.avatars = avatars;
        showAdvanced = Account.App.Source == GitHubAppSource.Settings;
        AttachedToVisualTree += (_, _) => settings?.Changed += OnSettingChanged;
        DetachedFromVisualTree += (_, _) => settings?.Changed -= OnSettingChanged;
        Update();
    }

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.Key is GitHubSettings.ClientId or GitHubSettings.AppSlug)
            OnChanged(sender, e);
    }

    protected override void Update()
    {
        var content = new StackPanel { Spacing = 14, Children = { Account.User is { } user ? SignedIn(user) : SignedOut(), Divider(new Thickness(0)), AppState() } };
        if (Account.App.Source != GitHubAppSource.None)
            content.Children.Add(Advanced());
        Child = content;
    }

    private Grid SignedIn(GitHubUser user)
    {
        var how = Account.Kind switch
        {
            GitHubSignInKind.Cli => "Signed in with the GitHub CLI",
            GitHubSignInKind.PersonalToken => "Signed in with a personal access token",
            _ => "Signed in with the GitHub App",
        };
        var text = new StackPanel
        {
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { Label(user.Name ?? user.Login, weight: FontWeight.SemiBold), Label("@" + user.Login, "TextMutedBrush", 12), Label(how, "TextMutedBrush", 12) },
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        if (Account.Kind != GitHubSignInKind.Cli)
        {
            var manage = IconButton(Icons.Shield, Account.Kind == GitHubSignInKind.App ? "Manage Access" : "Manage Tokens", "small");
            manage.Click += (_, _) => Actions.ManageAccess();
            buttons.Children.Add(manage);
        }

        var signOut = IconButton(GitHubIcons.Get(GitHubIcons.LogOut), "Sign Out", "small");
        signOut.Click += (_, _) => _ = Actions.SignOutAsync();
        buttons.Children.Add(signOut);
        return Row(new Avatar(avatars, user.Login, user.AvatarUrl, 40), text, buttons);
    }

    private StackPanel SignedOut()
    {
        var text = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Label("Not signed in", weight: FontWeight.SemiBold),
                Label("Sign in to clone from your account and organizations, push to github.com and see pull requests.", "TextMutedBrush", 12, wrap: true),
            },
        };
        var signIn = IconButton(GitHubIcons.Get(GitHubIcons.GitHub), "Sign in to GitHub", "accent", "small");
        signIn.Click += (_, _) => _ = Actions.SignInAsync();

        var hasCli = GitHubCli.IsInstalled();
        var cli = Link("Use the GitHub CLI's sign-in", () => _ = Actions.SignInAsync(SignInStart.Cli));
        cli.IsEnabled = hasCli;
        if (!hasCli)
            ToolTip.SetTip(cli, "Install the GitHub CLI, gh, and run gh auth login to use this.");
        var token = Link("Sign in with a token", () => _ = Actions.SignInAsync(SignInStart.Token));
        var others = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(52, -6, 0, 0),
            Children = { Label("Or:", "TextMutedBrush", 12), cli, token },
        };
        others.Children[0].VerticalAlignment = VerticalAlignment.Center;

        var mark = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Child = Icon(GitHubIcons.Get(GitHubIcons.GitHub), 20) };
        Brush(mark, BackgroundProperty, "SurfaceSunkenBrush");
        return new StackPanel { Spacing = 10, Children = { Row(mark, text, signIn), others } };
    }

    private Grid AppState()
    {
        var app = Account.App;
        if (app.Source == GitHubAppSource.Shipped)
        {
            return Row(Centered(Icon(Icons.CheckCircle, 16, "SuccessBrush")), Text($"Signing in with {app.App?.Name ?? "Runesmith's GitHub App"}",
                "Runesmith's own GitHub App, so there is nothing to register. You choose which repositories and organizations it may use."), new Panel());
        }

        if (app.Source == GitHubAppSource.Settings)
        {
            return Row(Centered(Icon(Icons.CheckCircle, 16, "SuccessBrush")), Text("Signing in with your own GitHub App",
                $"The app with client ID {app.ClientId}, from Client ID below. Sign in again after changing it."), new Panel());
        }

        var setup = Text("No GitHub App set up",
            "Signing in through the browser needs a GitHub App with the device flow turned on. Register one, then put its client ID in Client ID below.");
        var register = IconButton(Icons.ExternalLink, "Register a GitHub App", "small");
        register.Click += (_, _) => Actions.OpenUrl(NewAppUri);
        var guide = Link("How to register it", () => Actions.OpenUrl(RegistrationGuideUri));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { guide, register } };
        return Row(Centered(Icon(Icons.Info, 16, "TextSecondaryBrush")), setup, buttons);
    }

    private StackPanel Advanced()
    {
        var toggle = new Button
        {
            Classes = { "subtle", "small" },
            Padding = new Thickness(4, 2),
            Margin = new Thickness(48, -6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children = { Icon(showAdvanced ? Icons.ChevronDown : Icons.ChevronRight, 12, "TextMutedBrush"), Label("Advanced", "TextSecondaryBrush", 12) },
            },
        };
        toggle.Click += (_, _) =>
        {
            showAdvanced = !showAdvanced;
            Update();
        };
        var panel = new StackPanel { Spacing = 8, Children = { toggle } };
        if (!showAdvanced)
            return panel;

        var ownApp = Account.App.Source == GitHubAppSource.Settings;
        var text = Text("Use your own GitHub App", ownApp
            ? "Runesmith signs in with the app in Client ID and App name in URLs below instead of its own."
            : "Forks and companies can sign in with an app of their own: register it, then put its client ID in Client ID below and its name in App name in URLs.");
        text.Margin = new Thickness(52, 0, 0, 0);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(48, 0, 0, 0) };
        if (ownApp && Account.ShippedApp is not null)
        {
            var back = IconButton(Icons.Undo, "Back to Runesmith's app", "small");
            back.Click += (_, _) =>
            {
                Account.UseShippedApp();
                Update();
            };
            actions.Children.Add(back);
        }

        actions.Children.Add(Link("How to register one", () => Actions.OpenUrl(RegistrationGuideUri)));
        panel.Children.Add(text);
        panel.Children.Add(actions);
        return panel;
    }
}

/// <summary>The accounts and organizations the GitHub App is installed on, with what each gives and Configure, and Add an organization; for
/// a token or the GitHub CLI, where organization repositories come from instead.</summary>
internal sealed class RepositoryAccessCard : AccountCard
{
    private readonly OrganizationAccess access;
    private readonly AvatarCache avatars;
    private int generation;

    public RepositoryAccessCard(AccountActions actions, OrganizationAccess access, AvatarCache avatars)
        : base(actions)
    {
        this.access = access;
        this.avatars = avatars;
        AttachedToVisualTree += (_, _) => access.Installations.Changed += OnChanged;
        DetachedFromVisualTree += (_, _) => access.Installations.Changed -= OnChanged;
        Update();
    }

    protected override void Update()
    {
        IsVisible = Account.IsSignedIn;
        if (!IsVisible)
            return;

        var content = new StackPanel { Spacing = 12, Children = { Label("Repository access", weight: FontWeight.SemiBold) } };
        Child = content;
        if (Account.Kind != GitHubSignInKind.App)
        {
            content.Children.Add(Label(Account.Kind == GitHubSignInKind.Cli
                ? "Runesmith uses the repositories the GitHub CLI's sign-in can reach, including those of your organizations. Organization access comes from that sign-in, not from Runesmith."
                : "Runesmith uses the repositories the token can reach, including those of organizations the token was given access to. Change the token on GitHub to reach more.",
                "TextMutedBrush", 12, wrap: true));
            return;
        }

        content.Children.Add(Label("The accounts and organizations the GitHub App is installed on. Runesmith may use only the repositories they give it.", "TextMutedBrush", 12, wrap: true));
        var list = new StackPanel { Spacing = 10 };
        content.Children.Add(list);
        if (access.Installations.Current is { } current)
            Fill(list, current);
        else
            _ = LoadAsync(list);

        if (access.CanAdd)
        {
            var add = IconButton(Icons.Plus, "Add an organization...", "small");
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Click += (_, _) => _ = access.AddAsync();
            content.Children.Add(add);
        }
    }

    private async Task LoadAsync(StackPanel list)
    {
        var current = ++generation;
        list.Children.Clear();
        list.Children.Add(Label("Loading...", "TextMutedBrush", 12));
        try
        {
            var snapshot = await access.Installations.LoadAsync(CancellationToken.None);
            if (current == generation)
                Fill(list, snapshot);
        }
        catch (GitHubException exception)
        {
            if (current != generation)
                return;

            var retry = Link("Try again", () => _ = LoadAsync(list));
            retry.HorizontalAlignment = HorizontalAlignment.Left;
            list.Children.Clear();
            list.Children.Add(Label(exception.Message, "DangerBrush", 12, wrap: true));
            list.Children.Add(retry);
        }
    }

    private void Fill(StackPanel list, InstallationsSnapshot snapshot)
    {
        list.Children.Clear();
        var rows = snapshot.Rows(Account.User?.Login);
        if (rows.Count == 0)
        {
            list.Children.Add(Label("The app is not installed anywhere yet. Add your account or an organization to choose repositories.", "TextSecondaryBrush", 12, wrap: true));
            return;
        }

        foreach (var row in rows)
        {
            var kind = string.Equals(row.Login, Account.User?.Login, StringComparison.OrdinalIgnoreCase) ? "Your account" : row.IsOrganization ? "Organization" : "Account";
            var details = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { Label(kind, "TextMutedBrush", 12), Label("·", "TextDisabledBrush", 12), Label(row.Scope, "TextMutedBrush", 12) } };
            var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { Label(row.Login, weight: FontWeight.Medium), details } };
            var configure = IconButton(Icons.Settings, "Configure", "small");
            configure.VerticalAlignment = VerticalAlignment.Center;
            ToolTip.SetTip(configure, $"Choose which repositories of {row.Login} Runesmith may use, on GitHub");
            configure.Click += (_, _) => access.Configure(row);
            var avatar = new Avatar(avatars, row.Login, row.AvatarUrl, 32, row.IsOrganization) { Margin = new Thickness(4, 0) };
            list.Children.Add(Row(avatar, text, configure));
        }
    }
}
