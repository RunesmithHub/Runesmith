using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using HammerUI;
using HammerUI.Controls;
using static Runesmith.Plugins.Gitea.ViewHelpers;

namespace Runesmith.Plugins.Gitea;

/// <summary>The sign-in dialog of one account: OAuth2 in the browser where an application is set up for the server, waiting for the browser's
/// answer, and an access token instead. It closes with the <see cref="GiteaUser"/> that signed in.</summary>
internal sealed class SignInDialog : Dialog, IDisposable
{
    /// <summary>The scopes an access token needs, as the server's token form names them.</summary>
    public static readonly (string Scope, string Access, string Reason)[] TokenScopes =
    [
        ("repository", "Read and write", "clone, push, and list and create pull requests"),
        ("user", "Read", "your login and avatar, and your repositories"),
        ("organization", "Read", "the organizations whose repositories you clone"),
    ];

    private readonly GiteaHost host;
    private readonly ContentControl page = new();
    private readonly TextBlock error = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 300, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
    private readonly StackPanel buttons = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly CancellationTokenSource closing = new();
    private CancellationTokenSource? step;
    private bool isDisposed;

    public SignInDialog(GiteaHost host)
    {
        this.host = host;
        Header = $"Sign in to {host.Name}";
        Width = 520;
        ShowCloseButton = true;
        Content = page;
        Brush(error, TextBlock.ForegroundProperty, "DangerBrush");
        var footer = new DockPanel { LastChildFill = false, Children = { error, buttons } };
        DockPanel.SetDock(buttons, Dock.Right);
        Footer = footer;
        if (host.ClientId is not null)
            _ = BrowserAsync();
        else
            ShowToken();
        DetachedFromVisualTree += (_, _) => Dispose();
    }

    protected override Type StyleKeyOverride => typeof(Dialog);

    private string Host => host.Server.Key;

    public void Dispose()
    {
        if (isDisposed)
            return;

        isDisposed = true;
        CancelStep();
        closing.Cancel();
        closing.Dispose();
    }

    private async Task BrowserAsync()
    {
        var token = BeginStep();
        ShowError(null);
        if (host.ClientId is not { } clientId)
        {
            ShowToken();
            return;
        }

        LoopbackAuthorization authorization;
        try
        {
            authorization = host.OAuth.Start(clientId);
        }
        catch (OAuthException exception)
        {
            ShowToken();
            ShowError(exception.Message);
            return;
        }

        using (authorization)
        {
            ShowWaiting(authorization);
            host.Context.OpenUrl(authorization.AuthorizeUri);
            try
            {
                var code = await authorization.WaitForCodeAsync(token);
                ShowBusy("Signing in...");
                Close(await host.SignInWithCodeAsync(authorization, code, token));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception exception) when (exception is OAuthException or GiteaException)
            {
                ShowFailedBrowser(exception.Message);
            }
        }
    }

    private void ShowWaiting(LoopbackAuthorization authorization)
    {
        Description = $"Approve Runesmith on {Host} in your browser. Runesmith waits here for the answer and signs you in.";
        var mark = Icon(GiteaIcons.Get(host.Icon), 28, "AccentBrush");
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        var waiting = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { Spinner(13), Label("Waiting for the browser...", "TextSecondaryBrush") },
        };
        var again = Link("Open the page again", () => host.Context.OpenUrl(authorization.AuthorizeUri));
        again.HorizontalAlignment = HorizontalAlignment.Center;
        var hint = Label($"The browser asks you to sign in to {Host} first if you are not. Nothing you type there reaches Runesmith.", "TextMutedBrush", 12, wrap: true);
        hint.TextAlignment = TextAlignment.Center;
        page.Content = new StackPanel { Spacing = 12, Margin = new Thickness(0, 12, 0, 4), Children = { mark, waiting, again, hint } };

