using Avalonia.Controls;
using HammerUI.Services;
using Runesmith.Sdk;
using Runesmith.Sdk.Plugins;
using Runesmith.Sdk.Settings;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.VersionControl;

namespace Runesmith.Plugins.Gitea;

/// <summary>The provider as a plugin exports it: built from Runesmith's services, with its accounts in the state folder, its tokens in the
/// secret store and its dialogs shown with the window's dialog service. Under the fake variable it uses made-up answers and accounts instead.</summary>
internal abstract class GiteaPluginProvider : GiteaHostProvider
{
    protected GiteaPluginProvider(GiteaFlavor flavor, ISecretStore? secrets, ISettingsService? settings, INotificationService? notifications, ILauncher? launcher,
        IDialogService? dialogs)
        : base(CreateContext(flavor, secrets, settings, notifications, launcher), CreateStore(flavor))
    {
        Prompts = new GiteaPrompts(this, dialogs);
        SignInPrompt = Prompts;
        AddAccountPrompt = Prompts;
        Avatars = new AvatarCache(Context.Http, AvatarCache.FolderFor(flavor.PluginId));
        if (flavor.FakeMode == "signed-in")
        {
            var fake = FakeGiteaHandler.Accounts(flavor);
            foreach (var host in Accounts)
            {
                if (fake.FirstOrDefault(entry => entry.Account.Server == host.Server.Key && entry.Account.Login == host.Login).Tokens is { } tokens)
                    host.UseTokens(tokens);
            }
        }
    }

    public GiteaPrompts Prompts { get; }

    public AvatarCache Avatars { get; }

    private static GiteaContext CreateContext(GiteaFlavor flavor, ISecretStore? secrets, ISettingsService? settings, INotificationService? notifications,
        ILauncher? launcher)
    {
        var fake = flavor.FakeMode is not null;
        HttpMessageHandler handler = fake ? new FakeGiteaHandler(flavor.Kind) : new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        return new GiteaContext(flavor, http, fake ? null : secrets, settings, notifications, launcher);
    }

    private static AccountStore CreateStore(GiteaFlavor flavor)
    {
        if (flavor.FakeMode is null)
            return new AccountStore(Path.Combine(RunesmithPaths.State, flavor.PluginId, "accounts.json"));

        var store = new AccountStore(null);
        if (flavor.FakeMode == "signed-in")
            store.Save(FakeGiteaHandler.Accounts(flavor).Select(entry => entry.Account));
        return store;
    }
}

/// <summary>The plugin's startup work: its icons, and reading its accounts' tokens.</summary>
internal abstract class GiteaPluginStart(GiteaHostProvider provider) : IPlugin
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        GiteaIcons.Register();
        try
        {
            await provider.LoadAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
        }
    }

    public void Dispose()
    {
    }
}

/// <summary>Gives Git the plugin's accounts' tokens.</summary>
internal abstract class GiteaPluginCredentials(GiteaHostProvider provider) : GiteaCredentialSource(provider);

/// <summary>Puts the plugin's accounts at the top of its settings category.</summary>
internal abstract class GiteaPluginSection(GiteaPluginProvider provider) : ISettingsSectionProvider
{
    public string Category => provider.Context.Flavor.SettingsCategory;

    public Control CreateSection()
    {
        GiteaIcons.Register();
        return new AccountsCard(provider, provider.Prompts, provider.Avatars);
    }
}

/// <summary>The plugin's setting: the OAuth2 client ids of the servers where browser sign-in is set up.</summary>
internal abstract class GiteaPluginSettings(GiteaFlavor flavor) : ISettingContributor
{
    public IEnumerable<SettingDefinition> Settings { get; } =
    [
        new(flavor.ClientIdsSetting, "OAuth2 client IDs", flavor.SettingsCategory, "")
        {
            Description = $"The client ID of the OAuth2 application registered for Runesmith on each server, as server=ID pairs separated by commas, " +
                $"such as {flavor.DefaultServer.Key}=1a2b3c4d. An ID without a server is for {flavor.DefaultServerName}. Servers without one sign in with an access token.",
        },
    ];
}
