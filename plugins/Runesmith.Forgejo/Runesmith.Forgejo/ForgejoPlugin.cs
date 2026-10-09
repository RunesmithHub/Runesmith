using System.Composition;
using HammerUI.Services;
using Runesmith.Plugins.Gitea;
using Runesmith.Sdk.Plugins;
using Runesmith.Sdk.Settings;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.VersionControl;

namespace Runesmith.Forgejo;

/// <summary>What sets the Forgejo plugin apart: Codeberg as its default server, and Forgejo servers, which answer <c>/api/forgejo/v1/version</c>.</summary>
internal static class ForgejoService
{
    /// <summary>The environment variable that replaces every server with made-up answers and accounts, for tests and screenshots only:
    /// <c>signed-in</c> or <c>signed-out</c>. Nothing reaches a server while it is set.</summary>
    public const string FakeVariable = "RUNESMITH_FORGEJO_FAKE";

    public static GiteaFlavor Flavor { get; } = new("runesmith.forgejo", "forgejo", "Forgejo", GiteaIcons.Forgejo, ServerKind.Forgejo, GiteaServer.Of("codeberg.org"), "Codeberg", FakeVariable);
}

/// <summary>The Forgejo accounts, for the clone dialog, the Pull Requests window and links.</summary>
[Export(typeof(IRepositoryHostProvider))]
[Export]
[Shared]
[method: ImportingConstructor]
internal sealed class ForgejoHostProvider(
    [Import(AllowDefault = true)] ISecretStore? secrets,
    [Import(AllowDefault = true)] ISettingsService? settings,
    [Import(AllowDefault = true)] INotificationService? notifications,
    [Import(AllowDefault = true)] ILauncher? launcher,
    [Import(AllowDefault = true)] IDialogService? dialogs)
    : GiteaPluginProvider(ForgejoService.Flavor, secrets, settings, notifications, launcher, dialogs);

/// <summary>Registers the Forgejo icons and reads the accounts' tokens when Runesmith starts.</summary>
[Export(typeof(IPlugin))]
[method: ImportingConstructor]
internal sealed class ForgejoPlugin(ForgejoHostProvider provider) : GiteaPluginStart(provider);

/// <summary>Gives Git the token of a signed-in Forgejo account for its server's HTTPS remotes.</summary>
[Export(typeof(IGitCredentialSource))]
[method: ImportingConstructor]
internal sealed class ForgejoCredentials(ForgejoHostProvider provider) : GiteaPluginCredentials(provider);

/// <summary>Lists the Forgejo accounts at the top of Settings › Forgejo.</summary>
[Export(typeof(ISettingsSectionProvider))]
[method: ImportingConstructor]
internal sealed class ForgejoSettingsSection(ForgejoHostProvider provider) : GiteaPluginSection(provider);

/// <summary>The Forgejo settings: the OAuth2 client IDs per server.</summary>
[Export(typeof(ISettingContributor))]
internal sealed class ForgejoSettings() : GiteaPluginSettings(ForgejoService.Flavor);
