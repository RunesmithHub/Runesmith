using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using HammerUI;
using HammerUI.Controls;
using Runesmith.GitHub.GitHub;
using static Runesmith.Plugins.Views.ViewHelpers;

namespace Runesmith.GitHub.Views.Account;

/// <summary>Where the sign-in dialog starts.</summary>
internal enum SignInStart
{
    /// <summary>The ways to sign in, with the GitHub App first.</summary>
    Choices,

    /// <summary>Signing in with the GitHub CLI's account right away.</summary>
    Cli,

    /// <summary>The personal access token field.</summary>
    Token,
}

/// <summary>The Sign in to GitHub dialog: the device flow with its code, and the GitHub CLI's sign-in or a personal access token instead.
/// While no GitHub App is set up it explains how to register one. It closes with the <see cref="GitHubUser"/> that signed in.</summary>
internal sealed class SignInDialog : Dialog, IDisposable
{
    /// <summary>The permissions the GitHub App asks for, as the registration steps list them.</summary>
    public const string Permissions =
        "Contents (read and write), Metadata (read), Pull requests (read and write), Workflows (read and write), Checks (read), Commit statuses (read) and Email addresses (read)";

    private static readonly Uri NewAppUri = new("https://github.com/settings/apps/new");
    private static readonly Uri FineGrainedTokenUri = new("https://github.com/settings/personal-access-tokens/new");
    private static readonly Uri ClassicTokenUri = new("https://github.com/settings/tokens/new?scopes=repo,workflow,read:org&description=Runesmith");

    private readonly GitHubAccount account;
    private readonly Action<Uri> openUrl;
    private readonly Action? openSettings;
    private readonly ContentControl page = new();
    private readonly TextBlock error = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 300, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
    private readonly StackPanel buttons = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly CancellationTokenSource closing = new();
    private CancellationTokenSource? step;
    private bool isDisposed;

    public SignInDialog(GitHubAccount account, Action<Uri> openUrl, Action? openSettings, SignInStart start = SignInStart.Choices)
    {
        this.account = account;
        this.openUrl = openUrl;
        this.openSettings = openSettings;
        GitHubIcons.EnsureRegistered();
        Header = "Sign in to GitHub";
        Width = 500;
        ShowCloseButton = true;
        Content = page;
        Brush(error, TextBlock.ForegroundProperty, "DangerBrush");
        var footer = new DockPanel { LastChildFill = false, Children = { error, buttons } };
        DockPanel.SetDock(buttons, Dock.Right);
        Footer = footer;
        switch (start)
        {
            case SignInStart.Cli:
                _ = UseCliAsync();
                break;
            case SignInStart.Token:
                ShowToken();
                break;
            default:
                ShowStart();
                break;
        }
        DetachedFromVisualTree += (_, _) => Dispose();
    }

    protected override Type StyleKeyOverride => typeof(Dialog);

    public void Dispose()
    {
        if (isDisposed)
            return;

        isDisposed = true;
        step?.Cancel();
        closing.Cancel();
        closing.Dispose();
    }

    private void ShowStart()
    {
        CancelStep();
        ShowError(null);
        var hasApp = account.ClientId.Length > 0;
        Description = hasApp
            ? "Runesmith signs in as a GitHub App. You approve it on GitHub and choose which repositories and organizations it may use."
            : null;

        var content = new StackPanel { Spacing = 14, Margin = new Thickness(0, 4, 0, 0) };
        if (hasApp)
        {
            var start = new Button { Classes = { "accent" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Height = 36, IsDefault = true };
            start.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { new SymbolIcon { Data = GitHubIcons.Get(GitHubIcons.GitHub), Size = 16, VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = "Continue with GitHub", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center } },
            };
            start.Click += (_, _) => _ = StartDeviceFlowAsync();
            content.Children.Add(start);
        }
        else
        {
            content.Children.Add(SetupCard());
        }

        content.Children.Add(OrDivider(hasApp ? "or" : "or sign in another way"));
        var hasCli = GitHubCli.IsInstalled();
        var cli = Choice(Icons.Terminal, "Use the GitHub CLI's sign-in",
            hasCli ? "Uses the account you signed in to with gh auth login." : "Install the GitHub CLI, gh, and run gh auth login to use this.", () => _ = UseCliAsync());
        cli.IsEnabled = hasCli;
        content.Children.Add(cli);
        content.Children.Add(Choice(GitHubIcons.Get(GitHubIcons.KeyRound), "Sign in with a token", "Paste a fine-grained or classic personal access token.", ShowToken));
        content.Children.Add(StoreNote());
        page.Content = content;
        SetButtons(CancelButton());
    }

