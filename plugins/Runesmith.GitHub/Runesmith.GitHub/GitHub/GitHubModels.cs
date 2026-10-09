using System.Text.Json.Serialization;

namespace Runesmith.GitHub.GitHub;

/// <summary>A GitHub account as <c>GET /user</c> describes it.</summary>
internal sealed record GitHubUser(string Login, string? Name, string AvatarUrl, string? HtmlUrl);

/// <summary>The account that owns a repository or an installation: a user or an organization.</summary>
internal sealed record GitHubOwner(string Login, string AvatarUrl, string? Type)
{
    public bool IsOrganization => string.Equals(Type, "Organization", StringComparison.Ordinal);
}

/// <summary>An installation of Runesmith's GitHub App on an account or organization.</summary>
internal sealed record GitHubInstallation(long Id, GitHubOwner Account, string? RepositorySelection, string? AppSlug, string? HtmlUrl);

internal sealed record GitHubInstallationPage(int TotalCount, List<GitHubInstallation> Installations);

internal sealed record GitHubRepositoryPage(int TotalCount, List<GitHubRepository> Repositories);

/// <summary>A repository as the REST API describes it.</summary>
internal sealed record GitHubRepository(
    long Id,
    string Name,
    string FullName,
    GitHubOwner Owner,
    bool Private,
    string? Description,
    string? Language,
    int StargazersCount,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? PushedAt,
    string CloneUrl,
    string HtmlUrl,
    string? DefaultBranch,
    bool Fork,
    bool Archived)
{
    /// <summary>Gets when the repository last changed: its last push, or its last update when it was never pushed to.</summary>
    public DateTimeOffset? LastChanged => PushedAt > UpdatedAt ? PushedAt : UpdatedAt ?? PushedAt;
}

internal sealed record GitHubBranch(string Name);

internal sealed record GitHubNewPullRequest(string Title, string Head, string Base, string? Body, bool Draft);

internal sealed record GitHubCreatedPullRequest(int Number, string HtmlUrl);

internal sealed record GitHubErrorBody(string? Message, List<GitHubFieldError>? Errors);

internal sealed record GitHubFieldError(string? Message, string? Code, string? Field);

internal sealed record DeviceCodeResponse(string DeviceCode, string UserCode, string VerificationUri, int ExpiresIn, int Interval);

internal sealed record AccessTokenResponse(
    string? AccessToken,
    int? ExpiresIn,
    string? RefreshToken,
    int? RefreshTokenExpiresIn,
    string? Error,
    string? ErrorDescription,
    int? Interval);

/// <summary>What is kept in the secret store for the signed-in account.</summary>
internal sealed record StoredSession(
    GitHubSignInKind Kind,
    string Login,
    string? Name,
    string AvatarUrl,
    string? AccessToken,
    DateTimeOffset? AccessTokenExpires,
    string? RefreshToken,
    DateTimeOffset? RefreshTokenExpires);

/// <summary>How the account signed in, which decides where its token comes from and how repositories are listed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GitHubSignInKind>))]
internal enum GitHubSignInKind
{
    /// <summary>Runesmith's GitHub App, through the device flow; repositories come from the app's installations.</summary>
    App,

    /// <summary>The GitHub CLI's own sign-in, read with <c>gh auth token</c> each time it is needed.</summary>
    Cli,

    /// <summary>A personal access token the user pasted.</summary>
    PersonalToken,
}

/// <summary>JSON for the REST API, the device flow and the stored session, generated at build time.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(GitHubUser))]
[JsonSerializable(typeof(GitHubInstallationPage))]
[JsonSerializable(typeof(GitHubRepositoryPage))]
[JsonSerializable(typeof(List<GitHubRepository>))]
[JsonSerializable(typeof(GitHubRepository))]
[JsonSerializable(typeof(List<GitHubBranch>))]
[JsonSerializable(typeof(GitHubNewPullRequest))]
[JsonSerializable(typeof(GitHubCreatedPullRequest))]
[JsonSerializable(typeof(GitHubErrorBody))]
[JsonSerializable(typeof(DeviceCodeResponse))]
[JsonSerializable(typeof(AccessTokenResponse))]
[JsonSerializable(typeof(StoredSession))]
internal sealed partial class GitHubJson : JsonSerializerContext;
