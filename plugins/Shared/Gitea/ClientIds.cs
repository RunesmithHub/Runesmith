namespace Runesmith.Plugins.Gitea;

/// <summary>Reads and writes the OAuth2 client ids setting: entries such as <c>codeberg.org=1a2b3c</c>, one per server, separated by commas,
/// semicolons or spaces. An id without a server is for the default server.</summary>
internal static class ClientIds
{
    private static readonly char[] Separators = [',', ';', ' ', '\t', '\r', '\n'];

    /// <summary>Gets the client id for a server from the setting's value, or null when it has none.</summary>
    public static string? Find(string? value, GiteaServer server, GiteaServer defaultServer)
    {
        foreach (var (entryServer, id) in Parse(value, defaultServer))
        {
            if (entryServer == server)
                return id;
        }

        return null;
    }

    /// <summary>Gets the setting's value with a server's client id set, replacing the one it had; an empty id removes it.</summary>
    public static string With(string? value, GiteaServer server, string? clientId, GiteaServer defaultServer)
    {
        var entries = Parse(value, defaultServer).Where(entry => entry.Server != server).ToList();
        if (!string.IsNullOrWhiteSpace(clientId))
            entries.Add((server, clientId.Trim()));
        return string.Join(", ", entries.Select(entry => entry.Server.Key + "=" + entry.Id));
    }

    private static IEnumerable<(GiteaServer Server, string Id)> Parse(string? value, GiteaServer defaultServer)
    {
        foreach (var entry in (value ?? "").Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = entry.LastIndexOf('=');
            if (equals < 0)
            {
                yield return (defaultServer, entry);
                continue;
            }

            if (GiteaServer.Parse(entry[..equals]) is { } server && entry[(equals + 1)..] is { Length: > 0 } id)
                yield return (server, id);
        }
    }
}
