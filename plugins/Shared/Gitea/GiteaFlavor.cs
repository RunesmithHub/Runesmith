namespace Runesmith.Plugins.Gitea;

/// <summary>What a server answered about itself on the version endpoints.</summary>
internal enum ServerKind
{
    /// <summary>The address answers neither version endpoint, so it is not a Forgejo or Gitea server, or it could not be reached.</summary>
    Unknown,

    /// <summary>A Gitea server: <c>/api/v1/version</c> answers and <c>/api/forgejo/v1/version</c> does not.</summary>
    Gitea,

    /// <summary>A Forgejo server: <c>/api/forgejo/v1/version</c> answers.</summary>
    Forgejo,
}

/// <summary>What sets the Forgejo and Gitea plugins apart over the API they share: names, icon, default server, settings and which servers
/// they accept.</summary>
/// <param name="PluginId">The plugin's id, such as <c>runesmith.forgejo</c>; it starts every secret store key and names the state folder.</param>
/// <param name="IdPrefix">The start of every host's id, such as <c>forgejo</c>.</param>
/// <param name="Name">The service's name, such as "Forgejo".</param>
/// <param name="Icon">The name of the service's icon.</param>
/// <param name="Kind">The kind of server the plugin works with.</param>
/// <param name="DefaultServer">The server offered first, such as Codeberg.</param>
/// <param name="DefaultServerName">The default server's own name, such as "Codeberg".</param>
/// <param name="FakeVariable">The environment variable that turns on made-up answers for tests and screenshots.</param>
internal sealed record GiteaFlavor(
    string PluginId,
    string IdPrefix,
    string Name,
    string Icon,
    ServerKind Kind,
    GiteaServer DefaultServer,
    string DefaultServerName,
    string FakeVariable)
{
    /// <summary>Gets the settings category, which is the service's name.</summary>
    public string SettingsCategory => Name;

    /// <summary>Gets the key of the setting that holds the OAuth2 client ids, one per server.</summary>
    public string ClientIdsSetting => IdPrefix + ".oauthClientIds";

    /// <summary>Gets the fake mode from <see cref="FakeVariable"/>: <c>signed-in</c>, <c>signed-out</c>, or null for real servers.</summary>
    public string? FakeMode => Environment.GetEnvironmentVariable(FakeVariable) is { Length: > 0 } mode ? mode : null;

    /// <summary>Gets the name a server's hosts show: the default server's own name, or the service and the server's address.</summary>
    public string ServerName(GiteaServer server) =>
        server == DefaultServer ? DefaultServerName : $"{Name} ({server.Key})";

    /// <summary>Whether this plugin takes a server of a kind: Forgejo takes Forgejo servers, Gitea takes the servers that are not Forgejo.</summary>
    public bool Accepts(ServerKind kind) => kind == Kind;
}
