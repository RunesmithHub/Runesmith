using System.Globalization;
using System.Net;
using System.Text;

namespace Runesmith.GitHub.GitHub;

/// <summary>Answers like GitHub with recorded, made-up data, for screenshots and tests; see <see cref="GitHubHttp.FakeVariable"/>.</summary>
/// <remarks>The device flow never completes, so the sign-in dialog keeps showing its code.</remarks>
internal sealed class FakeGitHubHandler : HttpMessageHandler
{
    public const string ClientId = "Iv23liFakeRunesmith";

    /// <summary>Gets the session of the made-up account.</summary>
    public static StoredSession Session { get; } = new(GitHubSignInKind.App, "mira-holt", "Mira Holt", "https://avatars.githubusercontent.com/u/1?v=4", "fake-token",
        null, null, null);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? "";
        var json = path switch
        {
            "/login/device/code" => """{"device_code":"fake","user_code":"WDJB-MJHT","verification_uri":"https://github.com/login/device","expires_in":900,"interval":5}""",
            "/login/oauth/access_token" => """{"error":"authorization_pending"}""",
            "/user" => """{"login":"mira-holt","name":"Mira Holt","avatar_url":"https://avatars.githubusercontent.com/u/1?v=4"}""",
            "/user/installations" => Installations(),
            "/graphql" => PullRequests(),
            _ when path.StartsWith("/user/installations/", StringComparison.Ordinal) => InstallationRepositories(path.Split('/')[3]),
            _ when path.EndsWith("/branches", StringComparison.Ordinal) => """[{"name":"main"},{"name":"release/2.4"},{"name":"feature/streaming-export"}]""",
            _ when path.StartsWith("/repos/", StringComparison.Ordinal) && request.Method == HttpMethod.Post => """{"number":219,"html_url":"https://github.com/lumen-labs/aurora/pull/219"}""",
            _ when path.StartsWith("/repos/", StringComparison.Ordinal) => Repository("lumen-labs", "aurora", "Fast, typed configuration for services", "C#", 1284, 2),
            _ => null,
        };

