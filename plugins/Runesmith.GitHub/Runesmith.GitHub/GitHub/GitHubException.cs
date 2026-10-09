namespace Runesmith.GitHub.GitHub;

/// <summary>Why a request to GitHub failed.</summary>
internal enum GitHubFailure
{
    /// <summary>The token was refused or is missing; the user has to sign in again.</summary>
    Unauthorized,

    /// <summary>The rate limit is used up until <see cref="GitHubException.ResetsAt"/>.</summary>
    RateLimited,

    /// <summary>The token may not do this, such as an app that is not installed on the repository's owner.</summary>
    Forbidden,

    NotFound,

    /// <summary>GitHub refused the request's content, such as a pull request that exists already.</summary>
    Invalid,

    /// <summary>GitHub could not be reached.</summary>
    Network,

    /// <summary>GitHub answered with an error of its own.</summary>
    Server,
}

/// <summary>A failed request to GitHub, with a message for people; it never carries a token.</summary>
internal sealed class GitHubException(GitHubFailure failure, string message, DateTimeOffset? resetsAt = null, Exception? inner = null)
    : Exception(message, inner)
{
    public GitHubFailure Failure { get; } = failure;

    /// <summary>Gets when the rate limit resets, for <see cref="GitHubFailure.RateLimited"/>.</summary>
    public DateTimeOffset? ResetsAt { get; } = resetsAt;
}
