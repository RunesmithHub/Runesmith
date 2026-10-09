using System.Globalization;
using System.Text.Json.Serialization;
using Runesmith.Sdk.VersionControl;

namespace Runesmith.GitHub.GitHub;

internal sealed record GraphQLRequest(string Query, PullRequestQueryVariables Variables);

internal sealed record PullRequestQueryVariables(string Owner, string Name);

internal sealed record GraphQLResponse(GraphQLData? Data, List<GraphQLError>? Errors);

internal sealed record GraphQLError(string? Message, string? Type);

internal sealed record GraphQLData(GraphQLRepository? Repository);

internal sealed record GraphQLRepository(GraphQLPullRequestConnection PullRequests);

internal sealed record GraphQLPullRequestConnection(List<GraphQLPullRequest> Nodes);

internal sealed record GraphQLPullRequest(
    int Number,
    string Title,
    string Url,
    bool IsDraft,
    string HeadRefName,
    string BaseRefName,
    DateTimeOffset UpdatedAt,
    GraphQLActor? Author,
    string? ReviewDecision,
    GraphQLCommitConnection? Commits)
{
    /// <summary>Gets the pull request as Runesmith shows it, with the ref that holds its commits on the base repository.</summary>
    public PullRequest ToPullRequest() => new(Number, Title, Author?.Login ?? "ghost", HeadRefName, BaseRefName, new Uri(Url))
    {
        AuthorAvatarUrl = Uri.TryCreate(Author?.AvatarUrl, UriKind.Absolute, out var avatar) ? avatar : null,
        IsDraft = IsDraft,
        UpdatedAt = UpdatedAt,
        HeadRef = string.Create(CultureInfo.InvariantCulture, $"refs/pull/{Number}/head"),
        Review = ReviewDecision switch
        {
            "APPROVED" => ReviewState.Approved,
            "CHANGES_REQUESTED" => ReviewState.ChangesRequested,
            "REVIEW_REQUIRED" => ReviewState.ReviewRequired,
            _ => ReviewState.None,
        },
        Checks = Commits?.Nodes is [.., var last] ? last.Commit.StatusCheckRollup?.State switch
        {
            "SUCCESS" => ChecksState.Passing,
            "FAILURE" or "ERROR" => ChecksState.Failing,
            "PENDING" or "EXPECTED" => ChecksState.Pending,
            _ => ChecksState.None,
        } : ChecksState.None,
    };
}

internal sealed record GraphQLActor(string Login, string? AvatarUrl);

internal sealed record GraphQLCommitConnection(List<GraphQLCommitNode> Nodes);

internal sealed record GraphQLCommitNode(GraphQLCommit Commit);

internal sealed record GraphQLCommit(GraphQLStatusRollup? StatusCheckRollup);

internal sealed record GraphQLStatusRollup(string? State);

/// <summary>JSON for the GraphQL API, whose fields are camel case.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GraphQLRequest))]
[JsonSerializable(typeof(GraphQLResponse))]
internal sealed partial class GitHubGraphJson : JsonSerializerContext;
