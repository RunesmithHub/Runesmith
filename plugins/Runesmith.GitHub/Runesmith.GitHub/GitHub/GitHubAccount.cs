using System.Composition;
using System.Text.Json;
using Avalonia.Threading;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Settings;
using Runesmith.Sdk.Shell;

namespace Runesmith.GitHub.GitHub;

/// <summary>The signed-in GitHub account: signing in and out, keeping its tokens in the secret store, and handing out a token that is valid
/// now, refreshed when it is about to expire.</summary>
[Export]
[Shared]
internal sealed class GitHubAccount : IGitHubTokenSource, IDisposable
{
    /// <summary>The secret store key of the session; every key of the plugin starts with <c>runesmith.github/</c>.</summary>
    public const string SessionKey = "runesmith.github/session";

    public const string SignInCommand = "github.signIn";

    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan CliTokenLifetime = TimeSpan.FromMinutes(5);

    private readonly ISecretStore? secrets;
    private readonly ISettingsService? settings;
    private readonly INotificationService? notifications;
    private readonly Lazy<ICommandService>? commands;
    private readonly TimeProvider time;
    private readonly GitHubAppIdentity? shippedApp;
    private readonly SemaphoreSlim gate = new(1, 1);
    private StoredSession? session;
    private bool isLoaded;
    private (string Token, DateTimeOffset At)? cliToken;
    private string? discoveredAppSlug;

    [ImportingConstructor]
    public GitHubAccount(GitHubHttp http, [Import(AllowDefault = true)] ISettingsService? settings, [Import(AllowDefault = true)] ISecretStore? secrets,
        [Import(AllowDefault = true)] INotificationService? notifications, [Import(AllowDefault = true)] Lazy<ICommandService>? commands)
        : this(http.Client, settings, GitHubHttp.FakeMode is null ? secrets : null, notifications, commands, TimeProvider.System, shippedApp: GitHubAppFile.Shipped)
    {
        if (GitHubHttp.FakeMode == "signed-in")
        {
            session = FakeGitHubHandler.Session;
            isLoaded = true;
        }
    }

    internal GitHubAccount(HttpClient http, ISettingsService? settings, ISecretStore? secrets, INotificationService? notifications,
        Lazy<ICommandService>? commands, TimeProvider time, Func<TimeSpan, CancellationToken, Task>? delay = null, GitHubAppIdentity? shippedApp = null)
    {
        this.settings = settings;
        this.secrets = secrets;
        this.notifications = notifications;
        this.commands = commands;
        this.time = time;
        this.shippedApp = shippedApp;
        Client = new GitHubClient(http, this, time);
        DeviceFlow = new DeviceFlow(http, time, delay);
    }

    /// <summary>Raised on the UI thread when someone signs in or out, or the sign-in expires.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the client for GitHub's API, which signs its requests with this account's token.</summary>
    public GitHubClient Client { get; }

    public DeviceFlow DeviceFlow { get; }

    /// <summary>Gets the signed-in account, or null when no one is signed in or the session is not loaded yet.</summary>
    public GitHubUser? User => session is { } s ? new GitHubUser(s.Login, s.Name, s.AvatarUrl, $"https://github.com/{s.Login}") : null;

    /// <summary>Gets how the account signed in, or null when no one is.</summary>
    public GitHubSignInKind? Kind => session?.Kind;

    public bool IsSignedIn => session is not null;

    /// <summary>Gets whether the session was read from the secret store yet.</summary>
    public bool IsLoaded => isLoaded;

    /// <summary>Gets whether secrets are kept in the system's secret store, or false when they are kept in a file or only in memory.</summary>
    public bool HasSystemSecretStore => secrets?.IsSystemStore == true;

    /// <summary>Gets whether a secret store keeps the session between runs; without one it lasts until Runesmith closes.</summary>
    public bool HasSecretStore => secrets is not null;

    /// <summary>Gets the GitHub App to sign in as: the user's own from the settings when <c>github.clientId</c> is set, otherwise the one
    /// shipped in <c>github-app.json</c>.</summary>
    /// <summary>Gets the app shipped in <c>github-app.json</c>, or null when the file is missing or broken.</summary>
    public GitHubAppIdentity? ShippedApp => shippedApp;

