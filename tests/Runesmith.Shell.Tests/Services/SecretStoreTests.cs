using Runesmith.Shell.Services.Secrets;

namespace Runesmith.Shell.Tests.Services;

public sealed class SecretStoreTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("runesmith-secrets-").FullName;
    private readonly List<string> log = [];

    private string FilePath => Path.Combine(folder, "secrets.json");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public async Task TheFileKeepsSetsReplacesAndDeletesSecrets()
    {
        var store = new SecretStore(null, new FileSecretBackend(FilePath), log.Add);

        Assert.Null(await store.GetAsync("runesmith.tests/token", Token));
        await store.SetAsync("runesmith.tests/token", "first", Token);
        await store.SetAsync("runesmith.tests/token", "second \"quoted\"\nline", Token);
        await store.SetAsync("runesmith.tests/other", "kept", Token);

        Assert.Equal("second \"quoted\"\nline", await store.GetAsync("runesmith.tests/token", Token));
        var reopened = new SecretStore(null, new FileSecretBackend(FilePath), log.Add);
        Assert.Equal("kept", await reopened.GetAsync("runesmith.tests/other", Token));

        await reopened.DeleteAsync("runesmith.tests/token", Token);
        await reopened.DeleteAsync("runesmith.tests/missing", Token);
        Assert.Null(await reopened.GetAsync("runesmith.tests/token", Token));
        Assert.Equal("kept", await reopened.GetAsync("runesmith.tests/other", Token));
        Assert.False(reopened.IsSystemStore);
    }

    [Fact]
    public async Task TheFileIsReadableByTheUserAlone()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows has no Unix file modes.");
            return;
        }

        var store = new SecretStore(null, new FileSecretBackend(FilePath), log.Add);

        await store.SetAsync("runesmith.tests/token", "secret", Token);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(FilePath));
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public async Task ADamagedFileFailsWithAnIOException()
    {
        await File.WriteAllTextAsync(FilePath, "{ not json", Token);
        var store = new SecretStore(null, new FileSecretBackend(FilePath), log.Add);

        await Assert.ThrowsAsync<IOException>(() => store.GetAsync("runesmith.tests/token", Token));
    }

    [Fact]
    public async Task UsesTheSystemStoreWhenItIsAvailableAndSaysSo()
    {
        var system = new FakeBackend(available: true);
        var store = new SecretStore(system, new FileSecretBackend(FilePath), log.Add);

        await store.SetAsync("runesmith.tests/token", "secret", Token);

        Assert.True(store.IsSystemStore);
        Assert.Equal("secret", system.Secrets["runesmith.tests/token"]);
        Assert.False(File.Exists(FilePath));
        Assert.Contains(log, line => line.Contains("fake store", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FallsBackToTheFileWhenTheSystemStoreIsMissing()
    {
        var store = new SecretStore(new FakeBackend(available: false), new FileSecretBackend(FilePath), log.Add);

        await store.SetAsync("runesmith.tests/token", "secret", Token);

        Assert.False(store.IsSystemStore);
        Assert.True(File.Exists(FilePath));
        Assert.Contains(log, line => line.Contains("No secret store", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DecidesOnceWhichStoreToUse()
    {
        var system = new FakeBackend(available: true);
        var store = new SecretStore(system, new FileSecretBackend(FilePath), log.Add);

        await Task.WhenAll(Enumerable.Range(0, 5).Select(i => store.SetAsync($"runesmith.tests/{i}", "x", Token)));
        _ = store.IsSystemStore;

        Assert.Equal(1, system.AvailabilityChecks);
        Assert.Single(log);
    }

    [Fact]
    public async Task TakesTheFileForTheRestOfTheSessionWhenTheSystemStoreFails()
    {
        var system = new FakeBackend(available: true) { Fails = true };
        var store = new SecretStore(system, new FileSecretBackend(FilePath), log.Add);

        await store.SetAsync("runesmith.tests/token", "secret", Token);

        Assert.False(store.IsSystemStore);
        Assert.Equal("secret", await store.GetAsync("runesmith.tests/token", Token));
        Assert.Equal(1, system.Calls);
        Assert.Contains(log, line => line.Contains("rest of this session", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheSecretServiceKeepsSecretsWhenTheDesktopHasOne()
    {
        var backend = new SecretServiceBackend();
        Assert.SkipUnless(OperatingSystem.IsLinux() && IsOnPath("secret-tool") && await backend.IsAvailableAsync(Token), "No secret-tool or session bus with a Secret Service.");
        var key = $"runesmith.tests/{Guid.NewGuid():N}";
        try
        {
            await backend.SetAsync(key, "secret with spaces\nand a line", Token);
            Assert.Equal("secret with spaces\nand a line", await backend.GetAsync(key, Token));

            await backend.SetAsync(key, "replaced", Token);
            Assert.Equal("replaced", await backend.GetAsync(key, Token));
        }
        finally
        {
            await backend.DeleteAsync(key, Token);
        }

        Assert.Null(await backend.GetAsync(key, Token));
    }

    private static bool IsOnPath(string program) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Any(f => File.Exists(Path.Combine(f, program)));

    private sealed class FakeBackend(bool available) : ISecretBackend
    {
        public Dictionary<string, string> Secrets { get; } = [];

        public bool Fails { get; init; }

        public int AvailabilityChecks { get; private set; }

        public int Calls { get; private set; }

        public string Name => "the fake store";

        public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
        {
            AvailabilityChecks++;
            await Task.Yield();
            return available;
        }

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken)
        {
            Use();
            return Task.FromResult(Secrets.GetValueOrDefault(key));
        }

        public Task SetAsync(string key, string value, CancellationToken cancellationToken)
        {
            Use();
            lock (Secrets)
                Secrets[key] = value;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string key, CancellationToken cancellationToken)
        {
            Use();
            Secrets.Remove(key);
            return Task.CompletedTask;
        }

        private void Use()
        {
            Calls++;
            if (Fails)
                throw new SecretStoreException("The fake store failed.");
        }
    }
}
