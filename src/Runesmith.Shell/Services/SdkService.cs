using System.Collections.Concurrent;
using System.Composition;
using System.Globalization;
using Runesmith.Sdk.Sdks;
using Runesmith.Sdk.Settings;

namespace Runesmith.Shell.Services;

/// <summary>The SDKs of every kind the providers know, found once and kept until refreshed, and the user's default of each kind.</summary>
[Export(typeof(ISdkService))]
[Export]
[Shared]
public sealed class SdkService : ISdkService
{
    private const string DefaultKeyPrefix = "sdks.default.";

    private readonly ISettingsService settings;
    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<InstalledSdk>>>> installed = new(StringComparer.Ordinal);

    [ImportingConstructor]
    public SdkService([ImportMany] IEnumerable<ISdkProvider> providers, ISettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Providers = [.. providers.DistinctBy(p => p.Kind, StringComparer.Ordinal)];
        this.settings = settings;
        settings.Changed += (_, e) =>
        {
            if (e.Key.StartsWith(DefaultKeyPrefix, StringComparison.Ordinal))
                Changed?.Invoke(this, EventArgs.Empty);
        };
    }

    public IReadOnlyList<ISdkProvider> Providers { get; }

    public event EventHandler? Changed;

    /// <summary>Gets a comparer that orders SDK versions from oldest to newest.</summary>
    internal static IComparer<string> VersionComparer => VersionOrder.Instance;

    /// <summary>Gets the key of the setting that holds the default SDK of a kind.</summary>
    public static string DefaultKey(string kind) => DefaultKeyPrefix + kind;

    /// <summary>Gets the provider of a kind, or null when no plugin supplies one.</summary>
    public ISdkProvider? Provider(string kind) => Providers.FirstOrDefault(p => p.Kind == kind);

    public Task<IReadOnlyList<InstalledSdk>> GetInstalledAsync(string kind, CancellationToken cancellationToken = default) =>
        Provider(kind) is { } provider
            ? installed.GetOrAdd(kind, _ => new Lazy<Task<IReadOnlyList<InstalledSdk>>>(() => FindAsync(provider))).Value.WaitAsync(cancellationToken)
            : Task.FromResult<IReadOnlyList<InstalledSdk>>([]);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        installed.Clear();
        await Task.WhenAll(Providers.Select(p => GetInstalledAsync(p.Kind, cancellationToken))).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<InstalledSdk?> GetDefaultAsync(string kind, CancellationToken cancellationToken = default)
    {
        var sdks = await GetInstalledAsync(kind, cancellationToken).ConfigureAwait(false);
        var chosen = ReadDefault(kind);
        return sdks.FirstOrDefault(sdk => chosen is not null && Format(sdk) == chosen) ?? (sdks.Count > 0 ? sdks[0] : null);
    }

    /// <summary>Gets whether an SDK is the one the user chose as the default of its kind.</summary>
    public bool IsChosenDefault(InstalledSdk sdk)
    {
        ArgumentNullException.ThrowIfNull(sdk);
        return ReadDefault(sdk.Kind) == Format(sdk);
    }

    /// <summary>Makes an SDK the default of its kind, in the user's settings.</summary>
    public void SetDefault(InstalledSdk sdk)
    {
        ArgumentNullException.ThrowIfNull(sdk);
        settings.Set(DefaultKey(sdk.Kind), Format(sdk));
    }

    // One .NET root holds several SDKs, so the default names both the version and the folder.
    internal static string Format(InstalledSdk sdk) => $"{sdk.Version}|{sdk.Path}";

    private string? ReadDefault(string kind)
    {
        try
        {
            return settings.Get<string>(DefaultKey(kind)) is { Length: > 0 } value ? value : null;
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    private static async Task<IReadOnlyList<InstalledSdk>> FindAsync(ISdkProvider provider)
    {
        try
        {
            var found = await provider.FindInstalledAsync(CancellationToken.None).ConfigureAwait(false);
            return [.. found.OrderByDescending(sdk => sdk.Version, VersionOrder.Instance)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // Numbers by value, and a prerelease such as 11.0.100-rc.1 before its release.
    private sealed class VersionOrder : IComparer<string>
    {
        public static readonly VersionOrder Instance = new();

        public int Compare(string? x, string? y)
        {
            var (a, b) = (Parts(x), Parts(y));
            for (var i = 0; i < Math.Max(a.Numbers.Length, b.Numbers.Length); i++)
            {
                var result = (i < a.Numbers.Length ? a.Numbers[i] : 0).CompareTo(i < b.Numbers.Length ? b.Numbers[i] : 0);
                if (result != 0)
                    return result;
            }

            return a.IsPrerelease == b.IsPrerelease ? string.CompareOrdinal(x, y) : a.IsPrerelease ? -1 : 1;
        }

        private static (long[] Numbers, bool IsPrerelease) Parts(string? version)
        {
            var text = version ?? "";
            var end = text.IndexOfAny(['-', '+']);
            var core = end < 0 ? text : text[..end];
            var numbers = core.Split(['.', '_']).Select(part => long.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0).ToArray();
            return (numbers, end >= 0 && text[end] == '-');
        }
    }
}
