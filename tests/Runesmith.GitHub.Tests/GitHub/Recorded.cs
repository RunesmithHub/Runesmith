namespace Runesmith.GitHub.Tests.GitHub;

/// <summary>Responses recorded from GitHub's API, trimmed to the fields Runesmith reads plus a few it ignores.</summary>
internal static class Recorded
{
    public const string User = """
        {"login":"octocat","id":583231,"node_id":"MDQ6VXNlcjU4MzIzMQ==","avatar_url":"https://avatars.githubusercontent.com/u/583231?v=4","gravatar_id":"",
         "url":"https://api.github.com/users/octocat","html_url":"https://github.com/octocat","type":"User","site_admin":false,"name":"The Octocat",
         "company":"@github","blog":"https://github.blog","location":"San Francisco","email":null,"public_repos":8,"followers":21000,
         "created_at":"2011-01-25T18:44:36Z","updated_at":"2026-09-22T11:24:29Z"}
        """;

    public const string Installations = """
        {"total_count":2,"installations":[
          {"id":1001,"client_id":"Iv23liExample","account":{"login":"octocat","id":583231,"avatar_url":"https://avatars.githubusercontent.com/u/583231?v=4","type":"User"},
           "repository_selection":"selected","access_tokens_url":"https://api.github.com/app/installations/1001/access_tokens",
           "repositories_url":"https://api.github.com/installation/repositories","html_url":"https://github.com/settings/installations/1001",
           "app_id":42,"app_slug":"runesmith","target_id":583231,"target_type":"User","permissions":{"contents":"write","metadata":"read","pull_requests":"write"},
           "events":[],"created_at":"2026-09-01T10:00:00.000Z","updated_at":"2026-09-01T10:00:00.000Z","single_file_name":null},
          {"id":1002,"client_id":"Iv23liExample","account":{"login":"github","id":9919,"avatar_url":"https://avatars.githubusercontent.com/u/9919?v=4","type":"Organization"},
           "repository_selection":"all","html_url":"https://github.com/organizations/github/settings/installations/1002","app_id":42,"app_slug":"runesmith",
           "target_type":"Organization","permissions":{},"events":[]}]}
        """;

    public static string Repository(long id, string owner, string name, string ownerType = "User", bool isPrivate = false, string? description = "A repository",
        string pushedAt = "2026-09-30T08:00:00Z") => $$"""
        {"id":{{id}},"node_id":"R_{{id}}","name":"{{name}}","full_name":"{{owner}}/{{name}}","private":{{(isPrivate ? "true" : "false")}},
         "owner":{"login":"{{owner}}","id":1,"avatar_url":"https://avatars.githubusercontent.com/u/1?v=4","type":"{{ownerType}}"},
         "html_url":"https://github.com/{{owner}}/{{name}}","description":{{(description is null ? "null" : "\"" + description + "\"")}},"fork":false,
         "url":"https://api.github.com/repos/{{owner}}/{{name}}","created_at":"2020-01-01T00:00:00Z","updated_at":"2026-09-01T00:00:00Z",
         "pushed_at":"{{pushedAt}}","git_url":"git://github.com/{{owner}}/{{name}}.git","ssh_url":"git@github.com:{{owner}}/{{name}}.git",
         "clone_url":"https://github.com/{{owner}}/{{name}}.git","size":108,"stargazers_count":80,"watchers_count":80,"language":"C#",
         "forks_count":9,"archived":false,"disabled":false,"open_issues_count":0,"license":null,"topics":[],"visibility":"{{(isPrivate ? "private" : "public")}}",
         "default_branch":"main","permissions":{"admin":false,"maintain":false,"push":true,"triage":true,"pull":true} }
        """;

    public static string RepositoryPage(params string[] repositories) =>
        $$"""{"total_count":{{repositories.Length}},"repository_selection":"selected","repositories":[{{string.Join(',', repositories)}}]}""";

