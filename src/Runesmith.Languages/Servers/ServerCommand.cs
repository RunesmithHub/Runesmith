using System.Runtime.InteropServices;

namespace Runesmith.Languages.Servers;

/// <summary>Finds a language server's executable and the environment it needs, also when Runesmith was not started from a terminal.</summary>
internal static class ServerCommand
{
    /// <summary>Gets the full path of a command: found on the <c>PATH</c>, or in the .NET global tools folder, where
    /// <c>dotnet tool install --global</c> puts servers such as csharp-ls. A command that is found nowhere is returned as given.</summary>
    public static string Resolve(string command)
    {
        if (string.IsNullOrWhiteSpace(command) || Path.IsPathRooted(command) || command.Contains(Path.DirectorySeparatorChar) || command.Contains('/'))
            return command;

        var folders = (System.Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
        folders.Add(GlobalToolsFolder());
        foreach (var folder in folders)
        {
            foreach (var name in Candidates(command))
            {
                var path = Path.Combine(folder, name);
                if (File.Exists(path))
                    return path;
            }
        }

        return command;
    }

    /// <summary>Gets the variables a server started by Runesmith needs: <c>DOTNET_ROOT</c>, so .NET tools find the runtime Runesmith runs on
    /// when it is not where they look by default, as on NixOS.</summary>
    public static IReadOnlyDictionary<string, string> Environment()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("DOTNET_ROOT")) && DotnetRoot() is { } root)
            variables["DOTNET_ROOT"] = root;
        return variables;
    }

    private static string GlobalToolsFolder()
    {
        var home = System.Environment.GetEnvironmentVariable("DOTNET_CLI_HOME") is { Length: > 0 } cliHome
            ? cliHome
            : System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".dotnet", "tools");
    }

    private static IEnumerable<string> Candidates(string command)
    {
        if (!OperatingSystem.IsWindows() || Path.HasExtension(command))
            return [command];

        var extensions = (System.Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries);
        return [.. extensions.Select(extension => command + extension.ToLowerInvariant()), command];
    }

    // The runtime lives in <root>/shared/Microsoft.NETCore.App/<version>; a self-contained Runesmith has no such root, so the dotnet on the
    // PATH decides instead.
    private static string? DotnetRoot()
    {
        var runtime = RuntimeEnvironment.GetRuntimeDirectory().TrimEnd(Path.DirectorySeparatorChar);
        var root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(runtime)));
        if (root is not null && IsDotnetRoot(root))
            return root;

        var dotnet = Resolve(OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        if (!Path.IsPathRooted(dotnet))
            return null;

        var target = File.ResolveLinkTarget(dotnet, returnFinalTarget: true)?.FullName ?? dotnet;
        var folder = Path.GetDirectoryName(target);
        return folder is not null && IsDotnetRoot(folder) ? folder : null;
    }

    private static bool IsDotnetRoot(string folder) =>
        Directory.Exists(Path.Combine(folder, "host", "fxr")) && Directory.Exists(Path.Combine(folder, "shared", "Microsoft.NETCore.App"));
}
