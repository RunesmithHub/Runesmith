using Runesmith.Sdk.Plugins;

namespace Runesmith.Composition;

/// <summary>An assembly of Runesmith's own whose version does not fit the rest of the build.</summary>
/// <param name="Assembly">The assembly whose version is wrong.</param>
/// <param name="Found">The version this build has.</param>
/// <param name="Expected">The version it should have.</param>
/// <param name="RequiredBy">The assembly built against <paramref name="Expected"/>, or null when the plugin API version sets it.</param>
public sealed record HostMismatch(string Assembly, Version Found, Version Expected, string? RequiredBy);

/// <summary>Checks that Runesmith's own assemblies come from one build, before anything relies on them.</summary>
/// <remarks>An incremental build can leave an older copy of an assembly whose own files did not change, such as Runesmith.Text after only
/// its version changed. Plugins built against the newer version then fail, and Runesmith's own code may fail where it uses it.</remarks>
public static class HostIntegrity
{
    /// <summary>Gets the assemblies of this Runesmith whose versions do not fit together; none for a complete build.</summary>
    public static IReadOnlyList<HostMismatch> Check() =>
        Check(HostRuntime.OwnAssemblies(), new Version(RunesmithApi.Version.Major, RunesmithApi.Version.Minor, Math.Max(RunesmithApi.Version.Build, 0)), HostRuntime.VersionOf);

    /// <param name="own">The name and path of each of Runesmith's own assemblies.</param>
    /// <param name="api">The plugin API version, which the assemblies Runesmith shares with plugins carry.</param>
    /// <param name="versionOf">Gets the version of an assembly the host loads, or null when it has none.</param>
    internal static IReadOnlyList<HostMismatch> Check(IEnumerable<(string Name, string Path)> own, Version api, Func<string, Version?> versionOf)
    {
        var mismatches = new List<HostMismatch>();
        foreach (var (name, path) in own.OrderBy(assembly => assembly.Name, StringComparer.Ordinal))
        {
            if (HostRuntime.IsShared(name) && versionOf(name) is { } version && !IsRelease(version, api))
                mismatches.Add(new HostMismatch(name, version, api, null));

            foreach (var (reference, wanted) in References(path))
            {
                if (versionOf(reference) is { } found && found < wanted)
                    mismatches.Add(new HostMismatch(reference, found, wanted, name));
            }
        }

        return mismatches;
    }

    private static bool IsRelease(Version version, Version api) => version.Major == api.Major && version.Minor == api.Minor && version.Build == api.Build;

    private static List<(string Name, Version Version)> References(string path)
    {
        try
        {
            return AssemblyReferences.Read(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An assembly that cannot be read fails where it is loaded, with its own error.
            return [];
        }
    }
}
