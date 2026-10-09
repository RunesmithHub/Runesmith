using System.Globalization;
using System.Net;
using System.Text;

namespace Runesmith.Plugins.Gitea;

/// <summary>Answers like a Forgejo or Gitea server with made-up data, for screenshots and tests; see <see cref="GiteaFlavor.FakeVariable"/>.
/// It answers for any server address, and the OAuth2 sign-in never completes, so the sign-in dialog keeps waiting for the browser.</summary>
/// <param name="kind">The kind of server it pretends to be.</param>
internal sealed class FakeGiteaHandler(ServerKind kind) : HttpMessageHandler
{
    public const string ClientId = "00000000-fake-4000-8000-runesmith000";

    public const string Login = "mira-holt";

    /// <summary>Gets the made-up accounts of the <c>signed-in</c> mode: two signed in, and one on a self-hosted server whose sign-in expired.</summary>
    public static IReadOnlyList<(StoredAccount Account, StoredTokens? Tokens)> Accounts(GiteaFlavor flavor)
    {
        ArgumentNullException.ThrowIfNull(flavor);
        return
        [
            (new StoredAccount(flavor.DefaultServer.Key, Login, "Mira Holt", null), new StoredTokens(SignInKind.OAuth, "fake-token", null, null)),
            (new StoredAccount("git.lumen-labs.dev", "mira", "Mira Holt", null), new StoredTokens(SignInKind.Token, "fake-token", null, null)),
            (new StoredAccount("forge.example.org:3000", "mholt", null, null), null),
        ];
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? "";
        var origin = request.RequestUri?.GetLeftPart(UriPartial.Authority) ?? "https://codeberg.org";
        var api = path.IndexOf("/api/v1/", StringComparison.Ordinal) is var at and >= 0 ? path[(at + "/api/v1/".Length)..] : null;
        var json = path switch
        {
            _ when path.EndsWith("/api/forgejo/v1/version", StringComparison.Ordinal) => kind == ServerKind.Forgejo ? """{"version":"13.0.2+gitea-1.22.0"}""" : null,
            _ when path.EndsWith("/login/oauth/access_token", StringComparison.Ordinal) => """{"error":"invalid_grant","error_description":"made-up server"}""",
            _ => api switch
            {
                "version" => kind == ServerKind.Forgejo ? """{"version":"13.0.2+gitea-1.22.0"}""" : """{"version":"1.24.6"}""",
                "user" => $$"""{"id":1,"login":"{{Login}}","full_name":"Mira Holt","avatar_url":"","html_url":""}""",
                "user/orgs" => """[{"id":2,"name":"lumen-labs","full_name":"Lumen Labs","avatar_url":""},{"id":3,"name":"northwind-tools","full_name":"Northwind Tools","avatar_url":""}]""",
                "user/repos" => Repositories(origin, Login),
                "orgs/lumen-labs/repos" => Repositories(origin, "lumen-labs"),
                "orgs/northwind-tools/repos" => Repositories(origin, "northwind-tools"),
                not null when api.EndsWith("/pulls", StringComparison.Ordinal) && request.Method == HttpMethod.Post =>
                    PullRequest(origin, 219, "Stream large exports instead of buffering them", Login, "feature/streaming-export", 0),
                not null when api.EndsWith("/pulls", StringComparison.Ordinal) => PullRequests(origin),
                not null when api.EndsWith("/reviews", StringComparison.Ordinal) => Reviews(api),
                not null when api.EndsWith("/status", StringComparison.Ordinal) => Status(api),
                not null when api.StartsWith("repos/", StringComparison.Ordinal) => Repository(origin, "lumen-labs", "aurora", "Fast, typed configuration for services", "C#", 1284, 2),
                _ => null,
            },
        };

        var response = json is null
            ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("Not found.") }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        return Task.FromResult(response);
    }

    private static string Repositories(string origin, string owner)
    {
        string[] repositories = owner switch
        {
            Login =>
            [
                Repository(origin, Login, "dotfiles", "Shell, editor and terminal setup for every machine I use", "Shell", 41, 0),
                Repository(origin, Login, "trail-notes", "A static site of hiking notes, built from Markdown", "TypeScript", 12, 3),
                Repository(origin, Login, "budget", "Personal finance scripts and monthly reports", "Python", 0, 9, isPrivate: true),
            ],
            "lumen-labs" =>
            [
                Repository(origin, "lumen-labs", "aurora", "Fast, typed configuration for services", "C#", 1284, 0),
                Repository(origin, "lumen-labs", "aurora-docs", "The documentation site for Aurora", "MDX", 86, 1),
                Repository(origin, "lumen-labs", "billing-service", "Invoices, plans and payment webhooks", "Java", 0, 2, isPrivate: true),
                Repository(origin, "lumen-labs", "design-tokens", "Colors, type and spacing shared by every app", "TypeScript", 213, 14),
            ],
            _ =>
            [
                Repository(origin, "northwind-tools", "inventory-api", "Stock levels and orders for the warehouse apps", "Java", 18, 1, isPrivate: true),
            ],
        };
        return "[" + string.Join(',', repositories) + "]";
    }

    private static string Repository(string origin, string owner, string name, string description, string language, int stars, int daysAgo, bool isPrivate = false)
    {
        var updated = DateTimeOffset.UtcNow.AddDays(-daysAgo).AddHours(-3).ToString("O", CultureInfo.InvariantCulture);
        var id = Math.Abs(StringComparer.Ordinal.GetHashCode(owner + "/" + name));
        return $$"""
            {"id":{{id}},"owner":{"login":"{{owner}}","avatar_url":""},"name":"{{name}}","full_name":"{{owner}}/{{name}}","description":"{{description}}",
             "private":{{(isPrivate ? "true" : "false")}},"fork":false,"archived":false,"html_url":"{{origin}}/{{owner}}/{{name}}",
             "clone_url":"{{origin}}/{{owner}}/{{name}}.git","default_branch":"main","language":"{{language}}","stars_count":{{stars}},"updated_at":"{{updated}}"}
            """;
    }

    private static string PullRequest(string origin, int number, string title, string author, string branch, int hoursAgo) =>
        $$"""
        {"id":{{number}},"number":{{number}},"title":"{{title}}","html_url":"{{origin}}/lumen-labs/aurora/pulls/{{number}}","state":"open",
         "draft":{{(title.StartsWith("WIP:", StringComparison.Ordinal) ? "true" : "false")}},"user":{"login":"{{author}}","avatar_url":""},
         "head":{"ref":"{{branch}}","sha":"{{number:x40}}","label":"{{branch}}"},"base":{"ref":"main","sha":null,"label":"main"},"requested_reviewers":[],
         "updated_at":"{{DateTimeOffset.UtcNow.AddHours(-hoursAgo).ToString("O", CultureInfo.InvariantCulture)}}"}
        """;

    private static string PullRequests(string origin) => "[" + string.Join(',',
        PullRequest(origin, 218, "Stream large exports instead of buffering them", Login, "feature/streaming-export", 2),
        PullRequest(origin, 216, "Validate nested sections against the schema", "jonas-ek", "schema-validation", 5),
        PullRequest(origin, 214, "Fix reload losing environment overrides", "priya-raman", "fix/reload-overrides", 26),
        PullRequest(origin, 211, "WIP: YAML source with comments preserved", "tomas-okafor", "yaml-source", 72)) + "]";

    private static string Reviews(string api) => Number(api) switch
    {
        218 => """[{"id":1,"user":{"login":"jonas-ek"},"state":"APPROVED","stale":false,"dismissed":false}]""",
        214 => """[{"id":2,"user":{"login":"mira-holt"},"state":"REQUEST_CHANGES","stale":false,"dismissed":false}]""",
        216 => """[{"id":3,"user":{"login":"mira-holt"},"state":"REQUEST_REVIEW","stale":false,"dismissed":false}]""",
        _ => "[]",
    };

    private static string Status(string api)
    {
        var sha = api.Split('/')[^2];
        var number = int.TryParse(sha.TrimStart('0'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) ? value : 0;
        var state = number switch { 218 => "success", 216 => "pending", 214 => "failure", _ => null };
        return state is null ? """{"state":"","total_count":0}""" : $$"""{"state":"{{state}}","total_count":3}""";
    }

    private static int Number(string api) =>
        int.TryParse(api.Split('/')[^2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 0;
}
