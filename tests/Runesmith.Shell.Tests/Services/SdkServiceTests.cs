using Runesmith.Sdk.Sdks;
using Runesmith.Sdk.Settings;
using Runesmith.Shell.Services;

namespace Runesmith.Shell.Tests.Services;

public sealed class SdkServiceTests
{
    private static readonly InstalledSdk Old = new("dotnet", "9.0.318", "/usr/lib/dotnet");
    private static readonly InstalledSdk Newest = new("dotnet", "10.0.401", "/sdks/dotnet") { Source = SdkSource.Managed };
    private static readonly InstalledSdk Preview = new("dotnet", "10.0.100-rc.2.25502.107", "/sdks/dotnet") { Source = SdkSource.Managed };

    private readonly FakeProvider dotnet = new("dotnet", Old, Preview, Newest);
    private readonly FakeSettings settings = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ListsSdksNewestFirstAndFindsThemOnceUntilRefreshed()
    {
        var service = Service();
        var changes = 0;
        service.Changed += (_, _) => changes++;

        Assert.Equal([Newest, Preview, Old], await service.GetInstalledAsync("dotnet", Token));
        await service.GetInstalledAsync("dotnet", Token);
        Assert.Equal(1, dotnet.Searches);

        await service.RefreshAsync(Token);
        await service.GetInstalledAsync("dotnet", Token);
        Assert.Equal(2, dotnet.Searches);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task KnowsNothingOfKindsWithoutAProvider()
    {
        var service = Service();

        Assert.Empty(await service.GetInstalledAsync("go", Token));
        Assert.Null(await service.GetDefaultAsync("go", Token));
    }

    [Fact]
    public async Task UsesTheChosenDefaultAndOtherwiseTheNewest()
    {
        var service = Service();
        var changes = 0;
        service.Changed += (_, _) => changes++;

        Assert.Equal(Newest, await service.GetDefaultAsync("dotnet", Token));

        service.SetDefault(Old);
        Assert.Equal("9.0.318|/usr/lib/dotnet", settings.Values[SdkService.DefaultKey("dotnet")]);
        Assert.Equal(Old, await service.GetDefaultAsync("dotnet", Token));
        Assert.True(service.IsChosenDefault(Old));
        Assert.Equal(1, changes);

        settings.Set(SdkService.DefaultKey("dotnet"), "8.0.100|/gone");
        Assert.Equal(Newest, await service.GetDefaultAsync("dotnet", Token));
    }

    [Fact]
    public void RegistersADefaultSettingPerKind()
    {
        var definition = Assert.Single(new SdkSettings([dotnet, new FakeProvider("dotnet")]).Settings);

        Assert.Equal(("sdks.default.dotnet", "SDKs", ""), (definition.Key, definition.Category, (string)definition.DefaultValue));
    }

    private SdkService Service() => new([dotnet], settings);

    private sealed class FakeProvider(string kind, params InstalledSdk[] installed) : ISdkProvider
    {
        public int Searches { get; private set; }

        public string Kind => kind;

        public string Name => kind;

        public Task<IReadOnlyList<InstalledSdk>> FindInstalledAsync(CancellationToken cancellationToken)
        {
            Searches++;
            return Task.FromResult<IReadOnlyList<InstalledSdk>>(installed);
        }

        public Task<IReadOnlyList<SdkRelease>> GetReleasesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<InstalledSdk> InstallAsync(SdkRelease release, IProgress<SdkInstallProgress> progress, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task UninstallAsync(InstalledSdk sdk, CancellationToken cancellationToken) => throw new NotSupportedException();

        public SdkRelease? FindUpdate(InstalledSdk sdk, IReadOnlyList<SdkRelease> releases) => null;
    }

    internal sealed class FakeSettings : ISettingsService
    {
        public Dictionary<string, object> Values { get; } = new(StringComparer.Ordinal);

        public IReadOnlyList<SettingDefinition> Definitions { get; } = [new(SdkService.DefaultKey("dotnet"), "Default .NET SDK", "SDKs", "")];

        public event EventHandler<SettingChangedEventArgs>? Changed;

        public T Get<T>(string key) => Definitions.Any(d => d.Key == key)
            ? (T)(Values.TryGetValue(key, out var value) ? value : Definitions.First(d => d.Key == key).DefaultValue)
            : throw new KeyNotFoundException(key);

        public object? GetValue(string key, SettingScope scope) => Values.GetValueOrDefault(key);

        public SettingScope GetEffectiveScope(string key) => Values.ContainsKey(key) ? SettingScope.User : SettingScope.Default;

        public void Set(string key, object value, SettingScope scope = SettingScope.User)
        {
            Values[key] = value;
            Changed?.Invoke(this, new SettingChangedEventArgs(key));
        }

        public void Reset(string key, SettingScope scope = SettingScope.User)
        {
            Values.Remove(key);
            Changed?.Invoke(this, new SettingChangedEventArgs(key));
        }
    }
}
