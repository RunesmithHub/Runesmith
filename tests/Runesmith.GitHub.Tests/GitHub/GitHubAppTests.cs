using Runesmith.GitHub.GitHub;
using Runesmith.Sdk.Settings;

namespace Runesmith.GitHub.Tests.GitHub;

public sealed class GitHubAppTests : IDisposable
{
    private static readonly GitHubAppIdentity Shipped = new(5245881, "Iv23li1DdS0IT4aRfQxi", "runesmith-editor", "Runesmith Editor");

    private readonly string folder = Directory.CreateTempSubdirectory("runesmith-github-app-").FullName;
    private readonly HttpClient http = new(new FakeServer());

    [Fact]
    public void TheSettingsClientIdWinsOverTheShippedApp()
    {
        var choice = GitHubAppChoice.Resolve(" Iv23liMine ", "my-app", Shipped);

        Assert.Equal(GitHubAppSource.Settings, choice.Source);
        Assert.Equal("Iv23liMine", choice.ClientId);
        Assert.Equal("my-app", choice.App?.Slug);
    }

    [Fact]
    public void AnOwnAppWithoutANameNeverBorrowsTheShippedAppsName()
    {
        var choice = GitHubAppChoice.Resolve("Iv23liMine", "", Shipped);

        Assert.Equal(GitHubAppSource.Settings, choice.Source);
        Assert.Null(choice.App?.Slug);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("  ", "my-app")]
    public void WithoutAClientIdSettingTheShippedAppIsUsed(string? clientId, string? slug)
    {
        var choice = GitHubAppChoice.Resolve(clientId, slug, Shipped);

        Assert.Equal(GitHubAppSource.Shipped, choice.Source);
        Assert.Equal(Shipped, choice.App);
        Assert.Equal("Iv23li1DdS0IT4aRfQxi", choice.ClientId);
    }

    [Fact]
    public void WithNeitherThereIsNoApp()
    {
        var choice = GitHubAppChoice.Resolve("", "", null);

        Assert.Equal(GitHubAppSource.None, choice.Source);
        Assert.Equal("", choice.ClientId);
    }

    [Fact]
    public void ReadsTheAppFile()
    {
        var path = Write("""{ "appId": 5245881, "clientId": "Iv23li1DdS0IT4aRfQxi", "slug": "runesmith-editor", "name": "Runesmith Editor" }""");

        Assert.Equal(Shipped, GitHubAppFile.Read(path));
    }

    [Fact]
    public void TheFileThatShipsWithThePluginNamesRunesmithsApp()
    {
        var shipped = GitHubAppFile.Read(Path.Combine(AppContext.BaseDirectory, GitHubAppFile.FileName));

        Assert.Equal(Shipped, shipped);
        Assert.Equal(Shipped, GitHubAppFile.Shipped);
    }

    [Theory]
    [InlineData("""{ "clientId": "Iv23li1DdS0IT4aRfQxi", """)]
    [InlineData("not json")]
    [InlineData("""{ "slug": "runesmith-editor" }""")]
    [InlineData("""{ "clientId": "  " }""")]
    [InlineData("""{ "clientId": 42 }""")]
    public void ABrokenFileGivesNoApp(string json) => Assert.Null(GitHubAppFile.Read(Write(json)));

    [Fact]
    public void AMissingFileGivesNoApp() => Assert.Null(GitHubAppFile.Read(Path.Combine(folder, "missing.json")));

    [Fact]
    public void TheAccountSignsInWithTheSettingThenTheShippedAppThenNone()
    {
        var values = new Dictionary<string, object> { [GitHubSettings.ClientId] = "Iv23liMine", [GitHubSettings.AppSlug] = "my-app" };
        using var own = Account(new FixedSettings(values), Shipped);
        using var shipped = Account(new FixedSettings(new() { [GitHubSettings.ClientId] = "", [GitHubSettings.AppSlug] = "" }), Shipped);
        using var none = Account(new FixedSettings(new() { [GitHubSettings.ClientId] = "", [GitHubSettings.AppSlug] = "" }), null);

        Assert.Equal("Iv23liMine", own.ClientId);
        Assert.Equal("https://github.com/apps/my-app/installations/new", own.InstallUri?.AbsoluteUri);
        Assert.Equal("Iv23li1DdS0IT4aRfQxi", shipped.ClientId);
        Assert.Equal("https://github.com/apps/runesmith-editor/installations/new", shipped.InstallUri?.AbsoluteUri);
        Assert.Equal("", none.ClientId);
        Assert.Null(none.InstallUri);
        Assert.Equal("https://github.com/settings/installations", none.ManageAccessUri.AbsoluteUri);
    }

    [Fact]
    public void GoingBackToTheShippedAppClearsTheSettings()
    {
        var settings = new FixedSettings(new() { [GitHubSettings.ClientId] = "Iv23liMine", [GitHubSettings.AppSlug] = "my-app" });
        using var account = Account(settings, Shipped);

        account.UseShippedApp();

        Assert.Null(settings.GetValue(GitHubSettings.ClientId, SettingScope.User));
        Assert.Null(settings.GetValue(GitHubSettings.AppSlug, SettingScope.User));
        Assert.Equal(GitHubAppSource.Shipped, account.App.Source);
    }

    private GitHubAccount Account(ISettingsService settings, GitHubAppIdentity? shipped) =>
        new(http, settings, new MemorySecretStore(), null, null, TimeProvider.System, shippedApp: shipped);

    private string Write(string json)
    {
        var path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        return path;
    }

    public void Dispose()
    {
        http.Dispose();
        Directory.Delete(folder, recursive: true);
    }
}
