using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Runesmith.Shell.Hub;
using Runesmith.Shell.Tests.Appearance;

namespace Runesmith.Shell.Tests.Hub;

public sealed class PluginSuggestionsTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("runesmith-suggestions-").FullName;
    private readonly MemorySettings settings = new() { Values = { [HubSettings.DeclinedSuggestions] = "" } };
    private readonly HashSet<string> installed = new(StringComparer.Ordinal);
    private readonly List<string> shownPages = [];
    private bool canInstall = true;

    public void Dispose() => TestFolders.Delete(folder);

    [Theory]
    [InlineData("/repo/src/Program.cs", "runesmith.csharp")]
    [InlineData("/repo/App/App.csproj", "runesmith.csharp")]
    [InlineData("/repo/Shop.SLN", "runesmith.csharp")]
    [InlineData("/repo/Shop.slnx", "runesmith.csharp")]
    [InlineData("/repo/Pages/Index.razor", "runesmith.csharp")]
    [InlineData("/repo/src/main/java/Main.java", "runesmith.java")]
    [InlineData("/repo/pom.xml", "runesmith.java")]
    [InlineData("/repo/build.gradle", "runesmith.java")]
    [InlineData("/repo/app/build.gradle.kts", "runesmith.java")]
    [InlineData("/repo/README.md", null)]
    [InlineData("/repo/script.csx", null)]
    [InlineData("/repo/pom.xml.orig", null)]
    [InlineData("/repo/settings.gradle", null)]
    [InlineData(null, null)]
    public void SuggestsTheOfficialPluginOfTheFilesLanguage(string? path, string? expected) =>
        Assert.Equal(expected, Create().For(path)?.PluginId);

    [Fact]
    public void SuggestsEachLanguageOnlyOnceInASession()
    {
        var suggestions = Create();

        Assert.Equal("runesmith.csharp", suggestions.For("/repo/Program.cs")?.PluginId);
        Assert.Null(suggestions.For("/repo/Other.cs"));
        Assert.Null(suggestions.For("/repo/Shop.sln"));
        Assert.Equal("runesmith.java", suggestions.For("/repo/pom.xml")?.PluginId);
        Assert.Null(suggestions.For("/repo/Main.java"));
        Assert.Equal("runesmith.csharp", Create().For("/repo/Program.cs")?.PluginId);
    }

    [Fact]
    public void SuggestsNothingWhileThePluginIsInstalledOrTheHubCannotInstallIt()
    {
        var suggestions = Create();
        installed.Add("runesmith.csharp");
        canInstall = false;

        Assert.Null(suggestions.For("/repo/Program.cs"));
        Assert.Null(suggestions.For("/repo/Main.java"));

        canInstall = true;

        Assert.Null(suggestions.For("/repo/Program.cs"));
        Assert.Equal("runesmith.java", suggestions.For("/repo/Main.java")?.PluginId);
    }

    [Fact]
    public void InstallOpensThePluginsPageAndDeclineKeepsItQuietInLaterSessions()
    {
        var suggestions = Create();
        var csharp = suggestions.For("/repo/Program.cs")!;

        suggestions.Install(csharp);
        suggestions.StopSuggesting(csharp);
        suggestions.StopSuggesting(csharp);

        Assert.Equal(["runesmith.csharp"], shownPages);
        Assert.Equal("runesmith.csharp", settings.Values[HubSettings.DeclinedSuggestions]);
        Assert.Null(Create().For("/repo/Program.cs"));
        Assert.Equal("runesmith.java", Create().For("/repo/Main.java")?.PluginId);
    }

    [Fact]
    public void SaysWhatThePluginBringsAndNamesOnlyOfficialPlugins()
    {
        Assert.Equal("Install the C# plugin for completion, problems and builds.", PluginSuggestions.LanguagePlugins[0].Message);
        Assert.All(PluginSuggestions.LanguagePlugins.Concat([PluginSuggestions.Git, PluginSuggestions.GitHub]), p => Assert.StartsWith("runesmith.", p.PluginId, StringComparison.Ordinal));
        Assert.Equal(["runesmith.csharp", "runesmith.java"], PluginSuggestions.LanguagePlugins.Select(p => p.PluginId));
        Assert.Equal("Install the Git plugin for commits, branches, diffs and the log.", PluginSuggestions.Git.Message);
        Assert.Equal("Install the GitHub plugin for pull requests, reviews and checks.", PluginSuggestions.GitHub.Message);
    }

    [Fact]
    public Task TheBarInstallsOrStopsSuggestingAndClosesEitherWay() => HeadlessSession.Value.Dispatch(() =>
    {
        var suggestions = Create();
        var csharp = suggestions.For("/repo/Program.cs")!;
        var closed = 0;

        var bar = PluginSuggestionBar.Create(csharp, suggestions, () => closed++);
        var window = new Window { Content = bar };
        window.Show();
        var buttons = bar.GetVisualDescendants().OfType<Button>().ToList();
        Assert.Contains(bar.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == csharp.Message);
        Click(buttons.Single(b => b.Content is TextBlock { Text: "Install" }));
        Click(buttons.Single(b => b.Content is TextBlock { Text: "Don't suggest again" }));
        window.Close();

        Assert.Equal(2, closed);
        Assert.Equal(["runesmith.csharp"], shownPages);
        Assert.Equal("runesmith.csharp", settings.Values[HubSettings.DeclinedSuggestions]);
    }, CancellationToken.None);

    [Fact]
    public void AFolderThatIsNoRepositoryAndNoProjectGetsNoSuggestion()
    {
        File.WriteAllText(Path.Combine(folder, "README.md"), "");
        Directory.CreateDirectory(Path.Combine(folder, "src", "App"));
        File.WriteAllText(Path.Combine(folder, "src", "App", "App.csproj"), "");
        File.WriteAllText(Path.Combine(folder, "pom.xml.orig"), "");

        Assert.Null(Create().ForFolder(folder));
        Assert.Null(Create().ForFolder(null));
    }

    [Theory]
    [InlineData("Shop.sln", "runesmith.csharp")]
    [InlineData("Shop.SLNX", "runesmith.csharp")]
    [InlineData("App.csproj", "runesmith.csharp")]
    [InlineData("pom.xml", "runesmith.java")]
    [InlineData("build.gradle", "runesmith.java")]
    [InlineData("build.gradle.kts", "runesmith.java")]
    public void AProjectFileAtTheTopSuggestsItsLanguage(string file, string expected)
    {
        File.WriteAllText(Path.Combine(folder, file), "");
        var suggestions = Create();

        Assert.Equal(expected, suggestions.ForFolder(folder)?.PluginId);
        Assert.Null(suggestions.ForFolder(folder));
        Assert.Null(suggestions.For(Path.Combine(folder, file)));
    }

    [Fact]
    public void ARepositorySuggestsGitBeforeItsLanguagesAndFolderLanguagesFollowTheSameRules()
    {
        Repository("");
        File.WriteAllText(Path.Combine(folder, "Shop.slnx"), "");
        File.WriteAllText(Path.Combine(folder, "pom.xml"), "");
        settings.Values[HubSettings.DeclinedSuggestions] = "runesmith.java";
        var suggestions = Create();

        Assert.Equal("runesmith.git", suggestions.ForFolder(folder)?.PluginId);
        Assert.Equal("runesmith.csharp", suggestions.ForFolder(folder)?.PluginId);
        Assert.Null(suggestions.ForFolder(folder));

        installed.Add("runesmith.git");
        installed.Add("runesmith.csharp");
        settings.Values[HubSettings.DeclinedSuggestions] = "";
        Assert.Equal("runesmith.java", Create().ForFolder(folder)?.PluginId);
        canInstall = false;
        Assert.Null(Create().ForFolder(folder));
    }

    [Fact]
    public void ARepositorySuggestsGitAndOneOnGitHubSuggestsGitHubAfterIt()
    {
        Repository("[remote \"origin\"]\n\turl = git@github.com:RunesmithHub/plugin-git.git\n");
        var suggestions = Create();

        Assert.Equal("runesmith.git", suggestions.ForFolder(folder)?.PluginId);
        Assert.Equal("runesmith.github", suggestions.ForFolder(folder)?.PluginId);
        Assert.Null(suggestions.ForFolder(folder));
    }

    [Fact]
    public void ARepositoryWithoutAGitHubRemoteSuggestsOnlyGit()
    {
        Repository("[remote \"origin\"]\n\turl = https://gitlab.com/acme/shop.git\n");
        var suggestions = Create();

        Assert.Equal("runesmith.git", suggestions.ForFolder(folder)?.PluginId);
        Assert.Null(suggestions.ForFolder(folder));
    }

    [Fact]
    public void FolderSuggestionsFollowTheSameRulesAsLanguageOnes()
    {
        Repository("[remote \"origin\"]\n\turl = https://github.com/RunesmithHub/plugin-git\n");
        installed.Add("runesmith.git");
        settings.Values[HubSettings.DeclinedSuggestions] = "runesmith.github";

        Assert.Null(Create().ForFolder(folder));

        settings.Values[HubSettings.DeclinedSuggestions] = "";
        canInstall = false;

        Assert.Null(Create().ForFolder(folder));

        canInstall = true;

        Assert.Equal("runesmith.github", Create().ForFolder(folder)?.PluginId);
    }

    [Fact]
    public Task InstallOnTheFolderLineOpensGitsPage() => HeadlessSession.Value.Dispatch(() =>
    {
        Repository("");
        var suggestions = Create();
        var git = suggestions.ForFolder(folder)!;

        var bar = PluginSuggestionBar.Create(git, suggestions, () => { });
        var window = new Window { Content = bar };
        window.Show();
        Assert.Contains(bar.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == git.Message);
        Click(bar.GetVisualDescendants().OfType<Button>().Single(b => b.Content is TextBlock { Text: "Install" }));
        window.Close();

        Assert.Equal(["runesmith.git"], shownPages);
    }, CancellationToken.None);

    private void Repository(string config)
    {
        Directory.CreateDirectory(Path.Combine(folder, ".git"));
        File.WriteAllText(Path.Combine(folder, ".git", "config"), config);
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private PluginSuggestions Create() => new(settings, () => canInstall, installed.Contains, shownPages.Add);
}
