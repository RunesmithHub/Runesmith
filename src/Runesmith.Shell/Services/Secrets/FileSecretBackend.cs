using System.Text.Json;

namespace Runesmith.Shell.Services.Secrets;

/// <summary>Keeps secrets in a JSON file that only the user can read, for computers without a secret store.</summary>
internal sealed class FileSecretBackend(string path) : ISecretBackend
{
    private readonly Lock gate = new();

    public string Name => $"the file {path}";

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        lock (gate)
            return Task.FromResult(Read().GetValueOrDefault(key));
    }

    public Task SetAsync(string key, string value, CancellationToken cancellationToken) => ChangeAsync(secrets => secrets[key] = value);

    public Task DeleteAsync(string key, CancellationToken cancellationToken) => ChangeAsync(secrets => secrets.Remove(key));

    private Task ChangeAsync(Action<Dictionary<string, string>> change)
    {
        lock (gate)
        {
            var secrets = Read();
            change(secrets);
            Write(secrets);
        }

        return Task.CompletedTask;
    }

    private Dictionary<string, string> Read()
    {
        try
        {
            if (!File.Exists(path))
                return new(StringComparer.Ordinal);

            using var stream = File.OpenRead(path);
            return new(JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [], StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new SecretStoreException($"{path} could not be read: {exception.Message}", exception);
        }
    }

    // The file is created readable by the user alone, so the secrets are never readable by others, not even for a moment.
    private void Write(Dictionary<string, string> secrets)
    {
        var temporary = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }

            using (var stream = new FileStream(temporary, options))
                JsonSerializer.Serialize(stream, new SortedDictionary<string, string>(secrets, StringComparer.Ordinal));
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SecretStoreException($"{path} could not be written: {exception.Message}", exception);
        }
    }
}
