using System.Net;
using System.Text;
using Runesmith.Sdk.Settings;
using Runesmith.Sdk.Shell;

namespace Runesmith.Plugins.Gitea.Tests;

/// <summary>A request the fake server received, with its body read.</summary>
internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, string? UserAgent, string? ContentType, string Body);

/// <summary>Answers requests from a list of routes, newest first, and records them; nothing reaches the network.</summary>
internal sealed class FakeServer : HttpMessageHandler
{
    private readonly List<(Func<HttpRequestMessage, bool> Matches, Func<HttpRequestMessage, string, HttpResponseMessage> Respond)> routes = [];

    public List<RecordedRequest> Requests { get; } = [];

    /// <summary>Answers a path, or a path and query, with a status and body.</summary>
    public FakeServer On(string pathAndQuery, string body, HttpStatusCode status = HttpStatusCode.OK, Action<HttpResponseMessage>? headers = null) =>
        On(request => request.RequestUri!.PathAndQuery == pathAndQuery || request.RequestUri.AbsolutePath == pathAndQuery, (_, _) =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            headers?.Invoke(response);
            return response;
        });

    public FakeServer On(Func<HttpRequestMessage, bool> matches, Func<HttpRequestMessage, string, HttpResponseMessage> respond)
    {
        routes.Insert(0, (matches, respond));
        return this;
    }

    /// <summary>Fails every request as if the server could not be reached.</summary>
    public bool Unreachable { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Unreachable)
            throw new HttpRequestException("Connection refused");

        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), request.Headers.UserAgent.ToString(),
            request.Content?.Headers.ContentType?.MediaType, body));
        foreach (var (matches, respond) in routes)
        {
            if (matches(request))
                return respond(request, body);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"message":"The target couldn't be found.","errors":[]}""") };
    }
}

/// <summary>Responses recorded from codeberg.org's public API and trimmed, and hand-written ones for what needs a token.</summary>
internal static class Responses
{
    public static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Responses", name));

    public const string User = """
        {"id":4821,"login":"alice","login_name":"","source_id":0,"full_name":"Alice Liddell","email":"alice@noreply.codeberg.org",
         "avatar_url":"https://codeberg.org/avatars/1b0b4ce1d5b7c2f4","html_url":"https://codeberg.org/alice","language":"en-US","is_admin":false,
         "last_login":"2026-10-08T09:12:44+02:00","created":"2023-02-11T10:00:00+01:00","restricted":false,"active":true,"prohibit_login":false,
         "location":"","pronouns":"","website":"","description":"","visibility":"public","followers_count":3,"following_count":1,"starred_repos_count":7,
         "username":"alice"}
        """;

    public const string Organizations = """
        [{"id":901,"name":"wonderland","full_name":"Wonderland","email":"","avatar_url":"https://codeberg.org/avatars/9a","description":"","website":"",
          "location":"","visibility":"public","repo_admin_change_team_access":false,"username":"wonderland"}]
        """;

    public static string Repository(long id, string owner, string name, bool isPrivate = false, string updated = "2026-09-30T08:00:00+02:00") => $$"""
        {"id":{{id}},"owner":{"id":1,"login":"{{owner}}","full_name":"","avatar_url":"https://codeberg.org/avatars/{{owner}}"},"name":"{{name}}",
         "full_name":"{{owner}}/{{name}}","description":"About {{name}}","empty":false,"private":{{(isPrivate ? "true" : "false")}},"fork":false,
         "template":false,"mirror":false,"size":120,"language":"C#","languages_url":"","html_url":"https://codeberg.org/{{owner}}/{{name}}",
         "url":"https://codeberg.org/api/v1/repos/{{owner}}/{{name}}","ssh_url":"ssh://git@codeberg.org/{{owner}}/{{name}}.git",
         "clone_url":"https://codeberg.org/{{owner}}/{{name}}.git","website":"","stars_count":4,"forks_count":0,"watchers_count":1,
         "open_issues_count":0,"open_pr_counter":0,"release_counter":0,"default_branch":"main","archived":false,"created_at":"2024-01-01T00:00:00+01:00",
         "updated_at":"{{updated}}","permissions":{"admin":true,"push":true,"pull":true} }
        """;

    public static string List(params string[] items) => "[" + string.Join(',', items) + "]";

    public const string CreatedPullRequest = """
        {"id":77,"url":"https://codeberg.org/alice/notes/pulls/12","number":12,"user":{"id":4821,"login":"alice","avatar_url":"https://codeberg.org/avatars/1b"},
         "title":"WIP: Add the index","body":"Lists every note.","labels":[],"milestone":null,"assignees":null,"requested_reviewers":[],"state":"open",
         "draft":true,"is_locked":false,"comments":0,"html_url":"https://codeberg.org/alice/notes/pulls/12","mergeable":true,"merged":false,
         "base":{"label":"main","ref":"main","sha":"0d1c","repo_id":5},"head":{"label":"index","ref":"index","sha":"9f3a","repo_id":5},
         "created_at":"2026-10-08T10:00:00+02:00","updated_at":"2026-10-08T10:00:00+02:00"}
        """;

    public const string PullRequestExists = """
        {"message":"pull request already exists for these targets [id: 11, issue_id: 30, head_repo_id: 5, base_repo_id: 5, head_branch: index, base_branch: main]","url":"https://codeberg.org/api/swagger"}
        """;

    public const string TokenScopeMissing = """{"message":"token does not have at least one of required scope(s): [read:user]","url":"https://codeberg.org/api/swagger"}""";

    public const string Unauthorized = """{"message":"token is required","url":"https://codeberg.org/api/swagger"}""";

    public const string Tokens = """
        {"access_token":"eyJhbGciOiJSUzI1NiIsImtpZCI6ImEifQ.eyJ0dCI6MH0.c2lnbmF0dXJl","token_type":"bearer","expires_in":3600,
         "refresh_token":"eyJhbGciOiJSUzI1NiIsImtpZCI6ImEifQ.eyJ0dCI6MX0.cmVmcmVzaA"}
        """;

    public const string InvalidGrant = """{"error":"invalid_grant","error_description":"client is not authorized"}""";
}

/// <summary>A token source with a fixed token that counts how often the server refused it.</summary>
internal sealed class FixedTokens(GiteaToken? token) : IGiteaTokenSource
{
    public int Refusals { get; private set; }

    public Task<GiteaToken?> GetTokenAsync(CancellationToken cancellationToken) => Task.FromResult(token);

    public void OnUnauthorized() => Refusals++;
}

/// <summary>A secret store in memory.</summary>
internal sealed class MemorySecretStore : ISecretStore
{
    public Dictionary<string, string> Secrets { get; } = new(StringComparer.Ordinal);

    public bool IsSystemStore => true;

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(Secrets.GetValueOrDefault(key));

    public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        Secrets[key] = value;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        Secrets.Remove(key);
        return Task.CompletedTask;
    }
}

/// <summary>A clock the test moves by hand.</summary>
internal sealed class ManualClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Settings with fixed values.</summary>
internal sealed class FixedSettings(Dictionary<string, object> values) : ISettingsService
{
    public IReadOnlyList<SettingDefinition> Definitions => [];

    public T Get<T>(string key) => values.TryGetValue(key, out var value) ? (T)value : throw new KeyNotFoundException(key);

    public object? GetValue(string key, SettingScope scope) => values.GetValueOrDefault(key);

    public SettingScope GetEffectiveScope(string key) => SettingScope.User;

    public void Set(string key, object value, SettingScope scope = SettingScope.User) => values[key] = value;

    public void Reset(string key, SettingScope scope = SettingScope.User) => values.Remove(key);

#pragma warning disable CS0067
    public event EventHandler<SettingChangedEventArgs>? Changed;
#pragma warning restore CS0067
}

/// <summary>Notifications the code under test showed.</summary>
internal sealed class RecordedNotifications : INotificationService
{
    public List<(NotificationKind Kind, string Title, string? Message)> Shown { get; } = [];

    public void Notify(NotificationKind kind, string title, string? message = null, string? actionText = null, Action? action = null) => Shown.Add((kind, title, message));

    public Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", bool isDestructive = false) => Task.FromResult(true);

    public Task<int?> ChooseAsync(string title, string message, IReadOnlyList<string> choices) => Task.FromResult<int?>(0);
}

/// <summary>A sign-in prompt that signs in with a fixed token, as if the user pasted it.</summary>
internal sealed class TokenPrompt(string? token) : ISignInPrompt
{
    public int Shown { get; private set; }

    public async Task<bool> SignInAsync(GiteaHost host, CancellationToken cancellationToken)
    {
        Shown++;
        if (token is null)
            return false;

        await host.SignInWithTokenAsync(token, cancellationToken);
        return true;
    }
}

/// <summary>Builds accounts against a fake server, for one flavor.</summary>
internal sealed class Harness
{
    public static readonly DateTimeOffset Start = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    public Harness(Dictionary<string, object>? settings = null)
    {
        Http = new HttpClient(Server);
        Settings = new FixedSettings(settings ?? []);
        Context = new GiteaContext(Subject.Flavor, Http, Secrets, Settings, Notifications, launcher: null, Clock);
    }

    public FakeServer Server { get; } = new();

    public HttpClient Http { get; }

    public MemorySecretStore Secrets { get; } = new();

    public FixedSettings Settings { get; }

    public RecordedNotifications Notifications { get; } = new();

    public ManualClock Clock { get; } = new(Start);

    public GiteaContext Context { get; }

    public static GiteaServer DefaultServer => Subject.Flavor.DefaultServer;

    public GiteaHost Host(string? login = "alice", GiteaServer? server = null, ISignInPrompt? prompt = null)
    {
        server ??= DefaultServer;
        return new GiteaHost(Context, server, new StoredAccount(server.Key, login, null, null), () => prompt);
    }

    public string SecretKey(string login, GiteaServer? server = null) => Context.SecretKey(server ?? DefaultServer, login);
}
