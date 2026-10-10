namespace Runesmith.Sdk.Plugins;

/// <summary>Facts about the plugin API that plugins and their manifests refer to.</summary>
/// <remarks>The API follows semantic versioning. A plugin's manifest names the range of API versions it works with in <c>runesmithApi</c>,
/// such as <c>^0.1.0</c>; Runesmith loads it when <see cref="Version"/> is in that range and the range's lowest version is at least
/// <see cref="OldestSupported"/>.</remarks>
public static class RunesmithApi
{
    /// <summary>The version of this API.</summary>
    public static Version Version { get; } = new(0, 1, 1);

    /// <summary>The oldest API version plugins can be built against and still run: the start of <see cref="Version"/>'s compatibility
    /// line.</summary>
    public static Version OldestSupported { get; } = new(0, 1, 0);

    /// <summary>Whether a plugin built against API version <paramref name="pluginApiVersion"/> can run against this API: it is at least
    /// <see cref="OldestSupported"/> and not newer than <see cref="Version"/>.</summary>
    public static bool IsCompatible(Version pluginApiVersion)
    {
        ArgumentNullException.ThrowIfNull(pluginApiVersion);
        var version = new Version(pluginApiVersion.Major, pluginApiVersion.Minor, Math.Max(pluginApiVersion.Build, 0));
        return version >= OldestSupported && version <= Version;
    }
}