        var response = json is null
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        return Task.FromResult(response);
    }

    private static string Installations() => $$"""
        {"total_count":4,"installations":[
          {"id":1,"account":{{Owner("mira-holt", false)}},"repository_selection":"all","app_slug":"runesmith-editor"},
          {"id":2,"account":{{Owner("lumen-labs", true)}},"repository_selection":"selected","app_slug":"runesmith-editor"},
          {"id":3,"account":{{Owner("northwind-tools", true)}},"repository_selection":"selected","app_slug":"runesmith-editor"},
          {"id":4,"account":{{Owner("harbor-collective", true)}},"repository_selection":"all","app_slug":"runesmith-editor"}]}
        """;

    private static string InstallationRepositories(string installation)
    {
        string[] repositories = installation switch
        {
            "1" =>
            [
                Repository("mira-holt", "dotfiles", "Shell, editor and terminal setup for every machine I use", "Shell", 41, 0, isPrivate: false),
                Repository("mira-holt", "trail-notes", "A static site of hiking notes, built from Markdown", "TypeScript", 12, 3, isPrivate: false),
                Repository("mira-holt", "budget", "Personal finance scripts and monthly reports", "Python", 0, 9, isPrivate: true),
                Repository("mira-holt", "advent-of-code", "Solutions, one folder per year", "Rust", 7, 40, isPrivate: false),
            ],
            "2" =>
            [
                Repository("lumen-labs", "aurora", "Fast, typed configuration for services", "C#", 1284, 0, isPrivate: false),
                Repository("lumen-labs", "aurora-docs", "The documentation site for Aurora", "MDX", 86, 1, isPrivate: false),
                Repository("lumen-labs", "billing-service", "Invoices, plans and payment webhooks", "Java", 0, 2, isPrivate: true),
                Repository("lumen-labs", "infrastructure", "Terraform and deployment pipelines", "HCL", 0, 6, isPrivate: true),
                Repository("lumen-labs", "design-tokens", "Colors, type and spacing shared by every app", "TypeScript", 213, 14, isPrivate: false),
            ],
            "4" =>
            [
                Repository("harbor-collective", "tide-tables", "Tide predictions for every harbor on the coast", "Go", 57, 4, isPrivate: false),
                Repository("harbor-collective", "berth-planner", "Plans berths and crane slots for arriving ships", "C#", 9, 11, isPrivate: true),
                Repository("harbor-collective", "handbook", "How the collective works, from onboarding to releases", "Markdown", 3, 20, isPrivate: false),
            ],
            _ =>
            [
                Repository("northwind-tools", "inventory-api", "Stock levels and orders for the warehouse apps", "Java", 18, 1, isPrivate: true),
                Repository("northwind-tools", "scanner-app", "The handheld scanner app for the warehouse floor", "Kotlin", 4, 5, isPrivate: true),
            ],
        };
        return $$"""{"total_count":{{repositories.Length}},"repositories":[{{string.Join(',', repositories)}}]}""";
    }

    private static string Owner(string login, bool isOrganization) =>
        $$"""{"login":"{{login}}","avatar_url":"https://avatars.githubusercontent.com/{{login}}","type":"{{(isOrganization ? "Organization" : "User")}}"}""";

    private static string Repository(string owner, string name, string description, string language, int stars, int daysAgo, bool isPrivate = false)
    {
        var updated = DateTimeOffset.UtcNow.AddDays(-daysAgo).AddHours(-3).ToString("O", CultureInfo.InvariantCulture);
        var id = Math.Abs(StringComparer.Ordinal.GetHashCode(owner + "/" + name));
        return $$"""
            {"id":{{id}},"name":"{{name}}","full_name":"{{owner}}/{{name}}","owner":{{Owner(owner, owner != "mira-holt")}},"private":{{(isPrivate ? "true" : "false")}},
             "description":"{{description}}","language":"{{language}}","stargazers_count":{{stars}},"updated_at":"{{updated}}","pushed_at":"{{updated}}",
             "clone_url":"https://github.com/{{owner}}/{{name}}.git","html_url":"https://github.com/{{owner}}/{{name}}","default_branch":"main","fork":false,"archived":false}
            """;
    }

    private static string PullRequests()
    {
        static string Item(int number, string title, string author, string branch, bool draft, string? review, string? checks, int hoursAgo)
        {
            var updated = DateTimeOffset.UtcNow.AddHours(-hoursAgo).ToString("O", CultureInfo.InvariantCulture);
            var decision = review is null ? "null" : "\"" + review + "\"";
            var rollup = checks is null ? "null" : "{\"state\":\"" + checks + "\"}";
            return $$"""
                {"number":{{number}},"title":"{{title}}","url":"https://github.com/lumen-labs/aurora/pull/{{number}}","isDraft":{{(draft ? "true" : "false")}},
                 "headRefName":"{{branch}}","baseRefName":"main","updatedAt":"{{updated}}",
                 "author":{"login":"{{author}}","avatarUrl":"https://avatars.githubusercontent.com/{{author}}"},
                 "reviewDecision":{{decision}},"commits":{"nodes":[{"commit":{"statusCheckRollup":{{rollup}}} } ] } }
                """;
        }

        string[] items =
        [
            Item(218, "Stream large exports instead of buffering them", "mira-holt", "feature/streaming-export", false, "APPROVED", "SUCCESS", 2),
            Item(216, "Validate nested sections against the schema", "jonas-ek", "schema-validation", false, "REVIEW_REQUIRED", "PENDING", 5),
            Item(214, "Fix reload losing environment overrides", "priya-raman", "fix/reload-overrides", false, "CHANGES_REQUESTED", "FAILURE", 26),
            Item(211, "Draft: YAML source with comments preserved", "tomas-okafor", "yaml-source", true, null, "SUCCESS", 72),
            Item(207, "Document the binder's naming rules", "lea-schmidt", "docs/binder-names", false, "REVIEW_REQUIRED", null, 140),
        ];
        return "{\"data\":{\"repository\":{\"pullRequests\":{\"nodes\":[" + string.Join(',', items) + "]}}}}";
    }
}
