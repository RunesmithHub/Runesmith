using System.Diagnostics.CodeAnalysis;
using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Versioning;

namespace Runesmith.Hub.Links;

/// <summary>A <c>runesmith://</c> link: it opens a plugin's page, optionally at a version, or the updates list, and nothing else.</summary>
/// <remarks>A link names no address, file, hash or publisher, so it cannot point Runesmith anywhere. Anything beyond an id and a version is
/// refused.</remarks>
public abstract record HubLink
{
    /// <summary>The longest link Runesmith reads.</summary>
    public const int MaxLength = 512;

    public const string Scheme = "runesmith";

    private const string PluginPath = "plugin/";
    private const string UpdatesPath = "updates";
    private const string VersionParameter = "version";

    /// <summary>Gets whether text starts like a link, as opposed to a file path on the command line.</summary>
    public static bool LooksLikeLink(string text) =>
        text is not null && text.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads a link.</summary>
    /// <param name="error">Why the link is refused, in a sentence for the user.</param>
    public static bool TryParse(string? text, [NotNullWhen(true)] out HubLink? link, [NotNullWhen(false)] out string? error)
    {
        link = null;
        error = Problem(text, out var parsed);
        link = parsed;
        return error is null;
    }

    private static string? Problem(string? text, out HubLink? link)
    {
        link = null;
        if (string.IsNullOrEmpty(text))
            return Strings.HubLinkEmpty;
        if (text.Length > MaxLength)
            return Strings.HubLinkTooLong(MaxLength);
        if (text.Any(c => c <= ' ' || c > '~'))
            return Strings.HubLinkCharacters;
        if (!text.StartsWith(Scheme + "://", StringComparison.OrdinalIgnoreCase))
            return Strings.HubLinkScheme;
        if (text.Contains('#', StringComparison.Ordinal))
            return Strings.HubLinkFragment;

        var rest = text[(Scheme.Length + 3)..];
        var slash = rest.IndexOf('/', StringComparison.Ordinal);
        var host = slash < 0 ? rest : rest[..slash];
        if (!string.Equals(host, "hub", StringComparison.Ordinal))
            return Strings.HubLinkHost(host);

        var path = slash < 0 ? "" : rest[(slash + 1)..];
        var question = path.IndexOf('?', StringComparison.Ordinal);
        var query = question < 0 ? null : path[(question + 1)..];
        path = question < 0 ? path : path[..question];

        if (path == UpdatesPath)
        {
            if (query is not null)
                return Strings.HubLinkUpdatesParameters;
            link = new UpdatesLink();
            return null;
        }

        if (!path.StartsWith(PluginPath, StringComparison.Ordinal))
            return Strings.HubLinkNoTarget;

        var id = path[PluginPath.Length..];
        if (!PluginId.IsValid(id))
            return Strings.HubLinkBadId(id);

        SemanticVersion? version = null;
        if (query is not null)
        {
            var parameters = query.Split('&');
            var names = parameters.Select(p => p.Split('=', 2)[0]).ToList();
            var others = names.Where(name => name != VersionParameter).Distinct(StringComparer.Ordinal).ToList();
            if (others.Count > 0)
                return Strings.HubLinkParameters(string.Join(", ", others.Select(name => $"\"{name}\"")));
            if (parameters.Length > 1)
                return Strings.HubLinkVersions;

            var value = parameters[0].Length > VersionParameter.Length + 1 ? parameters[0][(VersionParameter.Length + 1)..] : "";
            if (!parameters[0].StartsWith(VersionParameter + "=", StringComparison.Ordinal) || !SemanticVersion.TryParse(value, out version))
                return Strings.HubLinkBadVersion(value);
        }

        link = new PluginLink(id, version);
        return null;
    }
}

/// <summary>A link to a plugin's page.</summary>
/// <param name="Version">The version to select, or null for the newest that can be installed.</param>
public sealed record PluginLink(string PluginId, SemanticVersion? Version) : HubLink
{
    public override string ToString() => $"runesmith://hub/plugin/{PluginId}{(Version is null ? "" : $"?version={Version}")}";
}

/// <summary>A link to the plugin manager's updates.</summary>
public sealed record UpdatesLink : HubLink
{
    public override string ToString() => "runesmith://hub/updates";
}
