namespace Runesmith.Sdk.Shell;

/// <summary>Keeps secrets such as access tokens in the system's secret store: the Secret Service on Linux, the Keychain on macOS and the
/// Credential Manager on Windows.</summary>
/// <remarks>Each plugin has its own keys: a plugin's keys start with its id and a slash, such as <c>runesmith.github/token</c>, and using any
/// other key fails. A plugin needs the <c>credentials</c> capability in its manifest to use the store.</remarks>
public interface ISecretStore
{
    /// <summary>Gets whether secrets are kept in the system's secret store; when false they are kept in a file only the user can read.</summary>
    bool IsSystemStore { get; }

    /// <summary>Gets a secret, or null when there is none.</summary>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Stores a secret, replacing the one with the same key.</summary>
    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);

    /// <summary>Deletes a secret; deleting one that does not exist does nothing.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
