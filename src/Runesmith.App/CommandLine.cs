using Runesmith.Hub.Links;

namespace Runesmith.App;

/// <summary>What Runesmith was started with on the command line.</summary>
/// <param name="Paths">Folders and files to open; a file can end in <c>:line</c> or <c>:line:column</c>.</param>
/// <param name="Link">A <c>runesmith://</c> link to open, as given; it is checked before anything opens.</param>
internal sealed record CommandLine(
    IReadOnlyList<string> Paths,
    bool ShowHelp,
    bool ShowVersion,
    bool IsDiagnosticsEnabled,
    bool IsNewInstance,
    bool ArePluginsDisabled,
    string? Error)
{
    public const string Usage = """
        Usage: runesmith [options] [paths...] [link]

        Opens folders and files in Runesmith. A file can end in :line or :line:column, such as Program.cs:12:5.
        A runesmith:// link, such as runesmith://hub/plugin/lumen.todo, opens that plugin's page.
        When Runesmith is running already, the paths and the link open in its window.

        Options:
          -h, --help           Show this help.
          -v, --version        Show the version.
          --diagnostics        Log startup timings and the loaded plugins to the Diagnostics output channel.
          --new-instance       Start another Runesmith instead of using the running one.
          --disable-plugins    Start without the plugins from the hub and the user plugins folder.
          --safe-mode          Start in safe mode: only the plugins that come with Runesmith load.
        """;

    public bool IsSafeMode { get; init; }

    public string? Link { get; init; }

    public static CommandLine Parse(IReadOnlyList<string> arguments)
    {
        var paths = new List<string>();
        string? link = null;
        bool help = false, version = false, diagnostics = false, newInstance = false, noPlugins = false, safeMode = false;
        var onlyPaths = false;
        foreach (var argument in arguments)
        {
            if (!onlyPaths && HubLink.LooksLikeLink(argument))
            {
                link ??= argument;
                continue;
            }

            if (onlyPaths || !argument.StartsWith('-'))
            {
                paths.Add(LocalPath(argument));
                continue;
            }

            switch (argument)
            {
                case "--":
                    onlyPaths = true;
                    break;
                case "-h" or "--help" or "/?":
                    help = true;
                    break;
                case "-v" or "--version":
                    version = true;
                    break;
                case "--diagnostics":
                    diagnostics = true;
                    break;
                case "--new-instance":
                    newInstance = true;
                    break;
                case "--disable-plugins":
                    noPlugins = true;
                    break;
                case "--safe-mode":
                    safeMode = true;
                    break;
                default:
                    return new CommandLine([], true, false, false, false, false, $"Unknown option: {argument}");
            }
        }

        return new CommandLine(paths, help, version, diagnostics, newInstance, noPlugins || safeMode, null) { IsSafeMode = safeMode, Link = link };
    }

    // Menu entries pass files as file:// addresses, since the same entry also receives runesmith:// links.
    private static string LocalPath(string argument) =>
        argument.StartsWith("file://", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(argument, UriKind.Absolute, out var uri) && uri.IsFile
            ? uri.LocalPath
            : argument;
}