    public GitHubAppChoice App => GitHubAppChoice.Resolve(Setting(GitHubSettings.ClientId), Setting(GitHubSettings.AppSlug), shippedApp);

    /// <summary>Gets the client id of the GitHub App to sign in as; empty when neither the settings nor the shipped file name one.</summary>
    public string ClientId => GitHubHttp.FakeMode is not null ? FakeGitHubHandler.ClientId : App.ClientId;

    /// <summary>Gets the app's name in its address, from the app in use or its installations, or null while unknown.</summary>
    public string? AppSlug => App.App?.Slug ?? discoveredAppSlug;

    /// <summary>Gets GitHub's page for installing the app on another account or organization, or null while the app's name is unknown.</summary>
    public Uri? InstallUri => AppSlug is { } slug ? GitHubAppUrls.Install(slug) : null;

    /// <summary>Gets the address where the user installs the GitHub App on their account and organizations and picks repositories.</summary>
    public Uri ManageAccessUri => InstallUri ?? new Uri("https://github.com/settings/installations");

    /// <summary>Goes back to the shipped app by clearing the <c>github.clientId</c> and <c>github.appSlug</c> settings, wherever they are set.</summary>
    public void UseShippedApp()
    {
        discoveredAppSlug = null;
        if (settings is null)
            return;

        foreach (var key in new[] { GitHubSettings.ClientId, GitHubSettings.AppSlug })
        {
            foreach (var scope in new[] { SettingScope.User, SettingScope.Workspace })
            {
                if (settings.GetValue(key, scope) is not null)
                    settings.Reset(key, scope);
            }
        }
    }

    /// <summary>Remembers the app's name from its installations, for <see cref="ManageAccessUri"/> when the app in use does not name it.</summary>
    public void RememberAppSlug(string? slug)
    {
        if (!string.IsNullOrWhiteSpace(slug))
            discoveredAppSlug = slug;
    }

    /// <summary>Reads the session from the secret store, once.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (isLoaded)
            return;

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (isLoaded)
                return;

