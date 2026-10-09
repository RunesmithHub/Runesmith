using System.Reflection;
using System.Runtime.Loader;
using RunesmithHub.Protocol;

namespace Runesmith.Composition;

/// <summary>What the host process offers plugins: the .NET libraries, the host-shared assemblies and Runesmith's other assemblies.</summary>
internal static class HostRuntime
{
    private static readonly Lazy<Dictionary<string, string>> Trusted = new(ReadTrusted);
    private static readonly string FrameworkDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location) ?? "";
    private static readonly string AppDirectory = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

    /// <summary>Gets whether an assembly is one every plugin shares with Runesmith, such as the SDK or Avalonia.</summary>
    public static bool IsShared(string name) => HostAssemblies.IsHostShared(name);

    /// <summary>Gets whether an assembly is part of .NET itself.</summary>
    public static bool IsFramework(string name)
    {
        if (!Trusted.Value.TryGetValue(name, out var path))
            return false;

        var directory = Path.GetDirectoryName(path) ?? "";
        if (!string.Equals(directory, FrameworkDirectory, StringComparison.Ordinal))
            return false;

        // A self-contained build keeps .NET next to Runesmith, so only the names tell them apart there.
        return !string.Equals(FrameworkDirectory, AppDirectory, StringComparison.Ordinal)
            || name is "mscorlib" or "netstandard" or "System" or "WindowsBase" or "Microsoft.CSharp" or "Microsoft.VisualBasic"
            || name.StartsWith("System.", StringComparison.Ordinal) || name.StartsWith("Microsoft.Win32.", StringComparison.Ordinal);
    }

    /// <summary>Gets whether Runesmith itself ships an assembly, apart from .NET.</summary>
    public static bool IsHostOnly(string name) => Trusted.Value.ContainsKey(name) && !IsFramework(name) && !IsShared(name);

    /// <summary>Gets the version of the assembly the host loads for a name, or null when the host has none.</summary>
    public static Version? VersionOf(string name)
    {
        if (AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a => string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase)) is { } loaded)
            return loaded.GetName().Version;

        try
        {
            return Trusted.Value.TryGetValue(name, out var path) ? AssemblyName.GetAssemblyName(path).Version : null;
        }
        catch (Exception exception) when (exception is BadImageFormatException or IOException)
        {
            return null;
        }
    }

    private static Dictionary<string, string> ReadTrusted()
    {
        var trusted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            trusted.TryAdd(Path.GetFileNameWithoutExtension(path), path);
        return trusted;
    }
}
