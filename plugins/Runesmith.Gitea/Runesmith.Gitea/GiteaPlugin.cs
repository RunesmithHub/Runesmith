using System.Composition;
using HammerUI.Services;
using Runesmith.Plugins.Gitea;
using Runesmith.Sdk.Plugins;
using Runesmith.Sdk.Settings;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.VersionControl;

namespace Runesmith.Gitea;

/// <summary>What sets the Gitea plugin apart: gitea.com as its default server, and Gitea servers, which answer <c>/api/v1/version</c> but not Forgejo's version endpoint.</summary>
internal static class GiteaService
{
    /// <summary>The environment variable that replaces every server with made-up answers and accounts, for tests and screenshots only:
    /// <c>signed-in</c> or <c>signed-out</c>. Nothing reaches a server while it is set.</summary>
    public const string FakeVariable = "RUNESMITH_GITEA_FAKE";

    public static GiteaFlavor Flavor { get; } = new("runesmith.gitea", "gitea", "Gitea", GiteaIcons.Gitea, ServerKind.Gitea, GiteaServer.Of("gitea.com"), "gitea.com", FakeVariable);
}

/// <summary>The Gitea accounts, for the clone dialog, the Pull Requests window and links.</summary>
[Export(typeof(IRepositoryHostProvider))]
[Export]
[Shared]
[method: ImportingConstructor]
internal sealed class GiteaHostProvider(
    [Import(AllowDefault = true)] ISecretStore? secrets,
    [Import(AllowDefault = true)] ISettingsService? settings,
    [Import(AllowDefault = true)] INotificationService? notifications,
    [Import(AllowDefault = true)] ILauncher? launcher,
    [Import(AllowDefault = true)] IDialogService? dialogs)
    : GiteaPluginProvider(GiteaService.Flavor, secrets, settings, notifications, launcher, dialogs);

/// <summary>Registers the Gitea icons and reads the accounts' tokens when Runesmith starts.</summary>
[Export(typeof(IPlugin))]
[method: ImportingConstructor]
internal sealed class GiteaPlugin(GiteaHostProvider provider) : GiteaPluginStart(provider);

/// <summary>Gives Git the token of a signed-in Gitea account for its server's HTTPS remotes.</summary>
[Export(typeof(IGitCredentialSource))]
[method: ImportingConstructor]
internal sealed class GiteaCredentials(GiteaHostProvider provider) : GiteaPluginCredentials(provider);

/// <summary>Lists the Gitea accounts at the top of Settings › Gitea.</summary>
[Export(typeof(ISettingsSectionProvider))]
[method: ImportingConstructor]
internal sealed class GiteaSettingsSection(GiteaHostProvider provider) : GiteaPluginSection(provider);

/// <summary>The Gitea settings: the OAuth2 client IDs per server.</summary>
[Export(typeof(ISettingContributor))]
internal sealed class GiteaSettings() : GiteaPluginSettings(GiteaService.Flavor);
