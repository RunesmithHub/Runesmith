namespace Runesmith.App.Tests;

public sealed class StartupSettingsTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("runesmith-startup-settings-").FullName;

    private string UserSettings => Path.Combine(folder, "settings.json");

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Theory]
    [InlineData(null, false)]
    [InlineData("{}", false)]
    [InlineData("""{ "plugins.allowLocalOverrides": false }""", false)]
    [InlineData("""{ "plugins.allowLocalOverrides": "true" }""", false)]
    [InlineData("""{ "plugins.allowLocalOverrides": true }""", true)]
    [InlineData("{ // Comments are allowed.\n \"plugins.allowLocalOverrides\": true, }", true)]
    public void LocalCopiesReplacePluginsOnlyWhenTheUsersSettingsSayTrue(string? json, bool expected)
    {
        if (json is not null)
            File.WriteAllText(UserSettings, json);

        Assert.Equal(expected, StartupSettings.AllowLocalOverrides(UserSettings));
    }
}