    private Border SetupCard()
    {
        var title = Label("Set up Runesmith's GitHub App first", weight: FontWeight.SemiBold);
        var intro = Label("Signing in through the browser needs a GitHub App, registered once by you or your organization:", "TextSecondaryBrush", wrap: true);
        var steps = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Step(1, ("Create a GitHub App on ", false), ("github.com/settings/apps/new", true), (". Any homepage URL works; turn off the webhook.", false)),
                Step(2, ("Turn on ", false), ("Enable Device Flow", true), (".", false)),
                Step(3, ("Give it these permissions: ", false), (Permissions, false), (".", false)),
                Step(4, ("Copy its ", false), ("Client ID", true), (" into Settings, GitHub, Client ID.", false)),
            },
        };
        var register = IconButton(Icons.ExternalLink, "Register a GitHub App", "small");
        register.Click += (_, _) => openUrl(NewAppUri);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { register } };
        if (openSettings is not null)
        {
            var settings = IconButton(Icons.Settings, "Open Settings", "small");
            settings.Click += (_, _) =>
            {
                Close();
                openSettings();
            };
            actions.Children.Add(settings);
        }

        var card = new Border
        {
            Padding = new Thickness(14, 12),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Child = new StackPanel { Spacing = 10, Children = { title, intro, steps, actions } },
        };
        Brush(card, Border.BackgroundProperty, "SurfaceSunkenBrush");
        Brush(card, Border.BorderBrushProperty, "BorderSubtleBrush");
        return card;
    }

    private static Grid OrDivider(string text)
    {
        var left = Divider(new Thickness(0));
        var right = Divider(new Thickness(0));
        var label = Label(text, "TextMutedBrush", 12);
        label.Margin = new Thickness(10, 0);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), Children = { left, label, right } };
        left.VerticalAlignment = right.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(label, 1);
        Grid.SetColumn(right, 2);
        return grid;
    }

    private static Button Choice(Geometry icon, string title, string hint, Action click)
    {
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { Label(title, weight: FontWeight.Medium), Label(hint, "TextMutedBrush", 12, wrap: true) } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,12,*,Auto"), Children = { Icon(icon, 18), text, Icon(Icons.ChevronRight, 14, "TextMutedBrush") } };
        Grid.SetColumn(text, 2);
        Grid.SetColumn(grid.Children[2], 3);
        var button = new Button
        {
            Content = grid,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(12, 10),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };
        button.Click += (_, _) => click();
        return button;
    }

    private DockPanel StoreNote()
    {
        var text = account.HasSystemSecretStore ? "Tokens are kept in the system's secret store."
            : account.HasSecretStore ? "No system secret store was found, so tokens are kept in a file only you can read."
            : "Tokens are kept only until Runesmith closes.";
        var lockIcon = Icon(Icons.Lock, 12, "TextMutedBrush");
        lockIcon.VerticalAlignment = VerticalAlignment.Top;
        var note = new DockPanel { Margin = new Thickness(2, 2, 0, 0), Children = { lockIcon, Label(text, "TextMutedBrush", 12, wrap: true) } };
        lockIcon.Margin = new Thickness(0, 2, 6, 0);
        DockPanel.SetDock(lockIcon, Dock.Left);
        return note;
    }

    private async Task StartDeviceFlowAsync()
    {
        var token = BeginStep();
        ShowError(null);
        ShowWaiting("Asking GitHub for a code...");
        try
        {
            var clientId = account.ClientId;
            var code = await account.DeviceFlow.StartAsync(clientId, token);
            ShowCode(code);
            var tokens = await account.DeviceFlow.WaitForTokenAsync(clientId, code, token);
            ShowWaiting("Signing in...");
            Close(await account.SignInAsync(tokens, token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is DeviceFlowException or GitHubException)
        {
            ShowStart();
            ShowError(exception.Message);
        }
    }

    private void ShowCode(DeviceCode code)
    {
        Description = "Enter this code on GitHub to approve Runesmith. You choose which repositories it may use right after.";
        var letters = new TextBlock
        {
            Text = code.UserCode,
            FontSize = 30,
            FontWeight = FontWeight.SemiBold,
            LetterSpacing = 6,
            FontFamily = new FontFamily("Cascadia Code, JetBrains Mono, Consolas, Menlo, monospace"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var copy = new Button { Classes = { "icon", "subtle" }, Content = new SymbolIcon { Data = Icons.Copy, Size = 16 }, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        ToolTip.SetTip(copy, "Copy the code");
        copy.Click += async (_, _) =>
        {
            await CopyAsync(code.UserCode);
            ToolTip.SetTip(copy, "Copied");
            copy.Content = new SymbolIcon { Data = Icons.Check, Size = 16 };
        };
        var box = new Border
        {
            Padding = new Thickness(16, 18),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Child = new Grid { Children = { letters, copy } },
        };
        Brush(box, Border.BackgroundProperty, "SurfaceSunkenBrush");
        Brush(box, Border.BorderBrushProperty, "BorderSubtleBrush");

        var open = new Button { Classes = { "accent" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Height = 36, IsDefault = true };
        open.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { new SymbolIcon { Data = Icons.ExternalLink, Size = 15, VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = "Copy Code and Open GitHub", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center } },
        };
        open.Click += async (_, _) =>
        {
            await CopyAsync(code.UserCode);
            openUrl(code.VerificationUri);
        };

        var spinner = new SymbolIcon { Data = Icons.RotateCw, Size = 13, Classes = { "spin" }, VerticalAlignment = VerticalAlignment.Center };
        Brush(spinner, SymbolIcon.ForegroundProperty, "AccentBrush");
        var waiting = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { spinner, Label($"Waiting for approval on {code.VerificationUri.Host}{code.VerificationUri.AbsolutePath}", "TextSecondaryBrush", 12) },
        };
        var expires = Label($"The code works until {code.ExpiresAt.ToLocalTime():t}.", "TextMutedBrush", 12);
        expires.HorizontalAlignment = HorizontalAlignment.Center;
        page.Content = new StackPanel { Spacing = 14, Margin = new Thickness(0, 4, 0, 0), Children = { box, open, waiting, expires } };

        var back = new Button { Content = "Back", MinWidth = 84 };
        back.Click += (_, _) => ShowStart();
        SetButtons(back, CancelButton());
    }

    private void ShowToken()
    {
        CancelStep();
        ShowError(null);
        Description = "Paste a personal access token. Runesmith checks it with GitHub and keeps it in the secret store.";
        var box = new TextBox { PasswordChar = '•', PlaceholderText = "github_pat_... or ghp_...", Classes = { "mono" } };
        var hint = Label("A fine-grained token needs Contents, Metadata and Pull requests access to the repositories you use; a classic token needs the repo and workflow scopes.",
            "TextMutedBrush", 12, wrap: true);
        var links = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(-4, 0, 0, 0),
            Children = { Link("Create a fine-grained token", () => openUrl(FineGrainedTokenUri)), Link("Create a classic token", () => openUrl(ClassicTokenUri)) },
        };
        page.Content = new StackPanel { Spacing = 10, Margin = new Thickness(0, 4, 0, 0), Children = { box, hint, links } };

        var signIn = new Button { Content = "Sign In", Classes = { "accent" }, MinWidth = 96, IsDefault = true, IsEnabled = false };
        box.TextChanged += (_, _) => signIn.IsEnabled = !string.IsNullOrWhiteSpace(box.Text);
        signIn.Click += (_, _) => _ = UseTokenAsync(box.Text ?? "", signIn);
        var back = new Button { Content = "Back", MinWidth = 84 };
        back.Click += (_, _) => ShowStart();
        SetButtons(back, signIn);
        box.AttachedToVisualTree += (_, _) => box.Focus();
    }

    private async Task UseTokenAsync(string text, Button signIn)
    {
        var token = BeginStep();
        signIn.IsEnabled = false;
        ShowError(null);
        try
        {
            Close(await account.SignInWithTokenAsync(text, token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (GitHubException exception)
        {
            ShowError(exception.Failure == GitHubFailure.Unauthorized ? "GitHub does not accept this token. Check that it is complete and not expired." : exception.Message);
            signIn.IsEnabled = true;
        }
    }

    private async Task UseCliAsync()
    {
        var token = BeginStep();
        ShowError(null);
        ShowWaiting("Reading the GitHub CLI's sign-in...");
        try
        {
            Close(await account.SignInWithCliAsync(token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (GitHubException exception)
        {
            ShowStart();
            ShowError(exception.Message);
        }
    }

    private void ShowWaiting(string text)
    {
        var spinner = new SymbolIcon { Data = Icons.RotateCw, Size = 14, Classes = { "spin" }, VerticalAlignment = VerticalAlignment.Center };
        Brush(spinner, SymbolIcon.ForegroundProperty, "AccentBrush");
        page.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 28),
            Children = { spinner, Label(text, "TextSecondaryBrush") },
        };
        var back = new Button { Content = "Back", MinWidth = 84 };
        back.Click += (_, _) => ShowStart();
        SetButtons(back, CancelButton());
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

    private async Task CopyAsync(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
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
