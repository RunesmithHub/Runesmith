using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Runesmith.Shell.Hub;
using Runesmith.Shell.Tests.Appearance;

namespace Runesmith.Shell.Tests.Hub;

public sealed class PluginSuggestionsTests
{
    private readonly MemorySettings settings = new() { Values = { [HubSettings.DeclinedSuggestions] = "" } };
    private readonly HashSet<string> installed = new(StringComparer.Ordinal);
    private readonly List<string> shownPages = [];
    private bool canInstall = true;

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
        Assert.Equal("Install the C# plugin for completion, problems and builds.", PluginSuggestions.Plugins[0].Message);
        Assert.All(PluginSuggestions.Plugins, p => Assert.StartsWith("runesmith.", p.PluginId, StringComparison.Ordinal));
        Assert.Equal(["runesmith.csharp", "runesmith.java"], PluginSuggestions.Plugins.Select(p => p.PluginId));
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

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private PluginSuggestions Create() => new(settings, () => canInstall, installed.Contains, shownPages.Add);
}
