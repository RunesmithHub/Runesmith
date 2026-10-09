using System.Globalization;

namespace Runesmith.Plugins.Gitea;

/// <summary>A Forgejo or Gitea server, by the address its web pages and API live under, such as <c>https://codeberg.org/</c> or
/// <c>https://example.com/git/</c> for a server under a path.</summary>
internal sealed record GiteaServer
{
    private GiteaServer(Uri baseUri) => BaseUri = baseUri;

    /// <summary>Gets the server's address, always ending with a slash, so the API is at <c>api/v1/</c> under it.</summary>
    public Uri BaseUri { get; }

    /// <summary>Gets the server's host name, in lower case.</summary>
    public string Host => BaseUri.IdnHost;

    /// <summary>Gets the path the server lives under, such as <c>/git</c>, or an empty string at the top of its host.</summary>
    public string PathPrefix => BaseUri.AbsolutePath.TrimEnd('/');

    /// <summary>Gets a short, stable name of the server for ids and keys: the host, with the port when it is not the scheme's default, the
    /// path, and <c>http://</c> in front for servers without HTTPS, such as <c>codeberg.org</c> or <c>git.example.com:3000/forge</c>.</summary>
    public string Key
    {
        get
        {
            var key = BaseUri.IsDefaultPort ? Host : Host + ":" + BaseUri.Port.ToString(CultureInfo.InvariantCulture);
            key += PathPrefix;
            return BaseUri.Scheme == Uri.UriSchemeHttp ? "http://" + key : key;
        }
    }

    /// <summary>Gets the server's API under <c>api/v1/</c>.</summary>
    public Uri ApiUri => new(BaseUri, "api/v1/");

    /// <summary>Gets the page where the user creates access tokens and OAuth2 applications.</summary>
    public Uri ApplicationsUri => new(BaseUri, "user/settings/applications");

    /// <summary>Reads an address the user typed or a setting holds, such as <c>codeberg.org</c>, <c>git.example.com:3000</c> or
    /// <c>https://example.com/git/</c>; HTTPS is assumed when no scheme is given.</summary>
    /// <returns>The server, or null when the text is not a web address.</returns>
    public static GiteaServer? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        text = text.Trim();
        if (!text.Contains("://", StringComparison.Ordinal))
            text = "https://" + text;

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || uri.Host.Length == 0
            || uri.UserInfo.Length > 0)
            return null;

        var builder = new UriBuilder(uri.Scheme, uri.IdnHost, uri.Port, uri.AbsolutePath.TrimEnd('/') + "/");
        return new GiteaServer(builder.Uri);
    }

    /// <summary>Gets a server from an address that is known to be valid, such as a plugin's default server.</summary>
    public static GiteaServer Of(string address) => Parse(address) ?? throw new ArgumentException($"{address} is not a server address.", nameof(address));

    /// <summary>Gets an address on the server from a path below it, such as <c>alice/notes/src/branch/main</c>.</summary>
    public Uri Page(string relative) => new(BaseUri, relative);

    public bool Equals(GiteaServer? other) => other is not null && string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Key);

    public override string ToString() => Key;
}
