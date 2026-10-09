using System.Composition;
using Runesmith.Composition;
using Runesmith.Sdk;
using Runesmith.Sdk.Shell;
using RunesmithHub.Protocol;

namespace Runesmith.Shell.Services.Secrets;

/// <summary>Keeps plugins' secrets in the system's secret store, or in a file only the user can read where there is none, each plugin's
/// under keys that start with its id.</summary>
/// <remarks>Which store is used is decided on first use and written to the Secrets output channel. When the system's store fails later, such
/// as when its service stops, the file takes over for the rest of the session.</remarks>
[Export(typeof(ISecretStore))]
[Shared]
public sealed class SecretStore : ISecretStore
{
    /// <summary>The name of the output channel the store writes to.</summary>
    public const string ChannelName = "Secrets";

    private readonly ISecretBackend? system;
    private readonly ISecretBackend file;
    private readonly Action<string> log;
    private readonly Lazy<Task<ISecretBackend>> chosen;
    private readonly Func<PluginInfo?> caller;
    private volatile ISecretBackend? current;

    [ImportingConstructor]
    public SecretStore(IOutputService output)
        : this(SystemBackend(), new FileSecretBackend(Path.Combine(RunesmithPaths.Config, "secrets.json")), line => output.GetChannel(ChannelName).AppendLine(line))
    {
    }

    internal SecretStore(ISecretBackend? system, ISecretBackend file, Action<string> log, Func<PluginInfo?>? caller = null)
    {
        this.caller = caller ?? PluginCallers.Current;
        this.system = system;
        this.file = file;
        this.log = log;
        chosen = new Lazy<Task<ISecretBackend>>(ChooseAsync);
    }

    /// <remarks>Decides which store to use when nothing has asked before, which can take a moment.</remarks>
    public bool IsSystemStore => !ReferenceEquals(Current().GetAwaiter().GetResult(), file);

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        CheckCaller(key);
        return await RunAsync(backend => backend.GetAsync(key, cancellationToken)).ConfigureAwait(false);
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);
        CheckCaller(key);
        await RunAsync(async backend =>
        {
            await backend.SetAsync(key, value, cancellationToken).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        CheckCaller(key);
        await RunAsync(async backend =>
        {
            await backend.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
    }

    private void CheckCaller(string key)
    {
        if (caller() is not { } plugin)
            return;

        PluginAccess.Demand(plugin, Capabilities.Credentials, "secret store", log);
        if (!key.StartsWith(plugin.Manifest.Id + "/", StringComparison.Ordinal))
        {
            var message = $"{plugin.Manifest.Name} ({plugin.Manifest.Id}) cannot use the secret {key}: a plugin's secrets start with its id and a slash.";
            log(message);
            throw new UnauthorizedAccessException(message);
        }
    }

    private static ISecretBackend? SystemBackend() =>
        OperatingSystem.IsWindows() ? new CredentialManagerBackend()
        : OperatingSystem.IsMacOS() ? new KeychainBackend()
        : OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD() ? new SecretServiceBackend()
        : null;

    private Task<ISecretBackend> Current() => current is { } backend ? Task.FromResult(backend) : chosen.Value;

    private async Task<ISecretBackend> ChooseAsync()
    {
        var backend = file;
        if (system is not null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                if (await system.IsAvailableAsync(timeout.Token).ConfigureAwait(false))
                    backend = system;
            }
            catch (OperationCanceledException)
            {
                log($"{system.Name} did not answer in time.");
            }
        }

        log(ReferenceEquals(backend, file)
            ? $"No secret store of the system is available{(system is null ? "" : $" ({system.Name})")}; secrets are kept in {file.Name}, which only you can read."
            : $"Secrets are kept in {backend.Name}.");
        current = backend;
        return backend;
    }

    private async Task<T> RunAsync<T>(Func<ISecretBackend, Task<T>> operation)
    {
        var backend = await Current().ConfigureAwait(false);
        try
        {
            return await operation(backend).ConfigureAwait(false);
        }
        catch (SecretStoreException exception) when (!ReferenceEquals(backend, file))
        {
            log($"{exception.Message} Secrets are kept in {file.Name} for the rest of this session.");
            current = file;
            return await RunFileAsync(operation).ConfigureAwait(false);
        }
        catch (SecretStoreException exception)
        {
            log(exception.Message);
            throw new IOException(exception.Message, exception);
        }
    }

    private async Task<T> RunFileAsync<T>(Func<ISecretBackend, Task<T>> operation)
    {
        try
        {
            return await operation(file).ConfigureAwait(false);
        }
        catch (SecretStoreException exception)
        {
            log(exception.Message);
            throw new IOException(exception.Message, exception);
        }
    }
}
