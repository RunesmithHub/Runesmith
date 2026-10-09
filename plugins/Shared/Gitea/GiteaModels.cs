using System.Text.Json.Serialization;

namespace Runesmith.Plugins.Gitea;

/// <summary>An account as <c>GET /user</c> describes it.</summary>
internal sealed record GiteaUser(long Id, string Login, string? FullName, string? AvatarUrl, string? HtmlUrl);

/// <summary>An organization the account belongs to, as <c>GET /user/orgs</c> lists it; <see cref="Name"/> is its login.</summary>
internal sealed record GiteaOrganization(long Id, string Name, string? FullName, string? AvatarUrl);

/// <summary>The owner of a repository or the author of a pull request.</summary>
internal sealed record GiteaOwner(string Login, string? AvatarUrl);

/// <summary>A repository as the API describes it.</summary>
internal sealed record GiteaRepository(
    long Id,
    GiteaOwner Owner,
    string Name,
    string FullName,
    string? Description,
    bool Private,
    bool Fork,
    bool Archived,
    string HtmlUrl,
    string CloneUrl,
    string? DefaultBranch,
    string? Language,
    int StarsCount,
    DateTimeOffset? UpdatedAt);

/// <summary>One side of a pull request: the branch and its commit.</summary>
internal sealed record GiteaBranchRef(string Ref, string? Sha, string? Label);

/// <summary>A pull request as the API describes it.</summary>
internal sealed record GiteaPullRequest(
    long Id,
    int Number,
    string Title,
    string HtmlUrl,
    string? State,
    bool? Draft,
    GiteaOwner? User,
    GiteaBranchRef Head,
    GiteaBranchRef Base,
    List<GiteaOwner>? RequestedReviewers,
    DateTimeOffset? UpdatedAt);

/// <summary>A review of a pull request.</summary>
internal sealed record GiteaReview(long Id, GiteaOwner? User, string? State, bool Stale, bool Dismissed, DateTimeOffset? SubmittedAt);

/// <summary>The combined status of a commit: the worst of its statuses.</summary>
internal sealed record GiteaCombinedStatus(string? State, int TotalCount);

/// <summary>The body of <c>POST /repos/{owner}/{repo}/pulls</c>.</summary>
internal sealed record GiteaNewPullRequest(string Head, string Base, string Title, string? Body);

/// <summary>What a version endpoint answers.</summary>
internal sealed record GiteaVersion(string? Version);

/// <summary>An error the API answers with.</summary>
internal sealed record GiteaErrorBody(string? Message, List<string>? Errors);

/// <summary>What the OAuth2 token endpoint answers, for a code or a refresh token.</summary>
internal sealed record OAuthTokenResponse(string? AccessToken, string? TokenType, int? ExpiresIn, string? RefreshToken, string? Error, string? ErrorDescription);

/// <summary>An account the plugin knows, as its state file keeps it; the tokens are in the secret store.</summary>
internal sealed record StoredAccount(string Server, string? Login, string? FullName, string? AvatarUrl);

/// <summary>The tokens of an account, as the secret store keeps them.</summary>
internal sealed record StoredTokens(SignInKind Kind, string AccessToken, DateTimeOffset? ExpiresAt, string? RefreshToken);

/// <summary>How an account signed in, which decides how its token is sent and whether it is refreshed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SignInKind>))]
internal enum SignInKind
{
    /// <summary>OAuth2 in the browser, with an access token that expires and a refresh token.</summary>
    OAuth,

    /// <summary>An access token the user created on the server and pasted.</summary>
    Token,
}

/// <summary>JSON for the API, the OAuth2 endpoints and what the plugin keeps, generated at build time.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(GiteaUser))]
[JsonSerializable(typeof(List<GiteaOrganization>))]
[JsonSerializable(typeof(GiteaRepository))]
[JsonSerializable(typeof(List<GiteaRepository>))]
[JsonSerializable(typeof(GiteaPullRequest))]
[JsonSerializable(typeof(List<GiteaPullRequest>))]
[JsonSerializable(typeof(List<GiteaReview>))]
[JsonSerializable(typeof(GiteaCombinedStatus))]
[JsonSerializable(typeof(GiteaNewPullRequest))]
[JsonSerializable(typeof(GiteaVersion))]
[JsonSerializable(typeof(GiteaErrorBody))]
[JsonSerializable(typeof(OAuthTokenResponse))]
[JsonSerializable(typeof(List<StoredAccount>))]
[JsonSerializable(typeof(StoredTokens))]
internal sealed partial class GiteaJson : JsonSerializerContext;
