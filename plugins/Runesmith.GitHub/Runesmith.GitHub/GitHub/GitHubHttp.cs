using System.Composition;

namespace Runesmith.GitHub.GitHub;

/// <summary>The one HTTP client the plugin talks to GitHub with.</summary>
/// <remarks>Setting the <c>RUNESMITH_GITHUB_FAKE</c> environment variable to <c>signed-in</c> or <c>signed-out</c> swaps GitHub for recorded
/// answers and a made-up account, for screenshots and tests; nothing reaches the network then.</remarks>
[Export]
[Shared]
internal sealed class GitHubHttp : IDisposable
{
    public const string FakeVariable = "RUNESMITH_GITHUB_FAKE";

    public GitHubHttp()
        : this(FakeMode is null ? new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) } : new FakeGitHubHandler())
    {
    }

    public GitHubHttp(HttpMessageHandler handler) => Client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>Gets the fake mode from <see cref="FakeVariable"/>: <c>signed-in</c>, <c>signed-out</c>, or null for the real GitHub.</summary>
    public static string? FakeMode { get; } = Environment.GetEnvironmentVariable(FakeVariable) is { Length: > 0 } mode ? mode : null;

    public HttpClient Client { get; }

    public void Dispose() => Client.Dispose();
}