    public const string PullRequests = """
        {"data":{"repository":{"pullRequests":{"nodes":[
          {"number":42,"title":"Add the clone dialog","url":"https://github.com/octocat/hello/pull/42","isDraft":false,"headRefName":"feature/clone",
           "baseRefName":"main","updatedAt":"2026-10-07T09:30:00Z","author":{"login":"octocat","avatarUrl":"https://avatars.githubusercontent.com/u/583231?v=4"},
           "reviewDecision":"APPROVED","commits":{"nodes":[{"commit":{"statusCheckRollup":{"state":"SUCCESS"}}}]}},
          {"number":41,"title":"Try a new parser","url":"https://github.com/octocat/hello/pull/41","isDraft":true,"headRefName":"parser",
           "baseRefName":"main","updatedAt":"2026-10-05T09:30:00Z","author":null,
           "reviewDecision":"CHANGES_REQUESTED","commits":{"nodes":[{"commit":{"statusCheckRollup":{"state":"ERROR"}}}]}},
          {"number":40,"title":"Docs","url":"https://github.com/octocat/hello/pull/40","isDraft":false,"headRefName":"docs",
           "baseRefName":"main","updatedAt":"2026-10-01T09:30:00Z","author":{"login":"hubot","avatarUrl":null},
           "reviewDecision":null,"commits":{"nodes":[{"commit":{"statusCheckRollup":null}}]}}]}}}}
        """;

    public const string PullRequestsWithoutChecksAccess = """
        {"data":{"repository":{"pullRequests":{"nodes":[
          {"number":7,"title":"Fix","url":"https://github.com/octocat/hello/pull/7","isDraft":false,"headRefName":"fix","baseRefName":"main",
           "updatedAt":"2026-10-07T09:30:00Z","author":{"login":"octocat","avatarUrl":null},"reviewDecision":"REVIEW_REQUIRED",
           "commits":{"nodes":[{"commit":{"statusCheckRollup":null}}]}}]}}},
         "errors":[{"type":"FORBIDDEN","path":["repository","pullRequests","nodes",0,"commits","nodes",0,"commit","statusCheckRollup"],
                    "message":"Resource not accessible by integration"}]}
        """;

    public const string RepositoryNotFound = """
        {"data":{"repository":null},"errors":[{"type":"NOT_FOUND","path":["repository"],"locations":[{"line":2,"column":3}],
          "message":"Could not resolve to a Repository with the name 'octocat/missing'."}]}
        """;

    public const string CreatedPullRequest = """
        {"url":"https://api.github.com/repos/octocat/hello/pulls/43","id":1,"html_url":"https://github.com/octocat/hello/pull/43","number":43,"state":"open",
         "title":"Add the clone dialog","draft":true}
        """;

    public const string PullRequestExists = """
        {"message":"Validation Failed","errors":[{"resource":"PullRequest","code":"custom","message":"A pull request already exists for octocat:feature/clone."}],
         "documentation_url":"https://docs.github.com/rest/pulls/pulls#create-a-pull-request","status":"422"}
        """;

    public const string BadCredentials = """{"message":"Bad credentials","documentation_url":"https://docs.github.com/rest","status":"401"}""";

    public const string RateLimited = """
        {"message":"API rate limit exceeded for user ID 583231.","documentation_url":"https://docs.github.com/rest/overview/rate-limits-for-the-rest-api","status":"403"}
        """;

    public const string DeviceCode = """
        {"device_code":"3584d83530557fdd1f46af8289938c8ef79f9dc5","user_code":"WDJB-MJHT","verification_uri":"https://github.com/login/device",
         "expires_in":900,"interval":5}
        """;

    public const string Pending = """{"error":"authorization_pending","error_description":"The authorization request is still pending.","error_uri":"https://docs.github.com"}""";

    public const string SlowDown = """{"error":"slow_down","error_description":"Too many requests have been made in the same timeframe.","error_uri":"https://docs.github.com","interval":10}""";

    public const string ExpiredToken = """{"error":"expired_token","error_description":"The device_code has expired.","error_uri":"https://docs.github.com"}""";

    public const string AccessDenied = """{"error":"access_denied","error_description":"The user has denied your application access.","error_uri":"https://docs.github.com"}""";

    public const string Tokens = """
        {"access_token":"ghu_16C7e42F292c6912E7710c838347Ae178B4a","expires_in":28800,"refresh_token":"ghr_1B4a2e77838347a7E420ce178F2E7c6912E169246c34E1ccbF66C46812d16D5B1A9Dc86A1498",
         "refresh_token_expires_in":15897600,"scope":"","token_type":"bearer"}
        """;

    public const string BadRefreshToken = """{"error":"bad_refresh_token","error_description":"The refresh token passed is incorrect or expired.","error_uri":"https://docs.github.com"}""";
}
