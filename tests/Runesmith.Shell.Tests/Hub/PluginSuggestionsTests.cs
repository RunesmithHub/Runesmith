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
    [InlineData("/repo/src/main.go", "acme.go")]
    [InlineData("/repo/go.mod", "acme.go")]
    [InlineData("/repo/Shop.GO", "acme.go")]
    [InlineData("/repo/src/lib.rs", "acme.rust")]
    [InlineData("/repo/Dockerfile", "acme.docker")]
    [InlineData("/repo/README.md", null)]
    [InlineData("/repo/script.gox", null)]
    [InlineData("/repo/go.mod.orig", null)]
    [InlineData(null, null)]
    public void SuggestsTheOfficialPluginWhoseKeywordsNameTheFile(string? path, string? expected) =>
        Assert.Equal(expected, Create().For(path)?.PluginId);

    [Fact]
    public void SuggestsEachPluginOnlyOnceInASession()
    {
        var suggestions = Create();

        Assert.Equal("acme.go", suggestions.For("/repo/main.go")?.PluginId);
        Assert.Null(suggestions.For("/repo/other.go"));
        Assert.Null(suggestions.For("/repo/go.mod"));
        Assert.Equal("acme.rust", suggestions.For("/repo/lib.rs")?.PluginId);
        Assert.Null(suggestions.For("/repo/main.rs"));
        Assert.Equal("acme.go", Create().For("/repo/main.go")?.PluginId);
    }

    [Fact]
    public void SuggestsNothingWhileThePluginIsInstalledOrTheHubCannotInstallIt()
    {
        var suggestions = Create();
        installed.Add("acme.go");
        canInstall = false;

        Assert.Null(suggestions.For("/repo/main.go"));
        Assert.Null(suggestions.For("/repo/lib.rs"));

        canInstall = true;

        Assert.Null(suggestions.For("/repo/main.go"));
        Assert.Equal("acme.rust", suggestions.For("/repo/lib.rs")?.PluginId);
    }

    [Fact]
    public void InstallOpensThePluginsPageAndDeclineKeepsItQuietInLaterSessions()
    {
        var suggestions = Create();
        var go = suggestions.For("/repo/main.go")!;

        suggestions.Install(go);
        suggestions.StopSuggesting(go);
        suggestions.StopSuggesting(go);

        Assert.Equal(["acme.go"], shownPages);
        Assert.Equal("acme.go", settings.Values[HubSettings.DeclinedSuggestions]);
        Assert.Null(Create().For("/repo/main.go"));
        Assert.Equal("acme.rust", Create().For("/repo/lib.rs")?.PluginId);
    }

    [Fact]
    public void SaysWhatThePluginWorksWith() =>
        Assert.Equal("The Go plugin on the hub works with .go. Install it?", Create().For("/repo/main.go")!.Message);

    [Fact]
    public Task TheBarInstallsOrStopsSuggestingAndClosesEitherWay() => HeadlessSession.Value.Dispatch(() =>
    {
        var suggestions = Create();
        var go = suggestions.For("/repo/main.go")!;
        var closed = 0;

        var bar = PluginSuggestionBar.Create(go, suggestions, () => closed++);
        var window = new Window { Content = bar };
        window.Show();
        var buttons = bar.GetVisualDescendants().OfType<Button>().ToList();
        Assert.Contains(bar.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == go.Message);
        Click(buttons.Single(b => b.Content is TextBlock { Text: "Install" }));
        Click(buttons.Single(b => b.Content is TextBlock { Text: "Don't suggest again" }));
        window.Close();

        Assert.Equal(2, closed);
        Assert.Equal(["acme.go"], shownPages);
        Assert.Equal("acme.go", settings.Values[HubSettings.DeclinedSuggestions]);
    }, CancellationToken.None);

    [Fact]
    public void AFolderWithNothingAPluginIsForGetsNoSuggestion()
    {
        File.WriteAllText(Path.Combine(folder, "README.md"), "");
        Directory.CreateDirectory(Path.Combine(folder, "src", "App"));
        File.WriteAllText(Path.Combine(folder, "src", "App", "main.go"), "");
        File.WriteAllText(Path.Combine(folder, "go.mod.orig"), "");

        Assert.Null(Create().ForFolder(folder));
        Assert.Null(Create().ForFolder(null));
    }

    [Theory]
    [InlineData("go.mod", "acme.go")]
    [InlineData("main.GO", "acme.go")]
    [InlineData("lib.rs", "acme.rust")]
    [InlineData("Dockerfile", "acme.docker")]
    public void AFileAtTheTopSuggestsItsPlugin(string file, string expected)
    {
        File.WriteAllText(Path.Combine(folder, file), "");
        var suggestions = Create();

        Assert.Equal(expected, suggestions.ForFolder(folder)?.PluginId);
        Assert.Null(suggestions.ForFolder(folder));
        Assert.Null(suggestions.For(Path.Combine(folder, file)));
    }

    [Fact]
    public void AFolderAtTheTopSuggestsItsPluginAndFolderSuggestionsFollowTheSameRules()
    {
        Repository();
        File.WriteAllText(Path.Combine(folder, "go.mod"), "");
        File.WriteAllText(Path.Combine(folder, "lib.rs"), "");
        settings.Values[HubSettings.DeclinedSuggestions] = "acme.rust";
        var suggestions = Create();

        Assert.Equal("acme.vcs", suggestions.ForFolder(folder)?.PluginId);
        Assert.Equal("acme.go", suggestions.ForFolder(folder)?.PluginId);
        Assert.Null(suggestions.ForFolder(folder));

        installed.Add("acme.vcs");
        installed.Add("acme.go");
        settings.Values[HubSettings.DeclinedSuggestions] = "";
        Assert.Equal("acme.rust", Create().ForFolder(folder)?.PluginId);
        canInstall = false;
        Assert.Null(Create().ForFolder(folder));
    }

    [Fact]
    public Task InstallOnTheFolderLineOpensThePluginsPage() => HeadlessSession.Value.Dispatch(() =>
    {
        Repository();
        var suggestions = Create();
        var vcs = suggestions.ForFolder(folder)!;

        var bar = PluginSuggestionBar.Create(vcs, suggestions, () => { });
        var window = new Window { Content = bar };
        window.Show();
        Assert.Contains(bar.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == vcs.Message);
        Click(bar.GetVisualDescendants().OfType<Button>().Single(b => b.Content is TextBlock { Text: "Install" }));
        window.Close();

        Assert.Equal(["acme.vcs"], shownPages);
    }, CancellationToken.None);

    // A folder of a made-up version control system, which the plugin with the keyword vcs is for.
    private void Repository() => Directory.CreateDirectory(Path.Combine(folder, ".vcs"));

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private PluginSuggestions Create() => new(settings, new Runesmith.Languages.LanguageRegistry([]), () => canInstall, () => Official, installed.Contains, shownPages.Add);

    private static readonly OfficialPlugin[] Official =
    [
        new("acme.docker", "Docker", ["dockerfile"]),
        new("acme.go", "Go", ["go", "mod"]),
        new("acme.rust", "Rust", ["rs"]),
        new("acme.vcs", "Version Control", ["vcs"]),
    ];
}