        var useToken = IconButton(GiteaIcons.Get(GiteaIcons.KeyRound), "Use a token instead");
        useToken.Click += (_, _) => ShowToken();
        SetButtons(useToken, CancelButton());
    }

    private void ShowFailedBrowser(string message)
    {
        CancelStep();
        Description = $"Signing in through the browser did not work. Try again, or use an access token from {Host}.";
        page.Content = new Panel { Height = 8 };
        ShowError(message);
        var again = new Button { Content = "Try Again", Classes = { "accent" }, MinWidth = 96, IsDefault = true };
        again.Click += (_, _) => _ = BrowserAsync();
        var useToken = IconButton(GiteaIcons.Get(GiteaIcons.KeyRound), "Use a token instead");
        useToken.Click += (_, _) => ShowToken();
        SetButtons(useToken, again);
    }

    private void ShowToken()
    {
        CancelStep();
        ShowError(null);
        Description = $"Paste an access token from your settings on {Host}. Runesmith checks it with the server before keeping it.";
        var box = new TextBox { PasswordChar = '•', PlaceholderText = "Access token", Classes = { "mono" } };
        var scopes = new StackPanel { Spacing = 4 };
        scopes.Children.Add(Label("Give the token these permissions:", "TextSecondaryBrush", 12));
        foreach (var (scope, access, reason) in TokenScopes)
        {
            var line = Label(null, "TextMutedBrush", 12, wrap: true);
            line.Inlines = [new Avalonia.Controls.Documents.Run(scope) { FontWeight = FontWeight.SemiBold }, new Avalonia.Controls.Documents.Run($": {access}, for {reason}")];
            line.Margin = new Thickness(10, 0, 0, 0);
            scopes.Children.Add(line);
        }

        var create = Link($"Create a token on {Host}", () => host.Context.OpenUrl(host.Server.ApplicationsUri));
        create.HorizontalAlignment = HorizontalAlignment.Left;
        create.Margin = new Thickness(-4, 0, 0, 0);
        var content = new StackPanel { Spacing = 10, Margin = new Thickness(0, 4, 0, 0), Children = { box, scopes, create } };
        if (host.ClientId is null)
            content.Children.Add(BrowserSetupNote());
        content.Children.Add(StoreNote(host.Context));
        page.Content = content;

        var signIn = new Button { Content = "Sign In", Classes = { "accent" }, MinWidth = 96, IsDefault = true, IsEnabled = false };
        box.TextChanged += (_, _) => signIn.IsEnabled = !string.IsNullOrWhiteSpace(box.Text);
        signIn.Click += (_, _) => _ = UseTokenAsync(box.Text ?? "", signIn);
        if (host.ClientId is not null)
        {
            var browser = new Button { Content = "Use the Browser", MinWidth = 84 };
            browser.Click += (_, _) => _ = BrowserAsync();
            SetButtons(browser, CancelButton(), signIn);
        }
        else
        {
            SetButtons(CancelButton(), signIn);
        }

        box.AttachedToVisualTree += (_, _) => box.Focus();
    }

    private Border BrowserSetupNote()
    {
        var text = Label($"To sign in through the browser instead, register an OAuth2 application for Runesmith on {Host} once.", "TextSecondaryBrush", 12, wrap: true);
        var setUp = Link("Set up browser sign-in", ShowSetup);
        setUp.HorizontalAlignment = HorizontalAlignment.Left;
        setUp.Margin = new Thickness(-4, 0, 0, 0);
        return Card(new StackPanel { Spacing = 4, Children = { text, setUp } });
    }

    private void ShowSetup()
    {
        CancelStep();
        ShowError(null);
        Description = $"Register Runesmith as an OAuth2 application on {Host}, once. Signing in then takes a click in the browser.";
        var steps = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Step(1, ("Open ", false), ("Settings › Applications", true), ($" on {Host}, under ", false), ("Manage OAuth2 applications", true), (".", false)),
                Step(2, ("Name it ", false), ("Runesmith", true), (", set the redirect URI to ", false), ("http://127.0.0.1/", true), (" and clear ", false),
                    ("Confidential client", true), (".", false)),
                Step(3, ("Select ", false), ("Create application", true), (" and paste its ", false), ("Client ID", true), (" here. Runesmith needs no client secret.", false)),
            },
        };
        var open = IconButton(Icons.ExternalLink, $"Open {Host} Applications", "small");
        open.HorizontalAlignment = HorizontalAlignment.Left;
        open.Click += (_, _) => host.Context.OpenUrl(host.Server.ApplicationsUri);
        var box = new TextBox { PlaceholderText = "Client ID", Classes = { "mono" } };
        page.Content = new StackPanel { Spacing = 12, Margin = new Thickness(0, 4, 0, 0), Children = { Card(steps), open, box } };

        var save = new Button { Content = "Save and Sign In", Classes = { "accent" }, MinWidth = 96, IsDefault = true, IsEnabled = false };
        box.TextChanged += (_, _) => save.IsEnabled = !string.IsNullOrWhiteSpace(box.Text);
        save.Click += (_, _) =>
        {
            host.Context.SetClientId(host.Server, box.Text!.Trim());
            _ = BrowserAsync();
        };
        var back = new Button { Content = "Back", MinWidth = 84 };
        back.Click += (_, _) => ShowToken();
        SetButtons(back, save);
        box.AttachedToVisualTree += (_, _) => box.Focus();
    }

    private async Task UseTokenAsync(string text, Button signIn)
    {
        var token = BeginStep();
        signIn.IsEnabled = false;
        ShowError(null);
        try
        {
            Close(await host.SignInWithTokenAsync(text, token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (GiteaException exception)
        {
            ShowError(exception.Failure switch
            {
                GiteaFailure.Unauthorized => $"{Host} does not accept this token. Check that it is complete and not deleted.",
                GiteaFailure.Forbidden => $"The token may not read your account. Give it the user scope with Read access.",
                _ => exception.Message,
            });
            signIn.IsEnabled = true;
        }
    }

    private void ShowBusy(string text)
    {
        page.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 28),
            Children = { Spinner(), Label(text, "TextSecondaryBrush") },
        };
        SetButtons(CancelButton());
    }

    private CancellationToken BeginStep()
    {
        CancelStep();
        step = CancellationTokenSource.CreateLinkedTokenSource(closing.Token);
        return step.Token;
    }

    private void CancelStep()
    {
        step?.Cancel();
        step?.Dispose();
        step = null;
    }

    private void ShowError(string? message)
    {
        error.Text = message;
        error.IsVisible = message is not null;
    }

    private Button CancelButton()
    {
        var cancel = new Button { Content = "Cancel", MinWidth = 84, IsCancel = true };
        cancel.Click += (_, _) => Close();
        return cancel;
    }

    private void SetButtons(params Button[] items)
    {
        buttons.Children.Clear();
        buttons.Children.AddRange(items);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }
}
