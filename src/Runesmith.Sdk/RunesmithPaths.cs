namespace Runesmith.Sdk;

/// <summary>Where Runesmith keeps its files on this computer.</summary>
/// <remarks>Set the <c>RUNESMITH_HOME</c> environment variable to keep everything in one other folder, such as for a portable copy.</remarks>
public static class RunesmithPaths
{
    /// <summary>Gets the folder of the user's settings, key bindings and plugins: <c>%APPDATA%\Runesmith</c> on Windows,
    /// <c>~/Library/Application Support/Runesmith</c> on macOS and <c>$XDG_CONFIG_HOME/runesmith</c> on Linux.</summary>
    public static string Config { get; } = Environment.GetEnvironmentVariable("RUNESMITH_HOME") is { Length: > 0 } home
        ? Path.GetFullPath(home)
        : OperatingSystem.IsLinux()
            ? Path.Combine(XdgDirectory("XDG_CONFIG_HOME", ".config"), "runesmith")
            : OperatingSystem.IsMacOS()
                ? Path.Combine(Home, "Library", "Application Support", "Runesmith")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Runesmith");

    /// <summary>Gets the folder of files Runesmith can rebuild, such as the composition cache.</summary>
    public static string Cache { get; } = Environment.GetEnvironmentVariable("RUNESMITH_HOME") is { Length: > 0 }
        ? Path.Combine(Config, "cache")
        : OperatingSystem.IsLinux()
            ? Path.Combine(XdgDirectory("XDG_CACHE_HOME", ".cache"), "runesmith")
            : OperatingSystem.IsMacOS()
                ? Path.Combine(Home, "Library", "Caches", "Runesmith")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Runesmith", "Cache");

    /// <summary>Gets the folder of large files Runesmith downloads, such as SDKs: <c>%LOCALAPPDATA%\Runesmith</c> on Windows,
    /// <c>~/Library/Application Support/Runesmith</c> on macOS and <c>$XDG_DATA_HOME/runesmith</c> on Linux.</summary>
    public static string Data { get; } = Environment.GetEnvironmentVariable("RUNESMITH_HOME") is { Length: > 0 }
        ? Path.Combine(Config, "data")
        : OperatingSystem.IsLinux()
            ? Path.Combine(XdgDirectory("XDG_DATA_HOME", Path.Combine(".local", "share")), "runesmith")
            : OperatingSystem.IsMacOS()
                ? Path.Combine(Home, "Library", "Application Support", "Runesmith")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Runesmith");

    /// <summary>Gets the folder SDKs Runesmith installs go in, one subfolder per kind.</summary>
    public static string Sdks => Path.Combine(Data, "sdks");

    /// <summary>Gets the user's settings file.</summary>
    public static string UserSettings => Path.Combine(Config, "settings.json");

    /// <summary>Gets the folder the user installs plugins in; each plugin is a subfolder with a <c>plugin.json</c>.</summary>
    public static string UserPlugins => Path.Combine(Config, "plugins");

    /// <summary>Gets the folder where Runesmith remembers windows, layouts and open files between runs.</summary>
    public static string State => Path.Combine(Config, "state");

    /// <summary>Gets the folder of the log files.</summary>
    public static string Logs => Path.Combine(Cache, "logs");

    /// <summary>The folder inside an open folder that holds its Runesmith settings.</summary>
    public const string WorkspaceFolderName = ".runesmith";

    /// <summary>Gets the settings file of an open folder.</summary>
    public static string WorkspaceSettings(string rootPath) => Path.Combine(rootPath, WorkspaceFolderName, "settings.json");

    // Spelled out because .NET maps the special folders on macOS to Unix-style folders that Mac apps do not use.
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string XdgDirectory(string variable, string fallback) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value && Path.IsPathRooted(value)
            ? value
            : Path.Combine(Home, fallback);
}
