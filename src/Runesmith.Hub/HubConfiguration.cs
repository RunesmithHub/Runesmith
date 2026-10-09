namespace Runesmith.Hub;

/// <summary>Which index this build of Runesmith trusts, and where it is served.</summary>
/// <param name="TrustRoot">The root shipped with Runesmith, or null when this build has none, so the hub is not set up.</param>
/// <param name="IndexUrls">The index base URLs to try in order, each ending in <c>/</c>; the policy's mirrors are tried after them.</param>
public sealed record HubConfiguration(ReadOnlyMemory<byte>? TrustRoot, IReadOnlyList<Uri> IndexUrls)
{
    /// <summary>The address of the hub's index.</summary>
    public static readonly Uri DefaultIndexUrl = new("https://runesmithhub.github.io/registry/");

    /// <summary>The file, relative to the application's folder, that holds the shipped root.</summary>
    public const string RootFile = "Hub/root.json";

    /// <summary>The variable that points development builds at another root file.</summary>
    public const string RootVariable = "RUNESMITH_HUB_ROOT";

    /// <summary>The variable that points development builds at another index, such as a local fixture.</summary>
    public const string IndexUrlVariable = "RUNESMITH_HUB_INDEX_URL";

    /// <summary>Gets whether this build has a trust root, without which only bundled and local plugins load.</summary>
    public bool IsSetUp => TrustRoot is not null;

    /// <summary>Reads the shipped root from the application's folder; development builds take <see cref="RootVariable"/> and
    /// <see cref="IndexUrlVariable"/> instead when they are set.</summary>
    public static HubConfiguration Load(string applicationDirectory, Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        var rootPath = Path.Combine(applicationDirectory, RootFile);
        var indexUrl = DefaultIndexUrl;
#if DEBUG
        if (environment(RootVariable) is { Length: > 0 } developmentRoot)
            rootPath = developmentRoot;
        if (environment(IndexUrlVariable) is { Length: > 0 } developmentIndex && Uri.TryCreate(developmentIndex, UriKind.Absolute, out var url))
            indexUrl = url;
#endif
        try
        {
            return File.Exists(rootPath) ? new HubConfiguration(File.ReadAllBytes(rootPath), [indexUrl]) : new HubConfiguration(null, [indexUrl]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new HubConfiguration(null, [indexUrl]);
        }
    }
}
