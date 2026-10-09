using Avalonia.Threading;
using Runesmith.Sdk.Settings;
using Runesmith.Sdk.Shell;

namespace Runesmith.Plugins.Gitea;

/// <summary>What every account of a plugin shares: the service, the HTTP client, the secret store, the settings and the system's services.</summary>
internal sealed class GiteaContext(
    GiteaFlavor flavor,
    HttpClient http,
    ISecretStore? secrets,
    ISettingsService? settings,
    INotificationService? notifications,
    ILauncher? launcher,
    TimeProvider? time = null)
{
    public GiteaFlavor Flavor => flavor;

    public HttpClient Http => http;

    /// <summary>Gets the secret store, or null when tokens last only until Runesmith closes.</summary>
    public ISecretStore? Secrets => secrets;

    public INotificationService? Notifications => notifications;

    public TimeProvider Time { get; } = time ?? TimeProvider.System;

    /// <summary>Gets whether tokens are kept in the system's secret store rather than a file or memory.</summary>
    public bool HasSystemSecretStore => secrets?.IsSystemStore == true;

    /// <summary>Gets the secret store key of an account's tokens, such as <c>runesmith.forgejo/codeberg.org/alice</c>.</summary>
    public string SecretKey(GiteaServer server, string login) => $"{flavor.PluginId}/{server.Key}/{login}";

    /// <summary>Gets the OAuth2 client id set for a server, or null when browser sign-in is not set up for it.</summary>
    public string? ClientIdFor(GiteaServer server) => ClientIds.Find(ClientIdsValue, server, flavor.DefaultServer)
        ?? (flavor.FakeMode is not null && server == flavor.DefaultServer ? FakeGiteaHandler.ClientId : null);

    /// <summary>Gets whether the browser should not be opened, because the answers are made up for screenshots and tests.</summary>
    public bool IsFake => flavor.FakeMode is not null;

    /// <summary>Sets the OAuth2 client id of a server in the user's settings.</summary>
    public void SetClientId(GiteaServer server, string clientId) =>
        settings?.Set(flavor.ClientIdsSetting, ClientIds.With(ClientIdsValue, server, clientId, flavor.DefaultServer));

    /// <summary>Opens a page in the browser, or shows its address when Runesmith cannot open one.</summary>
    public void OpenUrl(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (IsFake)
            return;

        if (launcher is not null)
            launcher.OpenUrl(url);
        else
            notifications?.Notify(NotificationKind.Info, "Open this address in a browser", url.AbsoluteUri);
    }

    /// <summary>Raises an event on the UI thread.</summary>
    public static void OnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    private string? ClientIdsValue
    {
        get
        {
            try
            {
                return settings?.Get<string>(flavor.ClientIdsSetting);
            }
            catch (KeyNotFoundException)
            {
                return null;
            }
        }
    }
}
