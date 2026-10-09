using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Runesmith.GitHub.GitHub;

/// <summary>A GitHub App's public identity. None of it is secret: the device flow signs in with the client ID alone.</summary>
/// <param name="AppId">The app's numeric id, or 0 when unknown.</param>
/// <param name="ClientId">The client ID the device flow signs in with.</param>
/// <param name="Slug">The app's name in its address, <c>github.com/apps/&lt;slug&gt;</c>, or null when unknown.</param>
/// <param name="Name">The app's display name, or null when unknown.</param>
internal sealed record GitHubAppIdentity(long AppId, string ClientId, string? Slug, string? Name);

/// <summary>Where the GitHub App Runesmith signs in as comes from.</summary>
internal enum GitHubAppSource
{
    /// <summary>No app: neither the settings nor the shipped file name one.</summary>
    None,

    /// <summary>The app in <c>github-app.json</c> next to the plugin.</summary>
    Shipped,

    /// <summary>The user's own app, from the <c>github.clientId</c> and <c>github.appSlug</c> settings.</summary>
    Settings,
}

/// <summary>The GitHub App in use and where it came from.</summary>
internal sealed record GitHubAppChoice(GitHubAppSource Source, GitHubAppIdentity? App)
{
    /// <summary>Gets the client ID to sign in with, or an empty string when there is no app.</summary>
    public string ClientId => App?.ClientId ?? "";

    /// <summary>Picks the app: the settings' client ID when set, with the settings' slug; otherwise the shipped app.</summary>
    public static GitHubAppChoice Resolve(string? settingClientId, string? settingSlug, GitHubAppIdentity? shipped)
    {
        if (settingClientId?.Trim() is { Length: > 0 } clientId)
            return new(GitHubAppSource.Settings, new GitHubAppIdentity(0, clientId, settingSlug?.Trim() is { Length: > 0 } slug ? slug : null, null));

        return shipped is null ? new(GitHubAppSource.None, null) : new(GitHubAppSource.Shipped, shipped);
    }
}

/// <summary>Reads the GitHub App a build of Runesmith ships, from <c>github-app.json</c> next to the plugin.</summary>
internal static class GitHubAppFile
{
    public const string FileName = "github-app.json";

    /// <summary>Gets the shipped app, read once.</summary>
    public static GitHubAppIdentity? Shipped { get; } = Read(Path.Combine(Path.GetDirectoryName(typeof(GitHubAppFile).Assembly.Location) ?? AppContext.BaseDirectory, FileName));

    /// <summary>Reads an app file; returns null when it is missing, unreadable, or has no client ID.</summary>
    public static GitHubAppIdentity? Read(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Parses an app file's text; returns null when it is not valid or has no client ID.</summary>
    public static GitHubAppIdentity? Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, GitHubAppJson.Default.GitHubAppFileContent) is { ClientId: { } clientId } content && clientId.Trim().Length > 0
                ? new GitHubAppIdentity(content.AppId ?? 0, clientId.Trim(), Blank(content.Slug), Blank(content.Name))
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

internal sealed record GitHubAppFileContent(long? AppId, string? ClientId, string? Slug, string? Name);

/// <summary>JSON for <c>github-app.json</c>, generated at build time.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(GitHubAppFileContent))]
internal sealed partial class GitHubAppJson : JsonSerializerContext;

/// <summary>The addresses of a GitHub App's pages on github.com.</summary>
internal static class GitHubAppUrls
{
    /// <summary>Gets GitHub's install page, where the user picks their account or an organization and the repositories; organization
    /// members who may not install send the owners a request from there.</summary>
    public static Uri Install(string slug) => new($"https://github.com/apps/{Uri.EscapeDataString(slug)}/installations/new");

    /// <summary>Gets an installation's settings page, where its repositories are chosen.</summary>
    public static Uri Configure(GitHubInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);
        var id = installation.Id.ToString(CultureInfo.InvariantCulture);
        return installation.Account.IsOrganization
            ? new Uri($"https://github.com/organizations/{Uri.EscapeDataString(installation.Account.Login)}/settings/installations/{id}")
            : new Uri($"https://github.com/settings/installations/{id}");
    }
}
