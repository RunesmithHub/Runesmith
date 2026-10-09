namespace Runesmith.Shell.Services.Secrets;

/// <summary>Keeps secrets in the Secret Service of the desktop session, such as GNOME Keyring or KWallet, through <c>secret-tool</c>.</summary>
/// <remarks>Each secret has the attributes application=runesmith and key=<c>key</c>. The secret goes through standard input, never the command
/// line.</remarks>
internal sealed class SecretServiceBackend : ISecretBackend
{
    private const string Program = "secret-tool";
    private const string ProbeKey = "runesmith/probe";

    public string Name => "the Secret Service (secret-tool)";

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")))
            return false;

        try
        {
            var result = await SecretProcess.RunAsync(Program, Attributes("lookup", ProbeKey), null, cancellationToken).ConfigureAwait(false);
            return IsSuccess(result);
        }
        catch (SecretStoreException)
        {
            return false;
        }
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        var result = await SecretProcess.RunAsync(Program, Attributes("lookup", key), null, cancellationToken).ConfigureAwait(false);
        Check(result, "lookup");
        return result.ExitCode == 0 ? result.Output : null;
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        string[] arguments = ["store", "--label", $"Runesmith: {key}", .. Attributes(null, key)];
        var result = await SecretProcess.RunAsync(Program, arguments, value, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new SecretStoreException($"secret-tool store failed: {result.Error.Trim()}");
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken) =>
        Check(await SecretProcess.RunAsync(Program, Attributes("clear", key), null, cancellationToken).ConfigureAwait(false), "clear");

    private static IEnumerable<string> Attributes(string? command, string key)
    {
        if (command is not null)
            yield return command;
        yield return "application";
        yield return "runesmith";
        yield return "key";
        yield return key;
    }

    // secret-tool exits with 1 and says nothing when no secret matches, and with 1 and a message when the service fails.
    private static bool IsSuccess(SecretProcess.Result result) => result.ExitCode == 0 || result.ExitCode == 1 && string.IsNullOrWhiteSpace(result.Error);

    private static void Check(SecretProcess.Result result, string command)
    {
        if (!IsSuccess(result))
            throw new SecretStoreException($"secret-tool {command} failed: {result.Error.Trim()}");
    }
}
