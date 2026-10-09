using System.Composition;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using HammerUI;
using HammerUI.Controls;
using Runesmith.GitHub.GitHub;
using Runesmith.Plugins.Views;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Shell;
using static Runesmith.Plugins.Views.ViewHelpers;

namespace Runesmith.GitHub.Views.Account;

/// <summary>Puts the GitHub account in the toolbar's trailing slot.</summary>
[Export(typeof(IToolbarWidgetProvider))]
[method: ImportingConstructor]
internal sealed class AccountToolbarProvider(AccountActions actions, OrganizationAccess organizations, GitHubAvatarCache avatars,
    [Import(AllowDefault = true)] ICommandService? commands) : IToolbarWidgetProvider
{
    public ToolbarSlot Slot => ToolbarSlot.Trailing;

    public int Order => 100;

    public Control CreateWidget() => new AccountWidget(actions, organizations, avatars, commands);
}

/// <summary>The account button: the avatar while signed in, with a popup of the account and its actions, and a GitHub button that signs in
/// while signed out.</summary>
internal sealed class AccountWidget : Button
{
    private readonly AccountActions actions;
    private readonly OrganizationAccess organizations;
    private readonly AvatarCache avatars;
    private readonly ICommandService? commands;
    private readonly Flyout flyout = new() { Placement = PlacementMode.BottomEdgeAlignedRight };

    public AccountWidget(AccountActions actions, OrganizationAccess organizations, AvatarCache avatars, ICommandService? commands)
    {
        this.actions = actions;
        this.organizations = organizations;
        this.avatars = avatars;
        this.commands = commands;
        GitHubIcons.EnsureRegistered();
        Classes.AddRange(["toolbar", "icon"]);
        VerticalAlignment = VerticalAlignment.Center;
        IsVisible = false;
        Click += (_, _) => ShowAccount();
        actions.Account.Changed += (_, _) => Update();
        AttachedToVisualTree += (_, _) => _ = LoadAsync();
    }

    protected override Type StyleKeyOverride => typeof(Button);

    private async Task LoadAsync()
    {
        try
        {
            await actions.Account.LoadAsync();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
        }

        Update();
    }

    private void Update()
    {
        var account = actions.Account;
        IsVisible = account.IsLoaded;
        if (account.User is { } user)
        {
            Content = new Avatar(avatars, user.Login, user.AvatarUrl, 20);
            ToolTip.SetTip(this, $"GitHub: {user.Login}");
        }
        else
        {
            Content = new SymbolIcon { Data = GitHubIcons.Get(GitHubIcons.GitHub), Size = 16 };
            ToolTip.SetTip(this, "Sign in to GitHub");
        }
    }

    private void ShowAccount()
    {
        if (actions.Account.User is not { } user)
        {
            _ = actions.SignInAsync();
            return;
        }

        flyout.Content = Card(user);
        flyout.ShowAt(this);
    }

    private StackPanel Card(GitHubUser user)
    {
        var account = actions.Account;
        var name = Label(user.Name ?? user.Login, weight: FontWeight.SemiBold);
        var login = Label("@" + user.Login, "TextMutedBrush", 12);
        var how = Label(account.Kind switch
        {
            GitHubSignInKind.Cli => "Signed in with the GitHub CLI",
            GitHubSignInKind.PersonalToken => "Signed in with a personal access token",
            _ when account.App.Source == GitHubAppSource.Settings => "Signed in with your own GitHub App",
            _ => "Signed in with Runesmith's GitHub App",
        }, "TextMutedBrush", 11);
        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { name, login, how } };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,12,*"), Margin = new Thickness(10, 8, 16, 10), Children = { new Avatar(avatars, user.Login, user.AvatarUrl, 40), text } };
        Grid.SetColumn(text, 2);

        var card = new StackPanel { MinWidth = 260, Children = { header, Divider(new Thickness(0, 0, 0, 4)) } };
        if (account.Kind != GitHubSignInKind.Cli)
            card.Children.Add(Row(Icons.Shield, account.Kind == GitHubSignInKind.App ? "Manage Access..." : "Manage Tokens...", actions.ManageAccess));
        card.Children.Add(Row(GitHubIcons.Get(GitHubIcons.GitHub), "Open Profile on GitHub", () => actions.OpenUrl(new Uri($"https://github.com/{Uri.EscapeDataString(user.Login)}"))));
        if (commands is not null)
            card.Children.Add(Row(Icons.FolderPlus, "Clone Repository...", () => _ = commands.ExecuteAsync(CommandIds.CloneRepository)));
        if (account.Kind == GitHubSignInKind.App)
            card.Children.Add(Organizations());
        card.Children.Add(Divider(new Thickness(0, 4)));
        card.Children.Add(Row(GitHubIcons.Get(GitHubIcons.LogOut), "Sign Out", () => _ = actions.SignOutAsync()));
        return card;
    }

    // Shows the organizations from the last load at once, and loads them again so the popup stays current.
    private StackPanel Organizations()
    {
        var list = new StackPanel();
        var heading = Label("Organizations", "TextMutedBrush", 11, FontWeight.SemiBold);
        heading.Margin = new Thickness(10, 4, 10, 4);
        var section = new StackPanel { Children = { Divider(new Thickness(0, 4)), heading, list } };
        if (organizations.Installations.Current is { } current)
            Fill(current);
        _ = ReloadAsync();
        if (organizations.CanAdd)
            section.Children.Add(Row(GitHubIcons.Get(GitHubIcons.Building), "Add an organization...", () => _ = organizations.AddAsync()));
        return section;

        async Task ReloadAsync()
        {
            try
            {
                Fill(await organizations.Installations.LoadAsync(CancellationToken.None));
            }
            catch (GitHubException)
            {
            }
        }

        void Fill(InstallationsSnapshot snapshot)
        {
            list.Children.Clear();
            foreach (var row in snapshot.Rows(actions.Account.User?.Login).Where(r => r.IsOrganization))
                list.Children.Add(OrganizationRow(row));
        }
    }

    private Grid OrganizationRow(InstallationRow row)
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { Label(row.Login), Label(row.Scope, "TextMutedBrush", 11) } };
        var configure = Link("Configure", () =>
        {
            flyout.Hide();
            organizations.Configure(row);
        });
        configure.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(configure, $"Choose which repositories of {row.Login} Runesmith may use, on GitHub");
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,10,*,8,Auto"),
            Margin = new Thickness(10, 3, 6, 3),
            Children = { new Avatar(avatars, row.Login, row.AvatarUrl, 22, isOrganization: true), text, configure },
        };
        Grid.SetColumn(text, 2);
        Grid.SetColumn(configure, 4);
        return grid;
    }

    private Button Row(Geometry icon, string text, Action click)
    {
        var button = new Button
        {
            Classes = { "subtle" },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10, 6),
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { Icon(icon, 14), new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } } },
        };
        button.Click += (_, _) =>
        {
            flyout.Hide();
            click();
        };
        return button;
    }
}
