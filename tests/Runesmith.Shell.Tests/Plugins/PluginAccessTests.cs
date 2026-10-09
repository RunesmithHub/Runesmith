using Runesmith.Composition;
using Runesmith.Shell.Services;
using Runesmith.Shell.Services.Secrets;

namespace Runesmith.Shell.Tests.Plugins;

public sealed class PluginAccessTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("runesmith-plugin-access-").FullName;
    private readonly List<string> log = [];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public async Task APluginWithoutTheCredentialsCapabilityCannotUseTheSecretStore()
    {
        var store = Secrets(TestCallers.Plugin("acme.todo", "network"));

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.SetAsync("acme.todo/token", "secret", Token));

        Assert.Contains("credentials", exception.Message, StringComparison.Ordinal);
        Assert.Equal(exception.Message, Assert.Single(log));
        Assert.False(File.Exists(Path.Combine(folder, "secrets.json")));
    }

    [Fact]
    public async Task APluginKeepsItsSecretsUnderItsOwnId()
    {
        var store = Secrets(TestCallers.Plugin("acme.todo", "credentials"));

        await store.SetAsync("acme.todo/token", "secret", Token);

        Assert.Equal("secret", await store.GetAsync("acme.todo/token", Token));
    }

    [Theory]
    [InlineData("runesmith.github/session")]
    [InlineData("acme.todo")]
    [InlineData("acme.todo.other/token")]
    public async Task APluginCannotUseAnotherPluginsSecrets(string key)
    {
        await Secrets(null).SetAsync(key, "theirs", Token);
        var store = Secrets(TestCallers.Plugin("acme.todo", "credentials"));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.GetAsync(key, Token));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.DeleteAsync(key, Token));
        Assert.Equal("theirs", await Secrets(null).GetAsync(key, Token));
    }

    [Fact]
    public async Task APluginWithAnOlderManifestDeclaresNothingAndMayUseTheSecretStoreUnderItsId()
    {
        var store = Secrets(TestCallers.Legacy("acme.todo"));

        await store.SetAsync("acme.todo/token", "secret", Token);

        Assert.Equal("secret", await store.GetAsync("acme.todo/token", Token));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.GetAsync("acme.other/token", Token));
    }

    [Fact]
    public void APluginWithoutTheProcessCapabilityCannotUseTheLauncher()
    {
        var launcher = new SystemLauncher(() => TestCallers.Plugin("acme.todo", "network"), log.Add);

        Assert.Throws<UnauthorizedAccessException>(() => launcher.OpenUrl(new Uri("https://example.com")));
        Assert.Throws<UnauthorizedAccessException>(() => launcher.Reveal(folder));
        Assert.Equal(2, log.Count);
        Assert.All(log, line => Assert.Contains("process", line, StringComparison.Ordinal));
    }

    [Fact]
    public void APluginWithTheProcessCapabilityGetsPastTheCheck()
    {
        var launcher = new SystemLauncher(() => TestCallers.Plugin("acme.todo", "process"), log.Add);

        Assert.Throws<ArgumentException>(() => launcher.OpenUrl(new Uri("file:///etc/passwd")));
        Assert.Empty(log);
    }

    [Fact]
    public void EachPluginHasAStorageFolderNamedAfterItsId()
    {
        PluginInfo? caller = TestCallers.Plugin("acme.todo");
        var storage = new PluginStorage(folder, () => caller);

        var todo = storage.GetFolder();
        caller = TestCallers.Plugin("acme.notes");
        var notes = storage.GetFolder();

        Assert.Equal(Path.Combine(folder, "acme.todo"), todo);
        Assert.Equal(Path.Combine(folder, "acme.notes"), notes);
        Assert.True(Directory.Exists(todo));
    }

    [Fact]
    public void RunesmithItselfHasNoPluginStorageFolder() =>
        Assert.Throws<InvalidOperationException>(() => new PluginStorage(folder, () => null).GetFolder());

    private SecretStore Secrets(PluginInfo? caller) => new(null, new FileSecretBackend(Path.Combine(folder, "secrets.json")), log.Add, () => caller);
}
