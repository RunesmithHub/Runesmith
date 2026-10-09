using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;
using Runesmith.Sdk;

namespace Runesmith.Plugins.Views;

/// <summary>Downloads avatars once and keeps them on disk and in memory. Each plugin exports its own subclass, since parts are matched by
/// type name and the plugins compile this class each.</summary>
internal abstract class AvatarCache : IDisposable
{
    private static readonly TimeSpan Freshness = TimeSpan.FromDays(7);

    private readonly Dictionary<string, Task<Bitmap?>> loaded = new(StringComparer.Ordinal);
    private readonly Lock gate = new();
    private readonly HttpClient http;

    protected AvatarCache()
    {
        http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Runesmith");
    }

    /// <summary>Gets the folder avatars are kept in.</summary>
    public static string Folder => Path.Combine(RunesmithPaths.Cache, "avatars");

    /// <summary>Gets an avatar decoded at about twice <paramref name="size"/> pixels, or null when it cannot be had; failures are quiet.</summary>
    public Task<Bitmap?> GetAsync(string? avatarUrl, int size)
    {
        if (!Uri.TryCreate(avatarUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return Task.FromResult<Bitmap?>(null);

        var pixels = Math.Max(1, size * 2);
        var key = uri.AbsoluteUri + "#" + pixels.ToString(System.Globalization.CultureInfo.InvariantCulture);
        lock (gate)
        {
            if (!loaded.TryGetValue(key, out var task))
            {
                task = Task.Run(() => LoadAsync(uri, pixels));
                loaded[key] = task;
            }

            return task;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the HTTP client.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
            http.Dispose();
    }

    private async Task<Bitmap?> LoadAsync(Uri uri, int pixels)
    {
        var file = Path.Combine(Folder, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)))[..32]);
        try
        {
            if (File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < Freshness)
                return Decode(await File.ReadAllBytesAsync(file).ConfigureAwait(false), pixels);

            using var response = await http.GetAsync(uri).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return File.Exists(file) ? Decode(await File.ReadAllBytesAsync(file).ConfigureAwait(false), pixels) : null;

            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            Directory.CreateDirectory(Folder);
            await File.WriteAllBytesAsync(file, bytes).ConfigureAwait(false);
            return Decode(bytes, pixels);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException
            or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private static Bitmap Decode(byte[] bytes, int pixels)
    {
        using var stream = new MemoryStream(bytes);
        return Bitmap.DecodeToWidth(stream, pixels);
    }
}
