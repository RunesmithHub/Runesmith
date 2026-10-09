using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using HammerUI;
using HammerUI.Controls;
using static Runesmith.Plugins.Gitea.ViewHelpers;

namespace Runesmith.Plugins.Gitea;

/// <summary>The accounts at the top of the plugin's settings: each with its server and how it signed in, with Sign In, Sign Out and Remove,
/// and Add Account under them.</summary>
internal sealed class AccountsCard : Border
{
    /// <summary>The page that explains how to register an OAuth2 application on a server.</summary>
    public static readonly Uri RegistrationGuideUri = new("https://github.com/RunesmithHub/Runesmith/blob/main/docs/forgejo-gitea-oauth.md");

    private readonly GiteaHostProvider provider;
    private readonly GiteaPrompts prompts;
    private readonly AvatarCache avatars;
    private readonly List<GiteaHost> watched = [];

    public AccountsCard(GiteaHostProvider provider, GiteaPrompts prompts, AvatarCache avatars)
    {
        this.provider = provider;
        this.prompts = prompts;
        this.avatars = avatars;
        Classes.Add("card");
        AttachedToVisualTree += (_, _) =>
        {
            provider.Changed += OnChanged;
            Update();
            _ = LoadAsync();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            provider.Changed -= OnChanged;
            Unwatch();
        };
        Update();
    }

    protected override Type StyleKeyOverride => typeof(Border);

    private async Task LoadAsync()
    {
        try
        {
            await provider.LoadAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
        }
    }

    private void OnChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Update);

    private void Unwatch()
    {
        foreach (var host in watched)
            host.Changed -= OnChanged;
        watched.Clear();
    }

    private void Update()
    {
        Unwatch();
        var rows = new StackPanel { Spacing = 14 };
        foreach (var host in provider.Accounts)
        {
            if (VisualRoot is not null)
            {
                host.Changed += OnChanged;
                watched.Add(host);
            }

            rows.Children.Add(host.Login is null ? FirstAccount(host) : AccountRow(host));
        }

        var add = IconButton(Icons.Plus, "Add Account", "small");
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Click += (_, _) => _ = provider.AddAccountAsync(CancellationToken.None);
        var guide = Link("How to set up browser sign-in", () => provider.Context.OpenUrl(RegistrationGuideUri));
        var footer = new DockPanel { LastChildFill = false, Children = { add, guide } };
        DockPanel.SetDock(guide, Dock.Right);
        Child = new StackPanel { Spacing = 14, Children = { rows, Divider(new Thickness(0)), footer } };
    }

    private Grid AccountRow(GiteaHost host)
    {
        var login = host.Login!;
        var how = host.Kind switch
        {
            SignInKind.OAuth => "Signed in through the browser",
            SignInKind.Token => "Signed in with an access token",
            _ => "Signed out",
        };
        var title = host.Profile.FullName is { } fullName ? $"{fullName} (@{login})" : "@" + login;
        var text = new StackPanel
        {
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { Label(title, weight: FontWeight.SemiBold), Label(host.Name, "TextMutedBrush", 12), Label(how, host.IsSignedIn ? "TextMutedBrush" : "WarningBrush", 12) },
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        if (host.IsSignedIn)
        {
            var manage = IconButton(Icons.Shield, "Manage Tokens", "small");
            ToolTip.SetTip(manage, $"Open the applications settings on {host.Server.Key}");
            manage.Click += (_, _) => provider.Context.OpenUrl(host.Server.ApplicationsUri);
            var signOut = IconButton(GiteaIcons.Get(GiteaIcons.LogOut), "Sign Out", "small");
            signOut.Click += (_, _) => _ = prompts.SignOutAsync(host);
            buttons.Children.Add(manage);
            buttons.Children.Add(signOut);
        }
        else
        {
            var signIn = IconButton(GiteaIcons.Get(host.Icon), "Sign In", "accent", "small");
            signIn.Click += (_, _) => _ = host.SignInAsync(CancellationToken.None);
            var remove = new Button { Classes = { "icon", "subtle", "small" }, Content = new SymbolIcon { Data = Icons.Trash, Size = 14 }, VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(remove, "Remove the account");
            remove.Click += (_, _) => _ = prompts.RemoveAsync(host);
            buttons.Children.Add(signIn);
            buttons.Children.Add(remove);
        }

        var avatar = new Avatar(avatars, login, host.AccountAvatarUrl, 40) { Opacity = host.IsSignedIn ? 1 : 0.6 };
        return Row(avatar, text, buttons);
    }

    private static Grid FirstAccount(GiteaHost host)
    {
        var text = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Label("Not signed in", weight: FontWeight.SemiBold),
                Label($"Sign in to clone from {host.Server.Key}, push with your account and see pull requests. Add Account signs in to another server.",
                    "TextMutedBrush", 12, wrap: true),
            },
        };
        var signIn = IconButton(GiteaIcons.Get(host.Icon), $"Sign in to {host.Name}", "accent", "small");
        signIn.VerticalAlignment = VerticalAlignment.Center;
        signIn.Click += (_, _) => _ = host.SignInAsync(CancellationToken.None);
        var mark = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Child = Icon(GiteaIcons.Get(host.Icon), 20) };
        Brush(mark, BackgroundProperty, "SurfaceSunkenBrush");
        return Row(mark, text, signIn);
    }
}
