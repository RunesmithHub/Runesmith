using System.Globalization;
using System.Text;

namespace Runesmith.Shell.Services.Secrets;

/// <summary>Keeps secrets in the login Keychain as generic passwords of the service <c>runesmith</c>, through <c>security</c>.</summary>
internal sealed class KeychainBackend : ISecretBackend
{
    private const string Program = "/usr/bin/security";
    private const string Service = "runesmith";
    private const int NotFound = 44;

    public string Name => "the Keychain";

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(File.Exists(Program));

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        var result = await SecretProcess.RunAsync(Program, ["find-generic-password", "-s", Service, "-a", key, "-w"], null, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode == NotFound)
            return null;
        if (result.ExitCode != 0)
            throw new SecretStoreException($"security find-generic-password failed: {result.Error.Trim()}");

        // The password comes back with a line break after it.
        return result.Output.EndsWith('\n') ? result.Output[..^1] : result.Output;
    }

    // security's -w takes the password as an argument, which other processes can read; in interactive mode the command comes on stdin instead.
    public async Task SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        if (key.Any(c => c is '"' or '\\' || char.IsControl(c)))
            throw new ArgumentException("A Keychain key cannot contain quotes, backslashes or control characters.", nameof(key));

        var hex = Convert.ToHexString(Encoding.UTF8.GetBytes(value));
        var command = string.Create(CultureInfo.InvariantCulture, $"add-generic-password -U -s {Service} -a \"{key}\" -X {hex}\n");
        var result = await SecretProcess.RunAsync(Program, ["-i"], command, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || !string.IsNullOrWhiteSpace(result.Error))
            throw new SecretStoreException($"security add-generic-password failed: {result.Error.Trim()}");
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        var result = await SecretProcess.RunAsync(Program, ["delete-generic-password", "-s", Service, "-a", key], null, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode is not (0 or NotFound))
            throw new SecretStoreException($"security delete-generic-password failed: {result.Error.Trim()}");
    }
}
