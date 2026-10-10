using System.Composition;
using Runesmith.Sdk.Settings;

namespace Runesmith.Shell.Hub;

/// <summary>An official plugin on the hub that Runesmith suggests, and the files that tell it to.</summary>
/// <param name="PluginId">The plugin's id on the hub.</param>
/// <param name="Name">The plugin's name, as the suggestion shows it.</param>
/// <param name="Brings">What the plugin adds, as the suggestion says it.</param>
public sealed record SuggestedPlugin(string PluginId, string Name, string Brings)
{
    /// <summary>Gets the file extensions, with their dot, that tell Runesmith to suggest it.</summary>
    public IReadOnlyList<string> Extensions { get; init; } = [];

    /// <summary>Gets the whole file names, such as <c>pom.xml</c>, that tell Runesmith to suggest it.</summary>
    public IReadOnlyList<string> FileNames { get; init; } = [];

    /// <summary>Gets the patterns, such as <c>*.csproj</c>, of the files that tell Runesmith to suggest it for a folder that has one at its top.</summary>
    public IReadOnlyList<string> FolderFiles { get; init; } = [];

    /// <summary>Gets what the suggestion says.</summary>
    public string Message => $"Install the {Name} plugin for {Brings}.";

    /// <summary>Gets whether a file is one that tells Runesmith to suggest it.</summary>
    public bool Matches(string filePath)
    {
        var name = Path.GetFileName(filePath);
        return FileNames.Contains(name, StringComparer.OrdinalIgnoreCase)
            || Extensions.Any(extension => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Suggests installing an official plugin that is not installed: a language's when the user opens one of its files or a folder with
/// its project files at the top, Git's when the open folder is a Git repository and GitHub's when it has a remote on github.com. Each is
/// suggested once in a session, and never again once the user said not to.</summary>
[Export]
[Shared]
public sealed class PluginSuggestions
{
    private const string LanguageFeatures = "completion, problems and builds";

    /// <summary>The official language plugins Runesmith suggests for files.</summary>
    public static IReadOnlyList<SuggestedPlugin> LanguagePlugins { get; } =
    [
        new("runesmith.csharp", "C#", LanguageFeatures)
        {
            Extensions = [".cs", ".csproj", ".sln", ".slnx", ".razor"],
            FolderFiles = ["*.sln", "*.slnx", "*.csproj"],
        },
        new("runesmith.java", "Java", LanguageFeatures)
        {
            Extensions = [".java"],
            FileNames = ["pom.xml", "build.gradle", "build.gradle.kts"],
            FolderFiles = ["pom.xml", "build.gradle", "build.gradle.kts"],
        },
    ];

    /// <summary>The Git plugin, suggested for a folder that is a Git repository.</summary>
    public static SuggestedPlugin Git { get; } = new("runesmith.git", "Git", "commits, branches, diffs and the log");

    /// <summary>The GitHub plugin, suggested for a repository with a remote on github.com.</summary>
    public static SuggestedPlugin GitHub { get; } = new("runesmith.github", "GitHub", "pull requests, reviews and checks");

    private readonly ISettingsService settings;
    private readonly Func<bool> canInstall;
    private readonly Func<string, bool> isInstalled;
    private readonly Action<string> showPage;
    private readonly HashSet<string> suggested = new(StringComparer.Ordinal);

    [ImportingConstructor]
    public PluginSuggestions(ISettingsService settings, Lazy<HubService> hub)
        : this(
            settings,
            () => hub.Value.Client.Status.HasCatalog,
            id => hub.Value.Model.Installed().Any(row => string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase)),
            id => hub.Value.ShowPage(id))
    {
    }

    /// <param name="canInstall">Whether the hub is set up and turned on.</param>
    /// <param name="isInstalled">Whether a plugin is installed, from anywhere and turned on or off.</param>
    /// <param name="showPage">Opens a plugin's page in the plugin manager.</param>
    internal PluginSuggestions(ISettingsService settings, Func<bool> canInstall, Func<string, bool> isInstalled, Action<string> showPage)
    {
        this.settings = settings;
        this.canInstall = canInstall;
        this.isInstalled = isInstalled;
        this.showPage = showPage;
    }

    /// <summary>Gets the plugin to suggest for a file the user opened, or null, and counts it as suggested for the session.</summary>
    public SuggestedPlugin? For(string? filePath) =>
        filePath is not null && LanguagePlugins.FirstOrDefault(p => p.Matches(filePath)) is { } plugin ? Suggest(plugin) : null;

    /// <summary>Gets the next plugin to suggest for the open folder, or null, and counts it as suggested for the session: Git when the folder is
    /// a Git repository, then GitHub when the repository has a remote on github.com, then the language plugins of the project files at its
    /// top.</summary>
    public SuggestedPlugin? ForFolder(string? folder)
    {
        if (folder is null)
            return null;

        if (GitFolder.IsRepository(folder) && (Suggest(Git) ?? (GitFolder.HasGitHubRemote(folder) ? Suggest(GitHub) : null)) is { } git)
            return git;

        foreach (var plugin in LanguagePlugins)
        {
            if (HasFolderFile(folder, plugin) && Suggest(plugin) is { } language)
                return language;
        }

        return null;
    }

    /// <summary>Opens the plugin's page in the plugin manager, where the user can install it.</summary>
    public void Install(SuggestedPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        showPage(plugin.PluginId);
    }

    /// <summary>Stops suggesting the plugin, in this session and later ones.</summary>
    public void StopSuggesting(SuggestedPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        var declined = Declined();
        if (declined.Add(plugin.PluginId))
            settings.Set(HubSettings.DeclinedSuggestions, string.Join(';', declined.Order(StringComparer.Ordinal)));
    }

    private SuggestedPlugin? Suggest(SuggestedPlugin plugin)
    {
        if (suggested.Contains(plugin.PluginId) || Declined().Contains(plugin.PluginId) || !canInstall() || isInstalled(plugin.PluginId))
            return null;

        suggested.Add(plugin.PluginId);
        return plugin;
    }

    private HashSet<string> Declined() =>
        settings.Get<string>(HubSettings.DeclinedSuggestions).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool HasFolderFile(string folder, SuggestedPlugin plugin)
    {
        var options = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive, MatchType = MatchType.Simple, IgnoreInaccessible = true };
        try
        {
            return plugin.FolderFiles.Any(pattern => Directory.EnumerateFiles(folder, pattern, options).Any(plugin.Matches));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
