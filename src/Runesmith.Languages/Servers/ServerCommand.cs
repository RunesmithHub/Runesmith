namespace Runesmith.Languages.Servers;

/// <summary>Finds a language server's executable, also when Runesmith was not started from a terminal.</summary>
internal static class ServerCommand
{
    /// <summary>Gets the full path of a command found on the <c>PATH</c>. A command that is found nowhere is returned as given.</summary>
    public static string Resolve(string command)
    {
        if (string.IsNullOrWhiteSpace(command) || Path.IsPathRooted(command) || command.Contains(Path.DirectorySeparatorChar) || command.Contains('/'))
            return command;

        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
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

    private static IEnumerable<string> Candidates(string command)
    {
        if (!OperatingSystem.IsWindows() || Path.HasExtension(command))
            return [command];

        var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries);
        return [.. extensions.Select(extension => command + extension.ToLowerInvariant()), command];
    }
}
