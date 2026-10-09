using System.Composition;
using Runesmith.Sdk.Settings;

namespace Runesmith.GitHub.GitHub;

/// <summary>The GitHub settings: your own GitHub App to sign in as, instead of the one Runesmith ships.</summary>
[Export(typeof(ISettingContributor))]
internal sealed class GitHubSettings : ISettingContributor
{
    public const string Category = "GitHub";
    public const string ClientId = "github.clientId";
    public const string AppSlug = "github.appSlug";

    public IEnumerable<SettingDefinition> Settings { get; } =
    [
        new(ClientId, "Client ID", Category, "")
        {
            Description = "The client ID of your own GitHub App, from the app's settings on GitHub; the app needs the device flow turned on. Leave it empty to sign in with Runesmith's app.",
        },
        new(AppSlug, "App name in URLs", Category, "")
        {
            Description = "Your own app's name as it appears in its address, github.com/apps/<name>, for adding organizations and Manage Access. Used only with Client ID.",
        },
    ];
}