            session = secrets is null ? null : Deserialize(await secrets.GetAsync(SessionKey, cancellationToken).ConfigureAwait(false));
            isLoaded = true;
        }
        finally
        {
            gate.Release();
        }

        RaiseChanged();
    }

    /// <summary>Finishes a device flow sign-in: reads the account the tokens belong to and keeps them.</summary>
    public async Task<GitHubUser> SignInAsync(GitHubTokens tokens, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        var user = await Client.GetUserAsync(tokens.AccessToken, cancellationToken).ConfigureAwait(false);
        await SaveAsync(new StoredSession(GitHubSignInKind.App, user.Login, user.Name, user.AvatarUrl, tokens.AccessToken, tokens.ExpiresAt, tokens.RefreshToken,
            tokens.RefreshExpiresAt), cancellationToken).ConfigureAwait(false);
        return user;
    }

    /// <summary>Signs in with the GitHub CLI's sign-in; its token is read from the CLI each time, never copied into the secret store.</summary>
    public async Task<GitHubUser> SignInWithCliAsync(CancellationToken cancellationToken)
    {
        var token = await GitHubCli.GetTokenAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new GitHubException(GitHubFailure.Unauthorized, "The GitHub CLI is not signed in. Run gh auth login in a terminal first.");
        var user = await Client.GetUserAsync(token, cancellationToken).ConfigureAwait(false);
        cliToken = (token, time.GetUtcNow());
        await SaveAsync(new StoredSession(GitHubSignInKind.Cli, user.Login, user.Name, user.AvatarUrl, null, null, null, null), cancellationToken).ConfigureAwait(false);
        return user;
    }

    /// <summary>Signs in with a personal access token, after checking that GitHub accepts it.</summary>
    public async Task<GitHubUser> SignInWithTokenAsync(string token, CancellationToken cancellationToken)
    {
        token = token.Trim();
        var user = await Client.GetUserAsync(token, cancellationToken).ConfigureAwait(false);
        await SaveAsync(new StoredSession(GitHubSignInKind.PersonalToken, user.Login, user.Name, user.AvatarUrl, token, null, null, null), cancellationToken)
            .ConfigureAwait(false);
        return user;
    }

    /// <summary>Signs out and deletes the tokens.</summary>
    public async Task SignOutAsync()
    {
        session = null;
        cliToken = null;
        if (secrets is not null)
            await secrets.DeleteAsync(SessionKey).ConfigureAwait(false);
        RaiseChanged();
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken).ConfigureAwait(false);
        switch (session)
        {
            case null:
                return null;
            case { Kind: GitHubSignInKind.Cli }:
                if (cliToken is { } cached && time.GetUtcNow() - cached.At < CliTokenLifetime)
                    return cached.Token;
                var token = await GitHubCli.GetTokenAsync(cancellationToken).ConfigureAwait(false);
                cliToken = token is null ? null : (token, time.GetUtcNow());
                return token;
            case { AccessTokenExpires: { } expires } when expires - RefreshMargin <= time.GetUtcNow():
                return await RefreshAsync(cancellationToken).ConfigureAwait(false);
            default:
                return session.AccessToken;
        }
    }

    public void Dispose() => gate.Dispose();

    public void OnUnauthorized()
    {
        if (session is null)
            return;

        var kind = session.Kind;
        _ = ExpireAsync(kind == GitHubSignInKind.Cli ? "The GitHub CLI's sign-in no longer works. Sign in to GitHub again." : "GitHub no longer accepts the sign-in. Sign in to GitHub again.");
    }

    private async Task<string?> RefreshAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        StoredSession current;
        try
        {
            if (session is not { } s)
                return null;
            if (s.AccessTokenExpires is not { } expires || expires - RefreshMargin > time.GetUtcNow())
                return s.AccessToken;
            current = s;

            if (current.RefreshToken is null || current.RefreshTokenExpires <= time.GetUtcNow() || ClientId.Length == 0)
            {
                session = null;
            }
            else
            {
                try
                {
                    var tokens = await DeviceFlow.RefreshAsync(ClientId, current.RefreshToken, cancellationToken).ConfigureAwait(false);
                    var refreshed = current with
                    {
                        AccessToken = tokens.AccessToken,
                        AccessTokenExpires = tokens.ExpiresAt,
                        RefreshToken = tokens.RefreshToken ?? current.RefreshToken,
                        RefreshTokenExpires = tokens.RefreshToken is null ? current.RefreshTokenExpires : tokens.RefreshExpiresAt,
                    };
                    session = refreshed;
                    if (secrets is not null)
                        await secrets.SetAsync(SessionKey, Serialize(refreshed), cancellationToken).ConfigureAwait(false);
                    return refreshed.AccessToken;
                }
                catch (DeviceFlowException exception) when (exception.Failure == DeviceFlowFailure.Network)
                {
                    throw new GitHubException(GitHubFailure.Network, exception.Message, inner: exception);
                }
                catch (DeviceFlowException)
                {
                    session = null;
                }
            }
        }
        finally
        {
            gate.Release();
        }

        await ExpireAsync("The GitHub sign-in expired. Sign in to GitHub again.").ConfigureAwait(false);
        return null;
    }

    private async Task ExpireAsync(string message)
    {
        session = null;
        cliToken = null;
        if (secrets is not null)
        {
            try
            {
                await secrets.DeleteAsync(SessionKey).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
            }
        }

        RaiseChanged();
        notifications?.Notify(NotificationKind.Warning, "Signed out of GitHub", message, commands is null ? null : "Sign In",
            commands is null ? null : () => _ = commands.Value.ExecuteAsync(SignInCommand));
    }

    private async Task SaveAsync(StoredSession stored, CancellationToken cancellationToken)
    {
        if (secrets is not null)
            await secrets.SetAsync(SessionKey, Serialize(stored), cancellationToken).ConfigureAwait(false);
        session = stored;
        isLoaded = true;
        RaiseChanged();
    }

    private string? Setting(string key)
    {
        try
        {
            return settings?.Get<string>(key);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    internal static string Serialize(StoredSession stored) => JsonSerializer.Serialize(stored, GitHubJson.Default.StoredSession);

    private static StoredSession? Deserialize(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize(json, GitHubJson.Default.StoredSession);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void RaiseChanged()
    {
        if (Changed is null)
            return;

        if (Dispatcher.UIThread.CheckAccess())
            Changed.Invoke(this, EventArgs.Empty);
        else
            Dispatcher.UIThread.Post(() => Changed?.Invoke(this, EventArgs.Empty));
    }
}
