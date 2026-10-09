using System.Net;
using System.Text;
using Runesmith.GitHub.GitHub;
using Runesmith.Sdk.Shell;

namespace Runesmith.GitHub.Tests.GitHub;

/// <summary>A request the fake server received, with its body read.</summary>
internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, string? UserAgent, string? ApiVersion, string? Accept, string Body);

/// <summary>Answers requests from a list of routes, in order, and records them; nothing reaches the network.</summary>
internal sealed class FakeServer : HttpMessageHandler
{
    private readonly List<(Func<HttpRequestMessage, bool> Matches, Func<HttpResponseMessage> Respond)> routes = [];

    public List<RecordedRequest> Requests { get; } = [];

    /// <summary>Answers requests to a path, and query when given, with a status and body; later routes for the same request win.</summary>
    public FakeServer On(string pathAndQuery, string body, HttpStatusCode status = HttpStatusCode.OK, Action<HttpResponseMessage>? headers = null) =>
        On(request => request.RequestUri!.PathAndQuery == pathAndQuery || request.RequestUri.AbsolutePath == pathAndQuery, () =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            headers?.Invoke(response);
            return response;
        });

    public FakeServer On(Func<HttpRequestMessage, bool> matches, Func<HttpResponseMessage> respond)
    {
        routes.Insert(0, (matches, respond));
        return this;
    }

    /// <summary>Answers a path with each body in turn, repeating the last one.</summary>
    public FakeServer Sequence(string path, params string[] bodies)
    {
        var index = 0;
        return On(request => request.RequestUri!.AbsolutePath == path,
            () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(bodies[Math.Min(index++, bodies.Length - 1)], Encoding.UTF8, "application/json") });
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), request.Headers.UserAgent.ToString(),
            request.Headers.TryGetValues("X-GitHub-Api-Version", out var versions) ? versions.Single() : null, request.Headers.Accept.ToString(), body));
        foreach (var (matches, respond) in routes)
        {
            if (matches(request))
                return respond();
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"message":"Not Found"}""") };
    }
}

/// <summary>A token source with a fixed token that counts how often GitHub refused it.</summary>
internal sealed class FixedTokens(string? token) : IGitHubTokenSource
{
    public int Refusals { get; private set; }

    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => Task.FromResult(token);

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
internal sealed class FixedSettings(Dictionary<string, object> values) : Runesmith.Sdk.Settings.ISettingsService
{
    public IReadOnlyList<Runesmith.Sdk.Settings.SettingDefinition> Definitions => [];

    public T Get<T>(string key) => values.TryGetValue(key, out var value) ? (T)value : throw new KeyNotFoundException(key);

    public object? GetValue(string key, Runesmith.Sdk.Settings.SettingScope scope) => values.GetValueOrDefault(key);

    public Runesmith.Sdk.Settings.SettingScope GetEffectiveScope(string key) => Runesmith.Sdk.Settings.SettingScope.User;

    public void Set(string key, object value, Runesmith.Sdk.Settings.SettingScope scope = Runesmith.Sdk.Settings.SettingScope.User) => values[key] = value;

    public void Reset(string key, Runesmith.Sdk.Settings.SettingScope scope = Runesmith.Sdk.Settings.SettingScope.User) => values.Remove(key);

#pragma warning disable CS0067
    public event EventHandler<Runesmith.Sdk.Settings.SettingChangedEventArgs>? Changed;
#pragma warning restore CS0067
}
