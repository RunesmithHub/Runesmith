namespace Runesmith.Shell.Services.Secrets;

/// <summary>One place secrets can be kept, such as the system's secret store or a file.</summary>
internal interface ISecretBackend
{
    /// <summary>Gets the store's name for the log, such as "the Secret Service".</summary>
    string Name { get; }

    /// <summary>Gets whether the store works on this computer now; asked once.</summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);

    /// <exception cref="SecretStoreException">The store failed.</exception>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken);

    /// <exception cref="SecretStoreException">The store failed.</exception>
    Task SetAsync(string key, string value, CancellationToken cancellationToken);

    /// <exception cref="SecretStoreException">The store failed.</exception>
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}

/// <summary>A secret store failed, such as a command that exited with an error.</summary>
internal sealed class SecretStoreException(string message, Exception? innerException = null) : Exception(message, innerException);
