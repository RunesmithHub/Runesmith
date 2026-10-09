using System.Runtime.InteropServices;
using Runesmith.Languages.Java.Jdk;
using Runesmith.Sdk;

namespace Runesmith.Java.Sdks;

/// <summary>Where the JDK provider installs, caches and looks for JDKs, and which platform it downloads for.</summary>
/// <param name="ManagedFolder">The folder Runesmith installs JDKs into, one subfolder each.</param>
/// <param name="CacheFolder">The folder the release metadata is kept in.</param>
/// <param name="Os">The system as foojay names it: <c>linux</c>, <c>macos</c> or <c>windows</c>.</param>
/// <param name="Architectures">The processors as foojay names them, such as <c>x64</c> or <c>aarch64</c>, the machine's own first.</param>
internal sealed record JdkSdkEnvironment(string ManagedFolder, string CacheFolder, string Os, IReadOnlyList<string> Architectures)
{
    /// <summary>Gets whether this Linux uses musl instead of glibc.</summary>
    public bool Musl { get; init; }

    /// <summary>Gets where else to look for JDKs; Runesmith's own folder is added to it.</summary>
    public JdkSearchOptions Search { get; init; } = new();

    /// <summary>Gets the environment of this machine.</summary>
    public static JdkSdkEnvironment Current()
    {
        var os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
        var native = RuntimeInformation.OSArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.Arm64 => "aarch64",
            System.Runtime.InteropServices.Architecture.X86 => "x86",
            System.Runtime.InteropServices.Architecture.Arm => "arm",
            _ => "x64",
        };
        // macOS and Windows on ARM also run x64 programs.
        string[] architectures = native == "aarch64" && os != "linux" ? [native, "x64"] : [native];
        return new JdkSdkEnvironment(ManagedFolderPath, Path.Combine(RunesmithPaths.Cache, "sdks", JdkSdkProvider.SdkKind), os, architectures)
        {
            Musl = RuntimeInformation.RuntimeIdentifier.Contains("musl", StringComparison.Ordinal),
        };
    }

    /// <summary>Gets the folder Runesmith installs JDKs into.</summary>
    public static string ManagedFolderPath => Path.Combine(RunesmithPaths.Sdks, JdkSdkProvider.SdkKind);

    /// <summary>Gets the name Runesmith shows for a processor: <c>x64</c> or <c>arm64</c> for the names foojay and the <c>release</c> file use.</summary>
    public static string Architecture(string name) => name.ToLowerInvariant() switch
    {
        "x64" or "amd64" or "x86_64" or "x86-64" => "x64",
        "aarch64" or "arm64" => "arm64",
        "x86" or "i386" or "i586" or "i686" or "x32" => "x86",
        var other => other,
    };
}
